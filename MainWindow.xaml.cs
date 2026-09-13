using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using WF = System.Windows.Forms;

namespace HWWidget;

public partial class MainWindow : Window
{
    const int WM_NCHITTEST = 0x0084;
    const int WM_NCCALCSIZE = 0x0083;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
              HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    readonly WidgetConfig _c;
    Palette _p;
    readonly DispatcherTimer _saveDebounce = new();
    readonly DispatcherTimer _resizeDebounce = new();
    readonly Dictionary<string, Series> _series = new();
    ResourceDictionary _menuStyles;
    WidgetView _view = null!;
    IntPtr _hwnd;
    HwndSource? _src;
    DateTime _lastUi = DateTime.MinValue;

    public event Action<MainWindow>? CloseRequested;

    internal MainWindow(WidgetConfig c)
    {
        _c = c;
        _p = Palette.For(c.Theme);
        _menuStyles = MenuStyles.Create(_p);
        InitializeComponent();

        var appIcon = AppIcon.Wpf();
        if (appIcon != null) Icon = appIcon;

        Background = Brushes.Transparent;
        Foreground = new SolidColorBrush(_p.Text);
        Width = _c.Width;
        Height = _c.Height;
        Topmost = _c.Topmost;

        Rebuild();
        ApplySurface();

        _saveDebounce.Interval = TimeSpan.FromMilliseconds(700);
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SavePosition(); };

        // panel layouts are sized from the widget width, so re-lay them out after a resize
        // (the graph history lives outside the visual tree and survives the rebuild)
        _resizeDebounce.Interval = TimeSpan.FromMilliseconds(180);
        _resizeDebounce.Tick += (_, _) =>
        {
            _resizeDebounce.Stop();
            if (_c.Layout is "panel" or "panelgraph") Rebuild();
        };

