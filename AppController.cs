using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace HWWidget;

/// <summary>Owns the tray icon, the widget windows and the app-level bits (autostart,
/// global hotkey, render mode). One process, N widgets, one shared sampler.</summary>
sealed class AppController : IDisposable
{
    public static AppController Current { get; private set; } = null!;

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppValue = "HWWidget";
    const int HotkeyId = 1;
    const int WM_HOTKEY = 0x0312;

    readonly List<MainWindow> _windows = new();
    TrayIcon? _tray;
    HwndSource? _hotkeySink;
    readonly DispatcherTimer _trayClickTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };

    public AppController() => Current = this;

    public IReadOnlyList<MainWindow> Widgets => _windows;

    public void Start()
    {
        ApplyRenderMode();
        HealStartupPath();

        var ids = WidgetConfig.Instances();
        if (ids.Count == 0) ids.Add("main");
        foreach (var id in ids) Open(WidgetConfig.Load(id));

        _tray = new TrayIcon(this);
        RegisterHotkey();
        SensorHub.Start(_windows.Count > 0 ? _windows[0].IntervalSeconds : 1);
        CheckUpdatesOnStartup();
    }

    /// <summary>Controllo aggiornamenti all'avvio: silenzioso se non c'è nulla di nuovo.</summary>
    async void CheckUpdatesOnStartup()
    {
        try
        {
            var keys = AiKeys.Load();
            if (!keys.CheckUpdatesOnStartup || keys.GitHub.Length == 0) return;
            await Task.Delay(8000);                       // lascia respirare l'avvio
            var info = await Updater.CheckAsync(keys.GitHub);
            if (info != null && Updater.IsNewer(info)) UpdateWindow.Start(info, keys.GitHub);
        }
        catch { }
    }

    MainWindow Open(WidgetConfig cfg)
    {
        var w = new MainWindow(cfg);
        w.CloseRequested += Remove;
        w.Closed += (_, _) => _windows.Remove(w);
        _windows.Add(w);
        w.Show();
        return w;
    }

    public void NewWidget(string? preset)
    {
        string id = WidgetConfig.NewId();
        var c = new WidgetConfig { Id = id };
        switch (preset)
        {
            case "cpu": c.ShowNet = false; c.ShowGpu = false; c.ShowRam = false; c.Layout = "tiles"; break;
            case "gpu": c.ShowNet = false; c.ShowCpu = false; c.ShowRam = false; c.Layout = "tiles"; break;
            case "ram": c.ShowNet = false; c.ShowCpu = false; c.ShowGpu = false; c.Layout = "tiles"; break;
            case "net": c.ShowCpu = false; c.ShowGpu = false; c.ShowRam = false; break;
            case "cpugpu": c.ShowNet = false; c.ShowRam = false; c.Layout = "cards"; break;
            case "ai":
                c.ShowNet = c.ShowCpu = c.ShowGpu = c.ShowRam = c.ShowDisk = false;
                c.ShowAi = true;
                c.Layout = "panelgraph";
                break;
        }
        c.Sanitized().Save();
        Open(c);
    }

    void Remove(MainWindow w)
    {
        w.CloseRequested -= Remove;
        string id = w.InstanceId;
        w.Close();
        _windows.Remove(w);
        var cfg = WidgetConfig.Load(id);
        cfg.Closed = true;      // keeps position/size/layout for later
        cfg.Save();
    }

    public void ToggleAllVisible()
    {
        bool anyVisible = _windows.Any(w => w.IsVisible);
        foreach (var w in _windows) w.SetVisible(!anyVisible);
    }

    public void ToggleHudAll()
    {
        foreach (var w in _windows) w.ToggleClickThrough();
    }

    public bool AnyHud => _windows.Any(w => w.Config.ClickThrough);

    public void SetHudAll(bool on)
    {
        foreach (var w in _windows)
            if (w.Config.ClickThrough != on) w.SetClickThrough(on);
        if (on) _tray?.WarnHud();
    }

    public void ExitAll()
    {
        _hub?.Close();
        foreach (var w in _windows.ToList())
        {
            w.CloseRequested -= Remove;
            w.Close();
        }
        _windows.Clear();
        Dispose();
        Application.Current.Shutdown();
    }

    ControlHub? _hub;

    public void ShowHub()
    {
        if (_hub is { IsLoaded: true })
        {
            _hub.Activate();
            return;
        }
        _hub = new ControlHub(this);
        _hub.Closed += (_, _) => _hub = null;
        _hub.Show();
        _hub.Activate();
    }

    // ---------- autostart ----------

    public bool StartupEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(AppValue) != null;
        }
    }

    public void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on)
            {
                string exe = Environment.ProcessPath ?? "";
                k.SetValue(AppValue, $"\"{exe}\"");
            }
            else k.DeleteValue(AppValue, false);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossibile modificare l'avvio automatico: " + ex.Message);
        }
    }

    /// <summary>If the exe moved, point the logon entry at the current path.</summary>
    static void HealStartupPath()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return;
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue(AppValue) is string cur && cur != $"\"{exe}\"")
                k.SetValue(AppValue, $"\"{exe}\"");
        }
        catch { }
    }

    // ---------- rendering mode (NVIDIA overlay workaround) ----------

    public void ApplyRenderMode()
    {
        bool software = _windows.Count > 0
            ? _windows[0].SoftwareRender
            : WidgetConfig.Load("main").SoftwareRender;
        RenderOptions.ProcessRenderMode = software ? RenderMode.SoftwareOnly : RenderMode.Default;
    }

    // ---------- global hotkey: HUD mode escape hatch ----------

    void RegisterHotkey()
    {
        var p = new HwndSourceParameters("HWWidgetHotkey") { Width = 0, Height = 0, WindowStyle = 0 };
        _hotkeySink = new HwndSource(p);
        _hotkeySink.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_HOTKEY)
            {
                ToggleHudAll();
                handled = true;
            }
            return IntPtr.Zero;
        });
        // Ctrl+Alt+H, with a fallback if another app already grabbed it
        HotkeyOk = Native.RegisterHotKey(_hotkeySink.Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT, 0x48);
        if (!HotkeyOk)
            HotkeyOk = Native.RegisterHotKey(_hotkeySink.Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x48);
    }

    public bool HotkeyOk { get; private set; }

    public void Dispose()
    {
        if (_hotkeySink != null)
        {
            Native.UnregisterHotKey(_hotkeySink.Handle, HotkeyId);
            _hotkeySink.Dispose();
            _hotkeySink = null;
        }
        _tray?.Dispose();
        _tray = null;
    }
}

