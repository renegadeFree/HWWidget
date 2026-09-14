using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace HWWidget;

public enum GraphStyle { Line, Area, Bars, Step }

/// <summary>Per-widget configuration. One file per widget instance:
/// settings.json for the first one, settings-&lt;id&gt;.json for the others.</summary>
internal sealed class WidgetConfig
{
    /// <summary>0 = file written by an older build (before layouts/theme).</summary>
    public int Schema { get; set; }

    /// <summary>Legacy field: text scale used to live in "Scale".</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    public string Id { get; set; } = "main";

    /// <summary>Nome del widget scelto alla creazione (GPU, CPU+GPU, TUTTO…). Vuoto = dedotto
    /// dagli elementi visibili, così anche i widget vecchi hanno un nome leggibile.</summary>
    public string Name { get; set; } = "";

    // position / size (physical pixels for the position, DIPs for the size)
    public bool HasPos { get; set; }
    public string? Monitor { get; set; }
    public double OffX { get; set; }
    public double OffY { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 170;

    // composition
    public bool ShowNet { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    /// <summary>Sezione AI (DeepSeek / ChatGPT / Claude).</summary>
    public bool ShowAi { get; set; }
    /// <summary>Sezione DeepSeek completa (saldo, uso del mese, modelli, grafico giornaliero).</summary>
    public bool ShowDs { get; set; }
    public List<string> AiProviders { get; set; } = new() { "deepseek", "openai", "anthropic" };
    /// <summary>Budget mensile per fornitore AI in USD (0 = non impostato). Serve al widget
    /// per calcolare il budget rimasto: le API non espongono un limite di spesa.</summary>
    public Dictionary<string, double> AiBudgets { get; set; } = new();
    public bool ShowSecondary { get; set; } = true;
    /// <summary>"⚡ HW Widget" heading inside the panel layout.</summary>
    public bool ShowTitle { get; set; }

    /// <summary>Order of the elements inside the widget (first = top). Empty = default
    /// order of the current layout.</summary>
    public List<string> Order { get; set; } = new();

    // look
    public string Layout { get; set; } = "rows";      // rows | cards | tiles
    public string GraphStyleName { get; set; } = "area";
    public string Theme { get; set; } = "system";     // system | dark | light
    /// <summary>blur = accent blur behind (always blurred, the DeskBox-like material) |
    /// mica | micaalt | acrylic = DWM backdrops (flat while the widget is unfocused) | none</summary>
    public string Backdrop { get; set; } = "blur";
    public double PanelOpacity { get; set; } = 0.45;
    public double TextScale { get; set; } = 1;
    public double RowScale { get; set; } = 1;
    /// <summary>Global scale applied to every element (menu: Scala interfaccia).</summary>
    public double UiScale { get; set; } = 1;

    /// <summary>Fixed colour per element ("cpu", "gpu", "ram", "disk", "net") as #RRGGBB.
    /// Missing or "auto" keeps the threshold colours (green / amber / red).</summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    // graphs
    public double GraphWidthScale { get; set; } = 1;
    public double GraphHeightScale { get; set; } = 1;
    public int GraphSeconds { get; set; } = 60;
    public double IntervalSeconds { get; set; } = 1;

    // behaviour
    public bool Topmost { get; set; } = true;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }

    /// <summary>Widget closed by the user: keep the file (position, size, layout) but
    /// don't reopen it at the next start.</summary>
    public bool Closed { get; set; }
    /// <summary>Software rendering is the default: with WPF's D3D path the NVIDIA
    /// overlay attaches an FPS counter to the widget (verified on this machine).</summary>
    public bool SoftwareRender { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public GraphStyle GraphStyle => GraphStyleName switch
    {
        "line" => GraphStyle.Line,
        "bars" => GraphStyle.Bars,
        "step" => GraphStyle.Step,
        _ => GraphStyle.Area,
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Elements
    {
        get
        {
            var visible = new List<string>();
            if (ShowNet) visible.Add("net");
            if (ShowCpu) visible.Add("cpu");
            if (ShowGpu) visible.Add("gpu");
            if (ShowRam) visible.Add("ram");
            if (ShowDisk) visible.Add("disk");
            if (ShowAi) visible.Add("ai");
            if (ShowDs) visible.Add("ds");

            if (Order.Count == 0) return visible;
            var ordered = Order.Where(visible.Contains).ToList();
            foreach (var v in visible) if (!ordered.Contains(v)) ordered.Add(v);
            return ordered;
        }
    }

    /// <summary>The order actually used, materialised (used by the reorder UI).</summary>
    public List<string> EffectiveOrder() => Elements.ToList();

    /// <summary>Nome da mostrare nell'hub: quello scelto, altrimenti i suoi elementi.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (Name.Trim().Length > 0) return Name.Trim();
            var visible = Elements.ToList();
            if (visible.Count == 0) return "VUOTO";
            if (ShowNet && ShowCpu && ShowGpu && ShowRam && ShowDisk) return "TUTTO";
            var parts = new List<string>();
            foreach (var (key, label) in new[]
                     {
                         ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("disk", "DISCO"),
                         ("net", "RETE"), ("ai", "AI"), ("ds", "DEEPSEEK"),
                     })
                if (visible.Contains(key)) parts.Add(label);
            return string.Join("+", parts);
        }
    }

    public void MoveElement(string key, int delta)
    {
        var order = EffectiveOrder();
        int i = order.IndexOf(key);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= order.Count) return;
        (order[i], order[j]) = (order[j], order[i]);
        Order = order;
    }