        LocationChanged += (_, _) => DebouncedSave();
        SizeChanged += (_, _) => { DebouncedSave(); _resizeDebounce.Stop(); _resizeDebounce.Start(); };
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RestorePosition));
        Deactivated += (_, _) => ApplyBackdrop();

        SensorHub.Tick += OnTick;
        SensorHub.SetInterval(_c.Id, _c.IntervalSeconds);
        _view.Bind(SensorHub.Current);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _src = HwndSource.FromHwnd(_hwnd);
        _src?.AddHook(WndProc);
        // WPF otherwise paints its composition target opaque and the DWM backdrop never shows
        if (_src?.CompositionTarget != null) _src.CompositionTarget.BackgroundColor = Colors.Transparent;

        // no WS_THICKFRAME on purpose: with it Windows draws a thin accent-coloured frame
        // line around the widget. Resizing is implemented by hand below.
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);

        ApplyBackdrop();
        if (_c.ClickThrough) SetClickThrough(true);
    }

    // ---------- content ----------

    void Rebuild()
    {
        _view = new WidgetView(_c, _p, _series, ActualWidth > 10 ? ActualWidth : _c.Width);
        Host.Content = _view.Root;
        _view.Bind(SensorHub.Current);
        _view.Root.Measure(new Size(Math.Max(60, (Host.ActualWidth > 10 ? Host.ActualWidth : _c.Width - 26)), double.PositiveInfinity));
    }

    void OnTick(Metrics m)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastUi).TotalSeconds < _c.IntervalSeconds - 0.02) return;
        _lastUi = now;
        _view.Bind(m);
    }

    // ---------- look ----------

    /// <summary>DeskBox-style mapping of the opacity slider to the legacy acrylic alpha.</summary>
    double LegacyAcrylicAlpha()
        => Math.Clamp(0.01 + 0.89 * Math.Clamp(_c.PanelOpacity, 0, 1), 0, 1);

    void ApplySurface()
    {
        double alpha = _c.Backdrop switch
        {
            "none" => Math.Max(_c.PanelOpacity, 0.85),
            // with the accent blur the tint already comes from DWM, so the XAML layer
            // only reinforces it (same trick DeskBox uses, so a silent blur failure
            // still leaves a readable panel)
            "blur" => Math.Clamp(LegacyAcrylicAlpha() * 0.45, 0, 0.9),
            _ => _c.PanelOpacity,
        };
        byte a = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        var panel = _p.Panel;
        Root.Background = new SolidColorBrush(Color.FromArgb(a, panel.R, panel.G, panel.B));
        Root.BorderBrush = new SolidColorBrush(_p.PanelBorder);
    }

    void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) { ApplySurface(); return; }
        Backdrop.Apply(_hwnd, _c.Backdrop, _p);
        if (_c.Backdrop == "blur")
        {
            // the tint follows the opacity slider
            double legacy = LegacyAcrylicAlpha();
            byte tintAlpha = (byte)Math.Round(Math.Clamp(legacy * 0.55, 0, 1) * 255);
            var panel = _p.Panel;
            Native.ApplyAccent(_hwnd,
                legacy <= 0.02 ? Native.ACCENT_ENABLE_BLURBEHIND : Native.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                Native.Abgr(tintAlpha, panel.R, panel.G, panel.B));
        }
        ApplySurface();
    }

    // ---------- position (physical pixels, DPI-proof) ----------

    void RestorePosition()
    {
        if (_hwnd == IntPtr.Zero) return;
        var screens = WF.Screen.AllScreens;

        if (!_c.HasPos)
        {
            var wa = WF.Screen.PrimaryScreen!.WorkingArea;
            UpdateLayout();
            Native.GetWindowRect(_hwnd, out var r);
            Native.SetWindowPos(_hwnd, IntPtr.Zero, wa.Right - (r.R - r.L) - 24, wa.Top + 24, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            SavePosition();
            return;
        }

        var scr = Array.Find(screens, x => x.DeviceName == _c.Monitor)
                  ?? Array.Find(screens, x => x.Primary) ?? screens[0];
        var wa2 = scr.WorkingArea;
        Width = Math.Max(_c.Width, MinWidth);
        Height = Math.Max(_c.Height, MinHeight);
        UpdateLayout();

        double dpi = Math.Max(1, Native.GetDpiForWindow(_hwnd) / 96.0);
        int pw = (int)Math.Ceiling(ActualWidth * dpi), ph = (int)Math.Ceiling(ActualHeight * dpi);
        int x = (int)Math.Clamp(wa2.X + _c.OffX, wa2.X, Math.Max(wa2.X, wa2.X + wa2.Width - pw));
        int y = (int)Math.Clamp(wa2.Y + _c.OffY, wa2.Y, Math.Max(wa2.Y, wa2.Y + wa2.Height - ph));
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    void SavePosition()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!Native.GetWindowRect(_hwnd, out var r)) return;
        var scr = WF.Screen.FromHandle(_hwnd);
        _c.Monitor = scr.DeviceName;
        _c.OffX = r.L - scr.WorkingArea.X;
        _c.OffY = r.T - scr.WorkingArea.Y;
        _c.Width = ActualWidth;
        _c.Height = ActualHeight;
        _c.HasPos = true;
        _c.Save();
    }

    void DebouncedSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    void MoveToMonitor(string device)
    {
        var scr = Array.Find(WF.Screen.AllScreens, s => s.DeviceName == device);
        if (scr == null || _hwnd == IntPtr.Zero) return;
        Native.GetWindowRect(_hwnd, out var r);
        var wa = scr.WorkingArea;
        int w = Math.Min(r.R - r.L, wa.Width), h = Math.Min(r.B - r.T, wa.Height);
        int x = Math.Clamp(r.L, wa.X, Math.Max(wa.X, wa.X + wa.Width - w));
        int y = Math.Clamp(r.T, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - h));
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        SavePosition();
    }

    public void MoveToMonitorPublic(string device) => MoveToMonitor(device);

    double FitHeight()
    {
        // measure the real content instead of estimating it: exact for every layout
        UpdateLayout();
        double inner = Math.Max(60, (Host.ActualWidth > 10 ? Host.ActualWidth : _c.Width - 26));
        _view.Root.Measure(new Size(inner, double.PositiveInfinity));
        double h = _view.Root.DesiredSize.Height + Root.Padding.Top + Root.Padding.Bottom + 6;
        // never ask for more than the monitor's working area
        var scr = _hwnd != IntPtr.Zero ? WF.Screen.FromHandle(_hwnd) : WF.Screen.PrimaryScreen!;
        return Math.Min(h, scr.WorkingArea.Height - 40);
    }

    // ---------- window chrome ----------

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        // keep the whole window as client area: with WS_THICKFRAME Windows would otherwise
        // draw a thin translucent frame strip along the top edge
        if (msg == WM_NCCALCSIZE && w != IntPtr.Zero)
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == WM_NCHITTEST && _c.ClickThrough)
        {
            handled = true;
            return (IntPtr)(-1);   // HTTRANSPARENT
        }
        return IntPtr.Zero;
    }

    // ---------- manual resizing (no window frame) ----------

    const int EdgeLeft = 1, EdgeTop = 2, EdgeRight = 4, EdgeBottom = 8;
    int _resizeEdge;
    Native.RECT _resizeStart;
    Native.POINT _resizeCursor;

    int EdgeAt(Point p)
    {
        const double m = 7;
        int edge = 0;
        if (p.X <= m) edge |= EdgeLeft;
        if (p.Y <= m) edge |= EdgeTop;
        if (p.X >= ActualWidth - m) edge |= EdgeRight;
        if (p.Y >= ActualHeight - m) edge |= EdgeBottom;
        return edge;
    }

    void StartResize(int edge)
    {
        _resizeEdge = edge;
        Native.GetWindowRect(_hwnd, out _resizeStart);
        Native.GetCursorPos(out _resizeCursor);
        CaptureMouse();
        MouseMove += ResizeMove;
        MouseLeftButtonUp += ResizeEnd;
    }

    void ResizeMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdge == 0) return;
        Native.GetCursorPos(out var cur);
        int dx = cur.X - _resizeCursor.X, dy = cur.Y - _resizeCursor.Y;
        var r = _resizeStart;
        int l = r.L, t = r.T, rr = r.R, bb = r.B;
        if ((_resizeEdge & EdgeLeft) != 0) l += dx;
        if ((_resizeEdge & EdgeTop) != 0) t += dy;
        if ((_resizeEdge & EdgeRight) != 0) rr += dx;
        if ((_resizeEdge & EdgeBottom) != 0) bb += dy;

        double dpi = Math.Max(1, Native.GetDpiForWindow(_hwnd) / 96.0);
        int minW = (int)(140 * dpi), minH = (int)(70 * dpi);
        if (rr - l < minW) { if ((_resizeEdge & EdgeLeft) != 0) l = rr - minW; else rr = l + minW; }
        if (bb - t < minH) { if ((_resizeEdge & EdgeTop) != 0) t = bb - minH; else bb = t + minH; }

        // resize through WPF (it owns Width/Height and re-applies them), move with SetWindowPos
        Width = Math.Max(MinWidth, (rr - l) / dpi);
        Height = Math.Max(MinHeight, (bb - t) / dpi);
        if ((_resizeEdge & (EdgeLeft | EdgeTop)) != 0)
            Native.SetWindowPos(_hwnd, IntPtr.Zero, l, t, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    void ResizeEnd(object sender, MouseButtonEventArgs e)
    {
        MouseMove -= ResizeMove;
        MouseLeftButtonUp -= ResizeEnd;
        ReleaseMouseCapture();
        _resizeEdge = 0;
        SavePosition();
    }


    void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_c.Locked || e.LeftButton != MouseButtonState.Pressed) return;
        int edge = EdgeAt(e.GetPosition(this));
        if (edge != 0)
        {
            StartResize(edge);
            e.Handled = true;
            return;
        }
        try { DragMove(); } catch { }
        SavePosition();
    }

    public void SetClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        ex = on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
        _c.ClickThrough = on;
        _c.Save();
    }

    public void ToggleClickThrough()
    {
        SetClickThrough(!_c.ClickThrough);
        ApplyBackdrop();
    }

    public void SetVisible(bool on) => Dispatcher.BeginInvoke(() => { if (on) Show(); else Hide(); });

    // ---------- menu ----------

    static MenuItem Leaf(string header, Action act)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => act();
        return mi;
    }

    static MenuItem Toggle(string header, bool value, Action<bool> set)
    {
        var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = value };
        mi.Click += (_, _) => set(mi.IsChecked);
        return mi;
    }

    static MenuItem Choose(string header, bool selected, Action act)
    {
        var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = selected };
        mi.Click += (_, _) => act();
        return mi;
    }

    static MenuItem Sub(string header, params object[] items)
    {
        var mi = new MenuItem { Header = header };
        foreach (var i in items) mi.Items.Add(i);
        return mi;
    }

    MenuItem SubWithHeader(string header, params object[] items)
    {
        var mi = Sub(header, items);
        mi.IsEnabled = true;
        return mi;
    }

    void Changed(bool needRebuild = false)
    {
        _c.Sanitized().Save();
        if (needRebuild) Rebuild();
        ApplySurface();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_c.ClickThrough) return;
        var menu = BuildMenu();
        menu.Resources = _menuStyles;
        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    ContextMenu BuildMenu()
    {
        var contenuto = SubWithHeader("Contenuto",
            Toggle("Rete (upload/download)", _c.ShowNet, v => { _c.ShowNet = v; Changed(true); }),
            Toggle("CPU", _c.ShowCpu, v => { _c.ShowCpu = v; Changed(true); }),
            Toggle("GPU", _c.ShowGpu, v => { _c.ShowGpu = v; Changed(true); }),
            Toggle("RAM", _c.ShowRam, v => { _c.ShowRam = v; Changed(true); }),
            Toggle("Disco (lettura/scrittura)", _c.ShowDisk, v => { _c.ShowDisk = v; Changed(true); }),
            new Separator(),
            Leaf("Preset: Tutto", () => Preset(true, true, true, true, true)),
            Leaf("Preset: solo CPU", () => Preset(false, true, false, false, false)),
            Leaf("Preset: solo GPU", () => Preset(false, false, true, false, false)),
            Leaf("Preset: solo RAM", () => Preset(false, false, false, true, false)),
            Leaf("Preset: solo rete", () => Preset(true, false, false, false, false)),
            Leaf("Preset: CPU + GPU", () => Preset(false, true, true, false, false)),
            Leaf("Preset: CPU + RAM", () => Preset(false, true, false, true, false)),
            Leaf("Preset: GPU + RAM", () => Preset(false, false, true, true, false)));

        var layout = SubWithHeader("Layout",
            Choose("Righe compatte", _c.Layout == "rows", () => SetLayout("rows")),
            Choose("Card per elemento", _c.Layout == "cards", () => SetLayout("cards")),
            Choose("Tessere (icona + barra)", _c.Layout == "tiles", () => SetLayout("tiles")),
            Choose("Pannello a sezioni (barre)", _c.Layout == "panel", () => SetLayout("panel")),
            Choose("Pannello con grafici", _c.Layout == "panelgraph", () => SetLayout("panelgraph")));

        var grafici = SubWithHeader("Grafici",
            SubWithHeader("Stile",
                Choose("Area", _c.GraphStyleName == "area", () => SetGraph(g => g.GraphStyleName = "area")),
                Choose("Linea", _c.GraphStyleName == "line", () => SetGraph(g => g.GraphStyleName = "line")),
                Choose("Barre", _c.GraphStyleName == "bars", () => SetGraph(g => g.GraphStyleName = "bars")),
                Choose("Scalini", _c.GraphStyleName == "step", () => SetGraph(g => g.GraphStyleName = "step"))),
            SubWithHeader("Larghezza",
                Leaf("Più stretti  −", () => SetGraph(g => g.GraphWidthScale = Math.Clamp(g.GraphWidthScale - 0.2, 0.3, 4))),
                Leaf("Più larghi  +", () => SetGraph(g => g.GraphWidthScale = Math.Clamp(g.GraphWidthScale + 0.2, 0.3, 4))),
                Leaf("Normale", () => SetGraph(g => g.GraphWidthScale = 1))),
            SubWithHeader("Altezza",
                Leaf("Più bassi  −", () => SetGraph(g => g.GraphHeightScale = Math.Clamp(g.GraphHeightScale - 0.15, 0.4, 3))),
                Leaf("Più alti  +", () => SetGraph(g => g.GraphHeightScale = Math.Clamp(g.GraphHeightScale + 0.15, 0.4, 3))),
                Leaf("Normale", () => SetGraph(g => g.GraphHeightScale = 1))),
            SubWithHeader("Durata",
                Choose("30 secondi", _c.GraphSeconds == 30, () => SetGraph(g => g.GraphSeconds = 30)),
                Choose("1 minuto", _c.GraphSeconds == 60, () => SetGraph(g => g.GraphSeconds = 60)),
                Choose("5 minuti", _c.GraphSeconds == 300, () => SetGraph(g => g.GraphSeconds = 300))));

        var aspetto = SubWithHeader("Aspetto",
            SubWithHeader("Tema",
                Choose("Come Windows", _c.Theme == "system", () => SetTheme("system")),
                Choose("Chiaro", _c.Theme == "light", () => SetTheme("light")),
                Choose("Scuro", _c.Theme == "dark", () => SetTheme("dark"))),
            SubWithHeader("Sfondo",
                Choose("Acrylic sfocato (sempre)", _c.Backdrop == "blur", () => SetBackdrop("blur")),
                Choose("Mica", _c.Backdrop == "mica", () => SetBackdrop("mica")),
                Choose("Mica Alt", _c.Backdrop == "micaalt", () => SetBackdrop("micaalt")),
                Choose("Acrylic (finestra attiva)", _c.Backdrop == "acrylic", () => SetBackdrop("acrylic")),
                Choose("Nessuno (pannello pieno)", _c.Backdrop == "none", () => SetBackdrop("none"))),
            SubWithHeader("Opacità pannello",
                Choose("Trasparente 0%", _c.PanelOpacity < 0.05, () => SetOpacity(0)),
                Choose("25%", Math.Abs(_c.PanelOpacity - 0.25) < 0.01, () => SetOpacity(0.25)),
                Choose("45%", Math.Abs(_c.PanelOpacity - 0.45) < 0.01, () => SetOpacity(0.45)),
                Choose("70%", Math.Abs(_c.PanelOpacity - 0.7) < 0.01, () => SetOpacity(0.7)),
                Choose("100%", Math.Abs(_c.PanelOpacity - 1) < 0.01, () => SetOpacity(1))),
            SubWithHeader("Testo",
                Leaf("Meno  −", () => SetText(-0.1)),
                Leaf("Più  +", () => SetText(0.1)),
                Leaf("Normale", () => SetText(0, true))),
            SubWithHeader("Righe",
                Leaf("Meno  −", () => SetRows(-0.15)),
                Leaf("Più  +", () => SetRows(0.15)),
                Leaf("Normale", () => SetRows(0, true))),
            SubWithHeader("Scala interfaccia (tutto)",
                Leaf("Riduci  −10%", () => SetUiScale(_c.UiScale - 0.1)),
                Leaf("Aumenta  +10%", () => SetUiScale(_c.UiScale + 0.1)),
                Leaf("75%", () => SetUiScale(0.75)),
                Leaf("100%", () => SetUiScale(1)),
                Leaf("125%", () => SetUiScale(1.25)),
                Leaf("150%", () => SetUiScale(1.5))),
            SubWithHeader("Dimensione widget",
                Leaf("Piccolo", () => SetSize(240)),
                Leaf("Medio", () => SetSize(360)),
                Leaf("Grande", () => SetSize(480)),
                Leaf("Adatta altezza", () => { Height = FitHeight(); SavePosition(); })));

        var colori = SubWithHeader("Colori dei misuratori",
            ColorPicker("cpu", "CPU"),
            ColorPicker("gpu", "GPU"),
            ColorPicker("ram", "RAM"),
            ColorPicker("disk", "Disco"),
            ColorPicker("net", "Rete"));

        var monitor = Sub("Sposta sul monitor");
        var screens = WF.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var mi = Choose($"Monitor {i + 1} · {screens[i].Bounds.Width}×{screens[i].Bounds.Height}" +
                            (screens[i].Primary ? "  (principale)" : ""),
                            _hwnd != IntPtr.Zero && WF.Screen.FromHandle(_hwnd).DeviceName == screens[i].DeviceName,
                            () => { });
            string dev = screens[i].DeviceName;
            mi.Click += (_, _) => MoveToMonitor(dev);
            monitor.Items.Add(mi);
        }

        var posizione = SubWithHeader("Posizione",
            monitor,
            Toggle("Sempre in primo piano", _c.Topmost, v => { _c.Topmost = v; Topmost = v; Changed(); }),
            Toggle("Blocca posizione e dimensioni", _c.Locked, v => { _c.Locked = v; Changed(); }),
            Toggle("HUD: clic attraverso (Ctrl+Alt+H)", _c.ClickThrough, _ => ToggleClickThrough()),
            Leaf("Reimposta posizione", () => { _c.HasPos = false; RestorePosition(); }));

        var aggiornamento = SubWithHeader("Aggiornamento",
            Choose("0,5 secondi", Math.Abs(_c.IntervalSeconds - 0.5) < 0.01, () => SetInterval(0.5)),
            Choose("1 secondo", Math.Abs(_c.IntervalSeconds - 1) < 0.01, () => SetInterval(1)),
            Choose("2 secondi", Math.Abs(_c.IntervalSeconds - 2) < 0.01, () => SetInterval(2)));

        var sistema = SubWithHeader("Sistema",
            Toggle("Avvia con Windows", AppController.Current.StartupEnabled, v => AppController.Current.SetStartup(v)),
            SubWithHeader("Nuovo widget",
                Leaf("Tutto", () => AppController.Current.NewWidget(null)),
                Leaf("Solo CPU", () => AppController.Current.NewWidget("cpu")),
                Leaf("Solo GPU", () => AppController.Current.NewWidget("gpu")),
                Leaf("Solo RAM", () => AppController.Current.NewWidget("ram")),
                Leaf("Solo rete", () => AppController.Current.NewWidget("net")),
                Leaf("CPU + GPU", () => AppController.Current.NewWidget("cpugpu"))),
            Leaf("Hub di controllo…", () => AppController.Current.ShowHub()),
            Toggle("Rendering CPU (se compare l'overlay NVIDIA)", _c.SoftwareRender,
                   v => { _c.SoftwareRender = v; Changed(); AppController.Current.ApplyRenderMode(); }),
            new Separator(),
            Leaf("Chiudi questo widget", () => CloseRequested?.Invoke(this)),
            Leaf("Esci da HW Widget", () => AppController.Current.ExitAll()));

        return new ContextMenu
        {
            Items = { contenuto, layout, grafici, aspetto, colori, posizione, aggiornamento, sistema },
            FontSize = 12.5,
        };
    }

    static readonly (string Name, string Hex)[] Swatches =
    {
        ("Blu", "#4CC2FF"),
        ("Verde", "#5CD66E"),
        ("Ambra", "#FFB84D"),
        ("Rosso", "#FF5B5B"),
        ("Viola", "#A78BFA"),
        ("Turchese", "#4FD1C5"),
        ("Rosa", "#FF7AC6"),
        ("Grigio", "#B0B6BF"),
    };

    MenuItem ColorPicker(string element, string label)
    {
        var root = new MenuItem { Header = label };
        root.Items.Add(Swatch(element, "Automatico (per soglia)", "auto"));
        foreach (var (name, hex) in Swatches) root.Items.Add(Swatch(element, name, hex));
        return root;
    }

    MenuItem Swatch(string element, string name, string hex)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = hex == "auto" ? new SolidColorBrush(_p.TextDim) : WidgetView.BrushFromHex(hex),
        });
        header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = _c.ColorOf(element) == hex };
        mi.Click += (_, _) =>
        {
            _c.Colors[element] = hex;
            Changed(true);
        };
        return mi;
    }

    void SetUiScale(double value)
    {
        _c.UiScale = Math.Clamp(value, 0.5, 2.5);
        Changed(true);
        Height = FitHeight();
        SavePosition();
    }

    void Preset(bool net, bool cpu, bool gpu, bool ram, bool disk)
    {
        _c.ShowNet = net; _c.ShowCpu = cpu; _c.ShowGpu = gpu; _c.ShowRam = ram; _c.ShowDisk = disk;
        Changed(true);
        Height = FitHeight();
        SavePosition();
    }

    void SetLayout(string layout)
    {
        _c.Layout = layout;
        Changed(true);
        Height = FitHeight();
        SavePosition();
    }

    void SetGraph(Action<WidgetConfig> mutate)
    {
        mutate(_c);
        Changed(true);
    }

    void SetTheme(string theme)
    {
        _c.Theme = theme;
        _p = Palette.For(theme);
        _menuStyles = MenuStyles.Create(_p);
        Foreground = new SolidColorBrush(_p.Text);
        Rebuild();
        ApplyBackdrop();
        _c.Save();
    }

    void SetBackdrop(string backdrop)
    {
        _c.Backdrop = backdrop;
        ApplyBackdrop();
        _c.Save();
    }

    void SetOpacity(double v)
    {
        _c.PanelOpacity = v;
        ApplySurface();
        _c.Save();
    }

    void SetText(double delta, bool reset = false)
    {
        _c.TextScale = reset ? 1 : Math.Clamp(_c.TextScale + delta, 0.6, 2.2);
        Changed(true);
        Height = FitHeight();
        SavePosition();
    }

    void SetRows(double delta, bool reset = false)
    {
        _c.RowScale = reset ? 1 : Math.Clamp(_c.RowScale + delta, 0.6, 3);
        Changed(true);
        Height = FitHeight();
        SavePosition();
    }

    void SetSize(double width)
    {
        Width = width;
        Height = FitHeight();
        SavePosition();
    }

    void SetInterval(double seconds)
    {
        _c.IntervalSeconds = seconds;
        Changed(true);
        SensorHub.SetInterval(_c.Id, seconds);
    }

    public string InstanceId => _c.Id;
    public double IntervalSeconds => _c.IntervalSeconds;
    public bool SoftwareRender => _c.SoftwareRender;

    internal WidgetConfig Config => _c;

    /// <summary>Re-applies everything from the config (used by the control hub for live edits).</summary>
    public void ApplyConfig(bool resize = false)
    {
        _p = Palette.For(_c.Theme);
        _menuStyles = MenuStyles.Create(_p);
        Foreground = new SolidColorBrush(_p.Text);
        Topmost = _c.Topmost;
        Rebuild();
        ApplyBackdrop();
        SensorHub.SetInterval(_c.Id, _c.IntervalSeconds);
        if (resize) Height = FitHeight();
        _c.Save();
    }

    public void FitHeightNow()
    {
        Height = FitHeight();
        SavePosition();
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePosition();
        SensorHub.Tick -= OnTick;
        SensorHub.Forget(_c.Id);
        _src?.RemoveHook(WndProc);
        base.OnClosed(e);
    }
}