/// <summary>System tray icon ("altre icone" in Windows 11). Left click hides/shows the
/// widgets, right click opens the same dark menu style used by the widgets.</summary>
sealed class TrayIcon : IDisposable
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    readonly AppController _app;
    readonly WF.NotifyIcon _ni;
    readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    IntPtr _hicon;

    public TrayIcon(AppController app)
    {
        _app = app;
        _ni = new WF.NotifyIcon
        {
            Icon = AppIcon.Drawing() ?? BuildIcon(),
            Text = "HW Widget",
            Visible = true,
        };
        _ni.MouseUp += (_, e) =>
        {
            if (e.Button == WF.MouseButtons.Right) ShowMenu();
        };
        _ni.MouseClick += (_, e) =>
        {
            if (e.Button != WF.MouseButtons.Left) return;
            // single click shows/hides, double click opens the hub: wait to tell them apart
            _clickTimer.Stop();
            _clickTimer.Start();
        };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            _app.ToggleAllVisible();
        };
        _ni.DoubleClick += (_, _) =>
        {
            _clickTimer.Stop();
            _app.ShowHub();
        };
    }

    SD.Icon BuildIcon()
    {
        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);
            using var bg = new SD.SolidBrush(SD.Color.FromArgb(235, 30, 30, 34));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var a = new SD.SolidBrush(SD.Color.FromArgb(255, 76, 194, 255));
            using var bb = new SD.SolidBrush(SD.Color.FromArgb(255, 255, 184, 77));
            using var c = new SD.SolidBrush(SD.Color.FromArgb(255, 79, 209, 197));
            g.FillRectangle(a, 8, 17, 5, 8);
            g.FillRectangle(bb, 14, 11, 5, 14);
            g.FillRectangle(c, 20, 14, 5, 11);
        }
        _hicon = bmp.GetHicon();
        return SD.Icon.FromHandle(_hicon);
    }

    void ShowMenu()
    {
        var menu = new ContextMenu
        {
            Resources = MenuStyles.Create(Palette.For(WidgetConfig.Load("main").Theme)),
            FontSize = 12.5,
            Placement = PlacementMode.MousePoint,
        };

        MenuItem Leaf(string h, Action act)
        {
            var mi = new MenuItem { Header = h };
            mi.Click += (_, _) => act();
            return mi;
        }

        var nuovo = new MenuItem { Header = "Nuovo widget" };
        nuovo.Items.Add(Leaf("Tutto", () => _app.NewWidget(null)));
        nuovo.Items.Add(Leaf("Solo CPU", () => _app.NewWidget("cpu")));
        nuovo.Items.Add(Leaf("Solo GPU", () => _app.NewWidget("gpu")));
        nuovo.Items.Add(Leaf("Solo RAM", () => _app.NewWidget("ram")));
        nuovo.Items.Add(Leaf("Solo rete", () => _app.NewWidget("net")));
        nuovo.Items.Add(Leaf("CPU + GPU", () => _app.NewWidget("cpugpu")));

        var startup = new MenuItem { Header = "Avvia con Windows", IsCheckable = true, IsChecked = _app.StartupEnabled };
        startup.Click += (_, _) => _app.SetStartup(startup.IsChecked);

        menu.Items.Add(Leaf("Mostra / nascondi widget", () => _app.ToggleAllVisible()));
        var hud = new MenuItem { Header = "Clic attraverso (HUD)", IsCheckable = true, IsChecked = _app.AnyHud };
        hud.Click += (_, _) => _app.SetHudAll(hud.IsChecked);
        menu.Items.Add(hud);
        menu.Items.Add(Leaf("Hub di controllo…", () => _app.ShowHub()));
        menu.Items.Add(nuovo);
        menu.Items.Add(startup);
        menu.Items.Add(new Separator());
        menu.Items.Add(Leaf("Esci da HW Widget", () => _app.ExitAll()));
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        _ni.Visible = false;
        _ni.Dispose();
        if (_hicon != IntPtr.Zero) DestroyIcon(_hicon);
    }

    /// <summary>Explains how to leave click-through mode: in that state the widget itself
    /// can no longer be clicked, so the tray icon (and the hotkey) are the way back.</summary>
    public void WarnHud()
    {
        try
        {
            _ni.BalloonTipTitle = "Modalità HUD attiva";
            _ni.BalloonTipText = _app.HotkeyOk
                ? "Il widget non riceve più i clic. Premi Ctrl+Alt+H oppure fai clic destro sull'icona nel tray per disattivarla."
                : "Il widget non riceve più i clic. Fai clic destro sull'icona HW Widget nel tray per disattivarla.";
            _ni.ShowBalloonTip(6000);
        }
        catch { }
    }
}