    internal static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWWidget");

    static string FileOf(string id) => Path.Combine(Dir, id == "main" ? "settings.json" : $"settings-{id}.json");

    public static WidgetConfig Load(string id)
    {
        try
        {
            string f = FileOf(id);
            if (System.IO.File.Exists(f))
            {
                var c = JsonSerializer.Deserialize<WidgetConfig>(System.IO.File.ReadAllText(f)) ?? new WidgetConfig();
                c.Id = id;
                return c.Migrated().Sanitized();
            }
        }
        catch { }
        return new WidgetConfig { Id = id }.Sanitized();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(FileOf(Id), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Delete()
    {
        try { System.IO.File.Delete(FileOf(Id)); } catch { }
    }

    /// <summary>Existing widgets = the settings files on disk. No extra registry file needed.</summary>
    public static List<string> Instances()
    {
        var ids = new List<string>();
        try
        {
            if (!Directory.Exists(Dir)) return ids;
            foreach (var f in Directory.GetFiles(Dir, "settings*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string id = name == "settings" ? "main"
                    : name.StartsWith("settings-", StringComparison.Ordinal) ? name["settings-".Length..] : "";
                if (id.Length > 0 && !ids.Contains(id))
                {
                    try
                    {
                        var cfg = JsonSerializer.Deserialize<WidgetConfig>(System.IO.File.ReadAllText(f));
                        if (cfg?.Closed == true) continue;
                    }
                    catch { }
                    ids.Add(id);
                }
            }
            ids.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return ids;
    }

    public static string NewId()
    {
        var used = Instances();
        for (int i = 2; i < 100; i++)
        {
            string id = "w" + i;
            if (!used.Contains(id)) return id;
        }
        return "w" + Guid.NewGuid().ToString("N")[..4];
    }

    /// <summary>A hand-edited or culture-broken file must not produce an unusable widget.</summary>
    public WidgetConfig Sanitized()
    {
        PanelOpacity = Math.Clamp(PanelOpacity, 0.0, 1);
        TextScale = Math.Clamp(TextScale, 0.6, 2.2);
        RowScale = Math.Clamp(RowScale, 0.6, 3);
        GraphWidthScale = Math.Clamp(GraphWidthScale, 0.3, 4);
        GraphHeightScale = Math.Clamp(GraphHeightScale, 0.4, 3);
        UiScale = Math.Clamp(UiScale, 0.5, 2.5);
        Width = Math.Clamp(Width, 180, 4000);
        Height = Math.Clamp(Height, 80, 2000);
        if (IntervalSeconds is not (0.5 or 1 or 2)) IntervalSeconds = 1;
        if (GraphSeconds is not (30 or 60 or 120 or 180 or 300 or 600)) GraphSeconds = 60;
        if (Backdrop is not ("blur" or "mica" or "micaalt" or "acrylic" or "none")) Backdrop = "blur";
        if (Theme is not ("system" or "dark" or "light")) Theme = "system";
        if (Layout is not ("rows" or "cards" or "tiles" or "panel" or "panelgraph")) Layout = "rows";
        if (GraphStyleName is not ("area" or "line" or "bars" or "step")) GraphStyleName = "area";
        Colors ??= new Dictionary<string, string>();
        foreach (var key in Colors.Keys.ToList())
            if (!IsValidColor(Colors[key])) Colors.Remove(key);
        Order ??= new List<string>();
        Order = Order.Where(k => k is "net" or "cpu" or "gpu" or "ram" or "disk" or "ai" or "ds")
                     .Distinct()
                     .ToList();
        AiProviders ??= new List<string>();
        AiProviders = AiProviders.Where(p => p is "deepseek" or "openai" or "anthropic").Distinct().ToList();
        AiBudgets ??= new Dictionary<string, double>();
        foreach (var p in AiBudgets.Keys.ToList())
            if (p is not ("deepseek" or "openai" or "anthropic")
                || double.IsNaN(AiBudgets[p]) || AiBudgets[p] < 0 || AiBudgets[p] > 1e6)
                AiBudgets.Remove(p);
        if (!ShowNet && !ShowCpu && !ShowGpu && !ShowRam && !ShowDisk && !ShowAi && !ShowDs) ShowCpu = true;
        Schema = 1;
        return this;
    }

    static bool IsValidColor(string v)
        => v == "auto" || (v.Length == 7 && v[0] == '#' && v.Skip(1).All(Uri.IsHexDigit));

    /// <summary>"auto" or a fixed #RRGGBB for the given element.</summary>
    public string ColorOf(string element)
        => Colors.TryGetValue(element, out var v) && v != "auto" ? v : "auto";

    /// <summary>Budget mensile impostato per il fornitore (0 = non impostato).</summary>
    public double AiBudget(string provider)
        => AiBudgets.TryGetValue(provider, out var v) && v > 0 ? v : 0;

    /// <summary>Carry an old settings.json forward. The opaque panel came from a broken
    /// "500% opacity" value written by the previous build, so legacy files get the new
    /// translucent default instead of staying black.</summary>
    WidgetConfig Migrated()
    {
        if (Schema >= 1) return this;
        if (Scale.HasValue) TextScale = Scale.Value;
        PanelOpacity = 0.45;
        SoftwareRender = true;
        Backdrop = "blur";
        return this;
    }
}

sealed class Palette
{
    public bool IsLight;
    public System.Windows.Media.Color Panel;
    public System.Windows.Media.Color PanelBorder;
    public System.Windows.Media.Color Card;
    public System.Windows.Media.Color Text;
    public System.Windows.Media.Color TextDim;
    public System.Windows.Media.Color Track;

    public System.Windows.Media.Color NetDown, NetUp, Cpu, Gpu, GpuAlt, Ram, Watt;

    /// <summary>Threshold colours for the panel layout (green / amber / red).</summary>
    public System.Windows.Media.Color Ok, Warn, Bad;

    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(k?.GetValue("SystemUsesLightTheme") ?? 0) == 1;
        }
        catch { return false; }
    }

    public static Palette For(string theme)
    {
        bool light = theme == "light" || (theme == "system" && SystemUsesLightTheme());
        return light ? Light() : Dark();
    }

    static Palette Dark() => new()
    {
        IsLight = false,
        Panel = C(0xFF, 0x18, 0x18, 0x18),
        PanelBorder = C(0x24, 0xFF, 0xFF, 0xFF),
        Card = C(0x1E, 0xFF, 0xFF, 0xFF),
        Text = C(0xF2, 0xFF, 0xFF, 0xFF),
        TextDim = C(0xA6, 0xFF, 0xFF, 0xFF),
        Track = C(0x2E, 0xFF, 0xFF, 0xFF),
        NetDown = C(0xFF, 0x4C, 0xC2, 0xFF),
        NetUp = C(0xFF, 0x6C, 0xCB, 0x5F),
        Cpu = C(0xFF, 0xA7, 0x8B, 0xFA),
        Gpu = C(0xFF, 0xFF, 0xB8, 0x4D),
        GpuAlt = C(0xFF, 0x8F, 0xA6, 0xC6),
        Ram = C(0xFF, 0x4F, 0xD1, 0xC5),
        Watt = C(0xFF, 0xFF, 0x8A, 0x65),
        Ok = C(0xFF, 0x5C, 0xD6, 0x6E),
        Warn = C(0xFF, 0xFF, 0xC1, 0x07),
        Bad = C(0xFF, 0xFF, 0x5B, 0x5B),
    };

    static Palette Light() => new()
    {
        IsLight = true,
        Panel = C(0xFF, 0xFA, 0xFA, 0xFA),
        PanelBorder = C(0x1F, 0x00, 0x00, 0x00),
        Card = C(0x14, 0x00, 0x00, 0x00),
        Text = C(0xE6, 0x00, 0x00, 0x00),
        TextDim = C(0x99, 0x00, 0x00, 0x00),
        Track = C(0x22, 0x00, 0x00, 0x00),
        NetDown = C(0xFF, 0x0A, 0x84, 0xFF),
        NetUp = C(0xFF, 0x28, 0xA7, 0x45),
        Cpu = C(0xFF, 0x6B, 0x4A, 0xE8),
        Gpu = C(0xFF, 0xD1, 0x7A, 0x00),
        GpuAlt = C(0xFF, 0x5B, 0x72, 0x94),
        Ram = C(0xFF, 0x00, 0x8F, 0x86),
        Watt = C(0xFF, 0xE0, 0x5A, 0x1E),
        Ok = C(0xFF, 0x1E, 0x9E, 0x4A),
        Warn = C(0xFF, 0xC0, 0x8A, 0x00),
        Bad = C(0xFF, 0xD3, 0x2F, 0x2F),
    };

    static System.Windows.Media.Color C(byte a, byte r, byte g, byte b)
        => System.Windows.Media.Color.FromArgb(a, r, g, b);
}
