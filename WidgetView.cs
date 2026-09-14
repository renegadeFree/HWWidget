using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextAlignment = System.Windows.TextAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace HWWidget;

internal sealed class Metrics
{
    public double NetDown, NetUp, NetLink;
    public string NetName = "";
    public double CpuUsage, CpuMhz, CpuBaseMhz;
    public string CpuName = "";
    public bool GpuOk;
    public double GpuUtil, VramUsed, VramTotal, Watts, WattsLimit, TempC, ClockMhz;
    public string GpuName = "";
    public double RamUsed, RamTotal, RamPct;
    public string RamSpeed = "";
    public bool DiskOk;
    public double DiskRead, DiskWrite;
    public AiSnapshot Ai = new();
}

/// <summary>Builds the widget content for the three layouts and binds a sample to it.
/// All colours come from the palette, so nothing depends on the WPF defaults (that is
/// how black-on-black text happened before).</summary>
internal sealed class WidgetView
{
    const string UiFont = "Segoe UI Variable Text, Segoe UI";
    const string MonoFont = "Cascadia Mono, Consolas";
    const string IconFont = "Segoe MDL2 Assets";

    readonly WidgetConfig _c;
    readonly Palette _p;
    readonly int _samples;
    readonly Dictionary<string, Series> _store;
    /// <summary>Panel layouts are proportional to the widget width, exactly like the
    /// reference widget: 1 unit = 1 pixel of that design (211 DIP wide).</summary>
    readonly double _u;
    readonly double _ts;   // text scale * global scale
    readonly double _rs;   // row scale * global scale
    readonly List<Action<Metrics>> _binds = new();

    public FrameworkElement Root { get; }

    public WidgetView(WidgetConfig c, Palette p, Dictionary<string, Series> store, double widthDip)
    {
        _c = c;
        _p = p;
        _store = store;
        _samples = (int)Math.Max(4, Math.Round(c.GraphSeconds / c.IntervalSeconds));
        _ts = c.TextScale * c.UiScale;
        _rs = c.RowScale * c.UiScale;
        _u = PanelUnit(c, widthDip);
        Root = c.Layout switch
        {
            "cards" => BuildCards(),
            "tiles" => BuildTiles(),
            "panel" => BuildPanel(false),
            "panelgraph" => BuildPanel(true),
            _ => BuildRows(),
        };
    }

    public void Bind(Metrics m)
    {
        foreach (var b in _binds) b(m);
    }

    public int BoundCount => _binds.Count;

    static double PanelUnit(WidgetConfig c, double widthDip)
        => Math.Clamp(widthDip / 211.0, 0.62, 2.4) * c.TextScale * c.UiScale;

    Series Shared(string key)
    {
        if (!_store.TryGetValue(key, out var s))
        {
            s = new Series();
            _store[key] = s;
        }
        return s;
    }

    /// <summary>Metric rows a given element will show — used to size the widget.</summary>
    public static int LineCount(WidgetConfig c, string el) => el switch
    {
        "net" => 2 + (c.ShowSecondary ? 1 : 0),
        "gpu" => 2 + (c.ShowSecondary ? 1 : 0),
        "ai" => Math.Max(1, c.AiProviders.Count) * 4,
        "ds" => 1 + DsMaxModels * 2 + 3,
        _ => 1 + (c.ShowSecondary ? 1 : 0),
    };

    // ---------- shared bits ----------

    TextBlock Txt(string text, double size, Brush? color = null, bool bold = false, bool mono = false,
                  TextAlignment align = TextAlignment.Left)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = color ?? new SolidColorBrush(_p.Text),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = new FontFamily(mono ? MonoFont : UiFont),
            TextAlignment = align,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
        return t;
    }

    Sparkline Graph(string key, Brush strokeA, Brush strokeB, double vmax, double height)
    {
        var g = new Sparkline
        {
            Series = Shared(key),
            WindowSamples = _samples,
            VMax = vmax,
            GraphStyle = _c.GraphStyle,
            StrokeA = strokeA,
            StrokeB = strokeB,
        };
        if (height > 0) g.Height = height;
        return g;
    }

    Border Surface(double radius, Color bg, Thickness pad, Thickness margin)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(bg),
            Padding = pad,
            Margin = margin,
        };
        // maschera sugli angoli tondi: i grafici arrivano ai bordi della card ma non
        // devono spuntare fuori dagli angoli
        b.SizeChanged += (_, e) =>
            b.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
        return b;
    }

    static ColumnDefinition Star(double weight)
        => new() { Width = new GridLength(Math.Max(0.05, weight), GridUnitType.Star) };

    static string Rate(double bps) => bps >= 1073741824 ? $"{bps / 1073741824:0.0} GB/s"
        : bps >= 1048576 ? $"{bps / 1048576:0.0} MB/s"
        : bps >= 1024 ? $"{bps / 1024:0} KB/s"
        : $"{bps:0} B/s";

    static string ElName(string el) => el switch
    {
        "net" => "NET",
        "cpu" => "CPU",
        "gpu" => "GPU",
        "disk" => "DISCO",
        "ai" => "AI",
        "ds" => "DEEPSEEK",
        _ => "RAM",
    };
    static string ElGlyph(string el) => el switch
    {
        "net" => "\uE968",
        "cpu" => "\uE950",
        "gpu" => "\uE7FC",
        "disk" => "\uEDA2",
        "ai" => "\uE945",
        "ds" => "\uE9D2",
        _ => "\uE964",
    };

    /// <summary>Per-element values: big number, bar fraction, small secondary line and
    /// the metric rows used by the cards layout.</summary>
    (string main, string sub, double bar, List<(string text, double value, string graph, Brush color, string key)> lines)
        Data(string el, Metrics m)
    {
        var lines = new List<(string, double, string, Brush, string)>();
        switch (el)
        {
            case "net":
            {
                double link = m.NetLink > 0 ? m.NetLink : 1;
                lines.Add(($"↓ {Rate(m.NetDown)}", m.NetDown, "down", new SolidColorBrush(_p.NetDown), "bps"));
                lines.Add(($"↑ {Rate(m.NetUp)}", m.NetUp, "up", new SolidColorBrush(_p.NetUp), "bps"));
                if (_c.ShowSecondary && m.NetLink > 0)
                    lines.Add(($"{m.NetLink / 1e9:0.0} Gbps · {m.NetName}", 0, "", new SolidColorBrush(_p.TextDim), ""));
                return (Rate(m.NetDown), $"↑ {Rate(m.NetUp)}", m.NetDown / link, lines);
            }
            case "cpu":
            {
                string freq = m.CpuBaseMhz <= 0 ? "" : $"{m.CpuMhz / 1000.0:0.00} GHz";
                lines.Add(($"{m.CpuUsage:0}%", m.CpuUsage, "use", new SolidColorBrush(_p.Cpu), "pct"));
                if (_c.ShowSecondary && freq.Length > 0)
                    lines.Add((freq, m.CpuMhz, "freq", new SolidColorBrush(_p.Ram), "mhz"));
                return ($"{m.CpuUsage:0}%", freq, m.CpuUsage / 100.0, lines);
            }
            case "gpu":
            {
                if (!m.GpuOk)
                {
                    lines.Add(("n/d", 0, "", new SolidColorBrush(_p.TextDim), ""));
                    return ("n/d", "", 0, lines);
                }
                double vramPct = m.VramTotal > 0 ? m.VramUsed * 100 / m.VramTotal : 0;
                lines.Add(($"{m.GpuUtil:0}%", m.GpuUtil, "use", new SolidColorBrush(_p.Gpu), "pct"));
                lines.Add(($"{m.VramUsed:0.0}/{m.VramTotal:0.0} GB", vramPct, "vram", new SolidColorBrush(_p.GpuAlt), "pct"));
                if (_c.ShowSecondary)
                    lines.Add(($"{m.Watts:0} W", m.WattsLimit > 0 ? m.Watts * 100 / m.WattsLimit : 0, "watt", new SolidColorBrush(_p.Watt), "pct"));
                string sub = $"{m.VramUsed:0.0}/{m.VramTotal:0.0} GB" + (_c.ShowSecondary ? $" · {m.Watts:0} W" : "");
                return ($"{m.GpuUtil:0}%", sub, m.GpuUtil / 100.0, lines);
            }
            default:
            {
                lines.Add(($"{m.RamPct:0}%", m.RamPct, "use", new SolidColorBrush(_p.Ram), "pct"));
                if (_c.ShowSecondary)
                    lines.Add(($"{m.RamUsed:0}/{m.RamTotal:0} GB", m.RamPct, "used", new SolidColorBrush(_p.NetDown), "pct"));
                string sub = $"{m.RamUsed:0}/{m.RamTotal:0} GB" + (m.RamSpeed.Length > 0 ? $" · {m.RamSpeed}" : "");
                return ($"{m.RamPct:0}%", sub, m.RamPct / 100.0, lines);
            }
            case "disk":
            {
                lines.Add(($"L {Rate(m.DiskRead)}", m.DiskRead, "read", new SolidColorBrush(_p.NetDown), "bps"));
                lines.Add(($"S {Rate(m.DiskWrite)}", m.DiskWrite, "write", new SolidColorBrush(_p.Watt), "bps"));
                return (Rate(m.DiskRead), $"S {Rate(m.DiskWrite)}", 0, lines);
            }
            case "ai":
            {
                // budget rimasto, budget consumato e token totali del primo fornitore attivo:
                // la sezione AI non ha grafici, qui serve solo un riassunto testuale
                string p = _c.AiProviders.FirstOrDefault() ?? "deepseek";
                string rimasto = AiRemaining(p, m);
                lines.Add(($"Rimasto {rimasto}", 0, "", new SolidColorBrush(_p.Ok), ""));
                lines.Add(($"Consumato {AiSpent(p, m)}", 0, "", new SolidColorBrush(_p.NetDown), ""));
                lines.Add(($"Token {AiTokens(p, m)}", 0, "", new SolidColorBrush(_p.Cpu), ""));
                return (rimasto, $"Token {AiTokens(p, m)}", 0, lines);
            }
            case "ds":
            {
                var d = m.Ai.Deep;
                string bal = d.Balance.Length > 0 ? d.Balance : "n/d";
                string spent = d.MonthCost.Length > 0 ? d.MonthCost : "n/d";
                lines.Add(($"Saldo {bal}", 0, "", new SolidColorBrush(DsAccent), ""));
                lines.Add(($"Speso {spent}", 0, "", new SolidColorBrush(DsHit), ""));
                return (bal, $"Speso {spent}", 0, lines);
            }
        }
    }

    // ---------- layout: rows ----------

    FrameworkElement BuildRows()
    {
        var stack = new StackPanel();
        foreach (string el in _c.Elements)
        {
            var grid = new Grid { Height = 30 * _rs, Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(Star(1));
            grid.ColumnDefinitions.Add(Star(_c.GraphWidthScale));

            var label = Txt(ElName(el), 10.5 * _ts, new SolidColorBrush(_p.TextDim), bold: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 10, 0);
            var value = Txt("—", 12.5 * _ts, new SolidColorBrush(_p.Text), mono: true);
            value.VerticalAlignment = VerticalAlignment.Center;
            // la sezione AI è solo testo: niente grafico
            var graph = el == "ai" ? null : Graph($"rows.{el}.main", new SolidColorBrush(_p.Text), Brushes.Transparent, 100,
                                                  Math.Max(8, (30 * _rs) - 4) * _c.GraphHeightScale);
            if (graph != null)
            {
                graph.VerticalAlignment = VerticalAlignment.Center;
                graph.Margin = new Thickness(8, 0, 0, 0);
            }

            Grid.SetColumn(label, 0);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
            if (graph != null) { Grid.SetColumn(graph, 2); grid.Children.Add(graph); }
            stack.Children.Add(grid);

            _binds.Add(m =>
            {
                var d = Data(el, m);
                value.Text = el switch
                {
                    "net" => $"{d.lines[0].text}  {d.lines[1].text}",
                    "cpu" => d.sub.Length > 0 ? $"{d.main} · {d.sub}" : d.main,
                    "gpu" => d.sub.Length > 0 ? $"{d.main} · {d.sub}" : d.main,
                    "ai" => d.sub.Length > 0 ? $"{d.main} · {d.sub}" : d.main,
                    _ => d.sub.Length > 0 ? $"{d.sub}" : d.main,
                };
                var primary = d.lines[0];
                if (graph == null) return;
                graph.StrokeA = primary.color;
                graph.StrokeB = el == "net" && d.lines.Count > 1 ? d.lines[1].color : Brushes.Transparent;
                if (el == "net") graph.Push(m.NetDown, m.NetUp);
                else graph.Push(primary.value);
            });
        }
        return stack;
    }

    // ---------- layout: cards (one card per element, big values + graphs) ----------

    FrameworkElement BuildCards()
    {
        var stack = new StackPanel();
        foreach (string el in _c.Elements)
        {
            var inner = new StackPanel();
            var title = Txt(ElName(el), 17 * _ts, new SolidColorBrush(_p.TextDim), bold: true, align: TextAlignment.Right);
            title.Margin = new Thickness(0, 0, 2, 2);
            inner.Children.Add(title);

            var rows = new List<(Sparkline? graph, int key)>();
            var texts = new List<TextBlock>();
            var defs = Data(el, new Metrics { GpuOk = true, GpuUtil = 1, CpuUsage = 1, RamPct = 1, NetDown = 1 });

            for (int i = 0; i < defs.lines.Count; i++)
            {
                var g = new Grid { Height = 34 * _rs };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(Star(_c.GraphWidthScale));

                var val = Txt("—", 20 * _ts, new SolidColorBrush(_p.Text), mono: true, align: TextAlignment.Left);
                val.VerticalAlignment = VerticalAlignment.Center;
                val.MinWidth = 96 * _ts;
                var sp = el == "ai" ? null : Graph($"cards.{el}.{i}", new SolidColorBrush(_p.Text), Brushes.Transparent, 100,
                                                   26 * _rs * _c.GraphHeightScale);
                if (sp != null)
                {
                    sp.VerticalAlignment = VerticalAlignment.Center;
                    sp.Margin = new Thickness(10, 0, 0, 0);
                }

                Grid.SetColumn(val, 0);
                g.Children.Add(val);
                if (sp != null) { Grid.SetColumn(sp, 1); g.Children.Add(sp); }
                inner.Children.Add(g);

                int idx = i;
                texts.Add(val);
                rows.Add((sp, idx));
            }

            var card = Surface(10, _p.Card, new Thickness(12, 8, 12, 10), new Thickness(0, 0, 0, 6));
            card.Child = inner;
            stack.Children.Add(card);

            _binds.Add(m =>
            {
                var d = Data(el, m);
                for (int i = 0; i < texts.Count && i < d.lines.Count; i++)
                {
                    texts[i].Text = d.lines[i].text;
                    var (sp, _) = rows[i];
                    if (sp == null) continue;
                    sp.StrokeA = d.lines[i].color;
                    if (el == "net" && i == 0 && d.lines.Count > 1)
                    {
                        sp.StrokeB = d.lines[1].color;
                        sp.Push(d.lines[0].value, d.lines[1].value);
                    }
                    else if (d.lines[i].key.Length > 0)
                    {
                        sp.Push(d.lines[i].value);
                    }
                }
            });
        }
        return stack;
    }

    // ---------- layout: tiles (icon, big value, bar, mini graph) ----------

    // ---------- layout: panel (LiteMonitor style: sections, label/value + level bar) ----------

    static readonly string[] PanelOrder = { "cpu", "gpu", "ram", "disk", "net", "ai", "ds" };

    // colori DeepSeek: accento del brand, poi i tre della legenda del monitor di riferimento
    static readonly Color DsAccent = Color.FromRgb(0x4D, 0x6B, 0xFE);
    static readonly Color DsHit = Color.FromRgb(0x30, 0xC4, 0x7A);
    static readonly Color DsMiss = Color.FromRgb(0xF5, 0x9E, 0x0B);
    static readonly Color DsOut = Color.FromRgb(0xA8, 0x55, 0xF7);
    const int DsMaxModels = 3;

    /// <summary>Spazio fra due righe di una sezione (segue la scala righe).</summary>
    double RowGap => 14 * _u * _rs;
    /// <summary>Margine interno orizzontale delle card: i grafici lo usano al negativo
    /// per arrivare ai bordi del box.</summary>
    double CardPad => 12 * _u;
    /// <summary>Margine sotto l'ultima riga di una card.</summary>
    double CardBottom => 4 * _u * _rs;
    /// <summary>Distanza del grafico dal bordo inferiore della card: senza, la linea di base
    /// finisce a contatto con il bordo del box.</summary>
    const double GraphClearance = 3;

    /// <summary>Green under 60%, amber to 85%, red above — temperatures shift the bands.</summary>
    SolidColorBrush Level(double value, bool isTemp)
    {
        var c = isTemp
            ? value >= 80 ? _p.Bad : value >= 60 ? _p.Warn : _p.Ok
            : value >= 85 ? _p.Bad : value >= 60 ? _p.Warn : _p.Ok;
        return new SolidColorBrush(c);
    }

    FrameworkElement BuildPanel(bool graphs)
    {
        var root = new StackPanel();

        if (_c.ShowTitle)
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2 * _u, 0, 0, 5 * _u) };
            title.Children.Add(new TextBlock
            {
                Text = "\uE945",
                FontFamily = new FontFamily(IconFont),
                FontSize = 13 * _u,
                Foreground = new SolidColorBrush(_p.Text),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6 * _u, 0),
            });
            title.Children.Add(Txt("HW Widget", 13 * _u, new SolidColorBrush(_p.Text), bold: true));
            root.Children.Add(title);
        }

        foreach (string el in PanelOrderFor())
            root.Children.Add(PanelSection(el, graphs));
        return root;
    }

    /// <summary>Panel default order is CPU, GPU, RAM, Disco, Rete; an explicit user order
    /// (set from the hub) always wins.</summary>
    IEnumerable<string> PanelOrderFor()
    {
        var visible = _c.Elements.ToList();
        if (_c.Order.Count > 0) return visible;
        var ordered = PanelOrder.Where(visible.Contains).ToList();
        ordered.AddRange(visible.Where(e => !PanelOrder.Contains(e)));
        return ordered;
    }

    FrameworkElement PanelSection(string el, bool graphs)
    {
        // margine di sezione + padding inferiore della card = RowGap: dopo l'ultima riga
        // di una sezione c'è lo stesso spazio che c'è fra due righe
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 10 * _u * _rs) };

        // header: icon + name only, no box (as in the reference)
        var headerContent = new StackPanel { Orientation = Orientation.Horizontal };
        headerContent.Children.Add(new TextBlock
        {
            Text = ElGlyph(el),
            FontFamily = new FontFamily(IconFont),
            FontSize = 11.5 * _u,
            Foreground = new SolidColorBrush(_p.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5 * _u, 0),
        });
        headerContent.Children.Add(Txt(ElName(el), 11.5 * _u, new SolidColorBrush(_p.Text), bold: true));
        headerContent.Margin = new Thickness(2 * _u, 0, 0, 3 * _u * _rs);
        section.Children.Add(headerContent);

        // one card per section: the metric rows sit one under the other inside it
        var body = new StackPanel();
        switch (el)
        {
            case "cpu":
                body.Children.Add(MetricRow($"{el}.use", el, "Uso", m => $"{m.CpuUsage:0.0}%", m => m.CpuUsage, m => m.CpuUsage, graphs, false, true));
                break;
            case "gpu":
                body.Children.Add(MetricRow($"{el}.use", el, "Uso", m => m.GpuOk ? $"{m.GpuUtil:0.0}%" : "n/d", m => m.GpuUtil, m => m.GpuUtil, graphs, false, false));
                body.Children.Add(MetricRow($"{el}.temp", el, "Temp", m => m.GpuOk ? $"{m.TempC:0.0} °C" : "n/d", m => m.TempC, m => m.TempC, graphs, true, false));
                body.Children.Add(MetricRow($"{el}.vram", el, "VRAM", m => m.VramTotal > 0 ? $"{m.VramUsed * 100 / m.VramTotal:0.0}%" : "n/d",
                    m => m.VramTotal > 0 ? m.VramUsed * 100 / m.VramTotal : 0,
                    m => m.VramTotal > 0 ? m.VramUsed * 100 / m.VramTotal : 0, graphs, false, true));
                break;
            case "ram":
                body.Children.Add(MetricRow($"{el}.use", el, "Uso", m => $"{m.RamPct:0.0}%", m => m.RamPct, m => m.RamPct, graphs, false, true));
                break;
            case "disk":
                body.Children.Add(PairRow(el, graphs,
                    ("Lettura", m => Rate(m.DiskRead), new SolidColorBrush(_p.NetDown), m => m.DiskRead),
                    ("Scrittura", m => Rate(m.DiskWrite), new SolidColorBrush(_p.Watt), m => m.DiskWrite)));
                break;
            case "ai":
                foreach (var row in AiRows())
                    body.Children.Add(row);
                break;
            case "ds":
                foreach (var row in DsRows())
                    body.Children.Add(row);
                break;
            default:
                body.Children.Add(PairRow(el, graphs,
                    ("Upload", m => Rate(m.NetUp), new SolidColorBrush(_p.NetUp), m => m.NetUp),
                    ("Download", m => Rate(m.NetDown), new SolidColorBrush(_p.NetDown), m => m.NetDown)));
                break;
        }
        var card = Surface(9 * _u, _p.Card, new Thickness(CardPad, 9 * _u * _rs, CardPad, CardBottom), new Thickness(0));
        card.Child = body;
        section.Children.Add(card);
        return section;
    }

    /// <summary>Sezione AI: senza grafici, solo budget rimasto, budget consumato e token
    /// totali. Un fornitore disattivato non compare nel widget.</summary>
    IEnumerable<FrameworkElement> AiRows()
    {
        var rows = new List<FrameworkElement>();
        foreach (string p in _c.AiProviders)
        {
            string provider = p;
            var (name, color) = provider switch
            {
                "openai" => ("ChatGPT", _p.NetDown),
                "anthropic" => ("Claude", _p.Watt),
                _ => ("DeepSeek", _p.Ok),
            };
            var header = Txt(name, 11.5 * _u, new SolidColorBrush(color), bold: true);
            header.Margin = new Thickness(0, rows.Count == 0 ? 0 : 9 * _u * _rs, 0, 4 * _u * _rs);
            rows.Add(header);
            rows.Add(AiLine("Rimasto", m => AiRemaining(provider, m), color, false));
            rows.Add(AiLine("Consumato", m => AiSpent(provider, m), color, false));
            rows.Add(AiLine("Token totali", m => AiTokens(provider, m), color, true));
        }
        if (rows.Count == 0)
            rows.Add(Txt("Nessun fornitore attivo", 10.5 * _u, new SolidColorBrush(_p.TextDim)));
        return rows;
    }

    FrameworkElement AiLine(string label, Func<Metrics, string> text, Color color, bool last)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, last ? 0 : 6 * _u * _rs) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lab = Txt(label, 10.5 * _u, new SolidColorBrush(_p.TextDim));
        lab.VerticalAlignment = VerticalAlignment.Center;
        var val = Txt("—", 12 * _u, new SolidColorBrush(color), bold: true, mono: true, align: TextAlignment.Right);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lab);
        grid.Children.Add(val);
        _binds.Add(m => val.Text = text(m));
        return grid;
    }

    /// <summary>Budget rimasto: saldo DeepSeek, oppure budget mensile meno spesa API.</summary>
    string AiRemaining(string provider, Metrics m) => provider switch
    {
        "deepseek" => m.Ai.DeepSeekBalance.Length > 0 ? m.Ai.DeepSeekBalance : "n/d",
        "openai" => Remaining(_c.AiBudget(provider), m.Ai.OpenAiMonthValue, m.Ai.OpenAiMonth),
        _ => Remaining(_c.AiBudget(provider), m.Ai.AnthropicMonthValue, m.Ai.AnthropicMonth),
    };

    static string Remaining(double budget, double spent, string spentText)
        => budget <= 0 || spentText.Length == 0 ? "n/d" : Money(Math.Max(0, budget - spent));

    string AiSpent(string provider, Metrics m) => provider switch
    {
        "deepseek" => m.Ai.DeepSeekSpent.Length > 0 ? m.Ai.DeepSeekSpent : "n/d",
        "openai" => m.Ai.OpenAiMonth.Length > 0 ? m.Ai.OpenAiMonth : "n/d",
        _ => m.Ai.AnthropicMonth.Length > 0 ? m.Ai.AnthropicMonth : "n/d",
    };

    /// <summary>Token totali: dai log locali delle CLI (DeepSeek non li espone).</summary>
    string AiTokens(string provider, Metrics m) => provider switch
    {
        "openai" => m.Ai.CodexTokens.Length > 0 ? m.Ai.CodexTokens : "n/d",
        "anthropic" => m.Ai.ClaudeTokens.Length > 0 ? m.Ai.ClaudeTokens : "n/d",
        _ => "n/d",
    };

    internal static string Money(double v) => v.ToString("0.00", CultureInfo.CurrentCulture) + " $";

    /// <summary>Sezione DeepSeek completa, come il monitor di riferimento: saldo con
    /// disponibilità, costo di oggi e del mese, un riquadro per modello (token, richieste,
    /// cache hit, costo) e il grafico giornaliero impilato con la legenda.</summary>
    IEnumerable<FrameworkElement> DsRows()
    {
        var accent = _c.ColorOf("ds") != "auto" ? BrushFromHex(_c.ColorOf("ds")) : (Brush)new SolidColorBrush(DsAccent);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Txt("Saldo", 10.5 * _u, new SolidColorBrush(_p.TextDim));
        label.VerticalAlignment = VerticalAlignment.Center;
        var chipText = Txt("—", 9.5 * _u, new SolidColorBrush(DsHit), bold: true);
        var chip = new Border
        {
            CornerRadius = new CornerRadius(8 * _u),
            Padding = new Thickness(8 * _u, 2 * _u, 8 * _u, 2 * _u),
            Background = new SolidColorBrush(Color.FromArgb(0x2E, DsHit.R, DsHit.G, DsHit.B)),
            Child = chipText,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(chip, 1);
        head.Children.Add(label);
        head.Children.Add(chip);

        var balance = Txt("—", 20 * _u, accent, bold: true, mono: true);
        balance.Margin = new Thickness(0, 2 * _u * _rs, 0, 5 * _u * _rs);
        var pair = new Grid();
        pair.ColumnDefinitions.Add(Star(1));
        pair.ColumnDefinitions.Add(Star(1));
        var (todayBox, todayText) = DsStat("Oggi", accent);
        var (monthBox, monthText) = DsStat("Mese", accent);
        monthBox.Margin = new Thickness(6 * _u, 0, 0, 0);
        Grid.SetColumn(todayBox, 0);
        Grid.SetColumn(monthBox, 1);
        pair.Children.Add(todayBox);
        pair.Children.Add(monthBox);

        _binds.Add(m =>
        {
            var d = m.Ai.Deep;
            balance.Text = d.Balance.Length > 0 ? d.Balance : "n/d";
            chip.Visibility = d.Balance.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var tone = d.Available ? DsHit : _p.Warn;
            chip.Background = new SolidColorBrush(Color.FromArgb(0x2E, tone.R, tone.G, tone.B));
            chipText.Text = d.Available ? "disponibile" : "non disponibile";
            chipText.Foreground = new SolidColorBrush(tone);
            todayText.Text = d.TodayCost.Length > 0 ? d.TodayCost : "n/d";
            monthText.Text = d.MonthCost.Length > 0 ? d.MonthCost : "n/d";
        });

        var rows = new List<FrameworkElement> { head, balance, pair };
        var slots = new List<DsSlot>();
        for (int i = 0; i < DsMaxModels; i++)
        {
            var slot = DsModelRow(accent);
            slots.Add(slot);
            rows.Add(slot.Box);
        }

        // grafico giornaliero: barre impilate (cache hit, miss, output) come nel riferimento
        var caption = new Grid { Margin = new Thickness(0, 9 * _u * _rs, 0, 0) };
        caption.Visibility = Visibility.Collapsed;
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        caption.Children.Add(Txt("Consumo giornaliero", 10.5 * _u, new SolidColorBrush(_p.TextDim)));
        var range = Txt("", 10 * _u, new SolidColorBrush(_p.TextDim), align: TextAlignment.Right);
        Grid.SetColumn(range, 1);
        caption.Children.Add(range);
        rows.Add(caption);

        var spark = new Sparkline
        {
            Series = Shared("ds.days"),
            WindowSamples = 30,
            Stacked = true,
            VMax = 0,                       // scala automatica sulla somma dei tre valori
            GraphStyle = GraphStyle.Bars,
            StrokeA = new SolidColorBrush(DsHit),
            StrokeB = new SolidColorBrush(DsMiss),
            StrokeC = new SolidColorBrush(DsOut),
            Height = 44 * _u * _c.GraphHeightScale,
            Margin = new Thickness(-CardPad, 5 * _u * _rs, -CardPad, 0),
            Visibility = Visibility.Collapsed,
        };
        rows.Add(spark);
        var legend = DsLegend();
        legend.Visibility = Visibility.Collapsed;
        rows.Add(legend);
        var hint = Txt("", 9.5 * _u, new SolidColorBrush(_p.TextDim));
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 6 * _u * _rs, 0, 0);
        rows.Add(hint);

        DateTime? seen = null;
        _binds.Add(m =>
        {
            var d = m.Ai.Deep;
            if (seen == d.FetchedUtc) return;
            seen = d.FetchedUtc;
            var series = Shared("ds.days");
            series.Clear();
            foreach (var day in d.Days) series.Push(day.Hit, day.Miss, day.Out);
            spark.WindowSamples = Math.Max(2, d.Days.Count);
            spark.Visibility = d.Days.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            legend.Visibility = spark.Visibility;
            caption.Visibility = spark.Visibility;
            if (d.Days.Count > 0) range.Text = $"{d.Days[0].Label} – {d.Days[^1].Label}";
            // nel widget una riga corta: i dettagli (e il motivo) stanno nell'hub
            hint.Text = d.HasUsage ? "" : "token di utilizzo non impostato o non valido · Hub → Impostazioni app";
            hint.Visibility = hint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (i >= d.Models.Count) { s.Box.Visibility = Visibility.Collapsed; continue; }
                var model = d.Models[i];
                s.Box.Visibility = Visibility.Visible;
                s.Name.Text = model.Name;
                s.Cost.Text = DeepSeekUsage.Text(model.Cost, d.Currency);
                s.Tokens.Text = AiUsage.Tokens(model.Tokens);
                s.Requests.Text = model.Requests.ToString("N0", CultureInfo.CurrentCulture);
                double pct = Math.Clamp(model.HitPct, 0, 100);
                s.Percent.Text = $"{pct:0}%";
                double w = s.Bar.ActualWidth > 2 ? s.Bar.ActualWidth : 120;
                s.Fill.Width = Math.Max(0, pct / 100) * w;
            }
            spark.Refresh();
        });
        return rows;
    }

    sealed class DsSlot
    {
        public FrameworkElement Box = null!;
        public TextBlock Name = null!, Cost = null!, Tokens = null!, Requests = null!, Percent = null!;
        public Grid Bar = null!;
        public Border Fill = null!;
    }

    /// <summary>Riquadro di un modello: nome, costo, token, richieste e barra cache hit.</summary>
    DsSlot DsModelRow(Brush accent)
    {
        var inner = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Txt("—", 12.5 * _u, accent, bold: true);
        var cost = Txt("—", 12.5 * _u, new SolidColorBrush(_p.Text), bold: true, mono: true, align: TextAlignment.Right);
        Grid.SetColumn(cost, 1);
        top.Children.Add(name);
        top.Children.Add(cost);
        inner.Children.Add(top);

        var stats = new Grid { Margin = new Thickness(0, 5 * _u * _rs, 0, 0) };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tokens = Txt("—", 11.5 * _u, new SolidColorBrush(_p.Text), mono: true, bold: true);
        var requests = Txt("—", 10.5 * _u, new SolidColorBrush(_p.TextDim), mono: true, align: TextAlignment.Right);
        Grid.SetColumn(requests, 1);
        stats.Children.Add(tokens);
        stats.Children.Add(requests);
        inner.Children.Add(stats);

        var track = new Border
        {
            Height = 8 * _u * _rs,
            CornerRadius = new CornerRadius(4 * _u * _rs),
            Background = new SolidColorBrush(_p.Track),
        };
        var fill = new Border
        {
            Height = 8 * _u * _rs,
            Width = 0,
            CornerRadius = new CornerRadius(4 * _u * _rs),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(DsHit),
        };
        var percent = Txt("—", 10 * _u, new SolidColorBrush(DsHit), bold: true, mono: true, align: TextAlignment.Right);
        var bar = new Grid { Margin = new Thickness(0, 5 * _u * _rs, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var host = new Grid();
        host.Children.Add(track);
        host.Children.Add(fill);
        Grid.SetColumn(host, 0);
        Grid.SetColumn(percent, 1);
        bar.Children.Add(host);
        bar.Children.Add(percent);
        inner.Children.Add(bar);

        var box = Surface(8 * _u, Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
                          new Thickness(10 * _u, 8 * _u * _rs, 10 * _u, 9 * _u * _rs),
                          new Thickness(0, 7 * _u * _rs, 0, 0));
        box.Child = inner;
        return new DsSlot
        {
            Box = box, Name = name, Cost = cost, Tokens = tokens,
            Requests = requests, Percent = percent, Bar = host, Fill = fill,
        };
    }

    /// <summary>Riquadro "Oggi" / "Mese" con l'importo.</summary>
    (FrameworkElement Box, TextBlock Value) DsStat(string label, Brush accent)
    {
        var stack = new StackPanel();
        stack.Children.Add(Txt(label, 9.5 * _u, new SolidColorBrush(_p.TextDim), bold: true));
        var value = Txt("—", 11 * _u, accent, bold: true, mono: true);
        value.Margin = new Thickness(0, 2 * _u, 0, 0);
        stack.Children.Add(value);
        var box = Surface(7 * _u, Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
                          new Thickness(7 * _u, 6 * _u * _rs, 7 * _u, 7 * _u * _rs), new Thickness(0));
        box.Child = stack;
        return (box, value);
    }

    FrameworkElement DsLegend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6 * _u * _rs, 0, 0) };
        foreach (var (color, text) in new[] { (DsHit, "cache hit"), (DsMiss, "miss"), (DsOut, "output") })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10 * _u, 0) };
            item.Children.Add(new Border
            {
                Width = 6 * _u,
                Height = 6 * _u,
                CornerRadius = new CornerRadius(3 * _u),
                Background = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4 * _u, 0),
            });
            item.Children.Add(Txt(text, 9.5 * _u, new SolidColorBrush(_p.TextDim)));
            row.Children.Add(item);
        }
        return row;
    }

    /// <summary>Colour of a meter: the fixed per-element colour when set, otherwise the
    /// threshold colours.</summary>
    Brush MeterColor(string element, double pct, bool isTemp)
    {
        string hex = _c.ColorOf(element);
        if (hex != "auto") return BrushFromHex(hex);
        return Level(pct, isTemp);
    }

    internal static Brush BrushFromHex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    FrameworkElement MetricRow(string key, string element, string label, Func<Metrics, string> text, Func<Metrics, double> pct,
                               Func<Metrics, double> graphValue, bool graphs, bool isTemp, bool isLast)
    {
        var inner = new StackPanel { Margin = new Thickness(0, 0, 0, isLast ? 0 : RowGap) };

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lab = Txt(label, 10 * _u, new SolidColorBrush(_p.TextDim));
        lab.VerticalAlignment = VerticalAlignment.Center;
        var val = Txt("—", 12 * _u, new SolidColorBrush(_p.Text), bold: true, mono: true, align: TextAlignment.Right);
        Grid.SetColumn(val, 1);
        top.Children.Add(lab);
        top.Children.Add(val);
        inner.Children.Add(top);

        var track = new Border
        {
            Height = 8.5 * _u * _rs,
            CornerRadius = new CornerRadius(4.25 * _u * _rs),
            Background = new SolidColorBrush(_p.Track),
        };
        var fill = new Border
        {
            Height = 8.5 * _u * _rs,
            Width = 0,
            CornerRadius = new CornerRadius(4.25 * _u * _rs),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(_p.Ok),
        };
        var bar = new Grid { Margin = new Thickness(0, 5 * _u * _rs, 0, 0) };
        bar.Children.Add(track);
        bar.Children.Add(fill);
        inner.Children.Add(bar);

        Sparkline? graph = null;
        if (graphs)
        {
            graph = Graph($"panel.{key}", new SolidColorBrush(_p.Ok), Brushes.Transparent, isTemp ? 0 : 100, 24 * _u * _c.GraphHeightScale);
            // il grafico arriva ai bordi del box: ai lati sempre, in basso solo nell'ultima
            // riga (dove sotto c'è il bordo della card); il pieno sfuma verso i bordi
            graph.Margin = new Thickness(-CardPad, 6 * _u * _rs, -CardPad, isLast ? GraphClearance : 0);
            inner.Children.Add(graph);
        }

        _binds.Add(m =>
        {
            val.Text = text(m);
            double p = Math.Clamp(pct(m), 0, 100);
            var color = MeterColor(element, p, isTemp);
            val.Foreground = color;
            fill.Background = color;
            double w = bar.ActualWidth > 2 ? bar.ActualWidth : 140;
            fill.Width = Math.Max(0, p / 100.0) * w;
            if (graph != null)
            {
                graph.StrokeA = color;
                graph.Push(graphValue(m));
            }
        });
        return inner;
    }

    FrameworkElement PairRow(string element, bool graphs,
                             (string label, Func<Metrics, string> text, Brush color, Func<Metrics, double> value) left,
                             (string label, Func<Metrics, string> text, Brush color, Func<Metrics, double> value) right)
    {
        // la scala righe allarga anche la coppia lettura/scrittura (o upload/download)
        var grid = new Grid { Margin = new Thickness(0, 3 * _u * (_rs - 1), 0, 3 * _u * (_rs - 1)) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // a fixed per-element colour applies to both columns, otherwise keep the two defaults
        string hex = _c.ColorOf(element);
        Brush colorLeft = hex != "auto" ? BrushFromHex(hex) : left.color;
        Brush colorRight = hex != "auto" ? BrushFromHex(hex) : right.color;

        var valLeft = Txt("—", 12.5 * _u, colorLeft, bold: true, mono: true, align: TextAlignment.Center);
        var valRight = Txt("—", 12.5 * _u, colorRight, bold: true, mono: true, align: TextAlignment.Center);

        var cellLeft = new StackPanel();
        cellLeft.Children.Add(Txt(left.label, 10 * _u, new SolidColorBrush(_p.TextDim), align: TextAlignment.Center));
        cellLeft.Children.Add(valLeft);
        var cellRight = new StackPanel { Margin = new Thickness(8 * _u, 0, 0, 0) };
        cellRight.Children.Add(Txt(right.label, 10 * _u, new SolidColorBrush(_p.TextDim), align: TextAlignment.Center));
        cellRight.Children.Add(valRight);

        Sparkline? graphLeft = null, graphRight = null;
        if (graphs)
        {
            graphLeft = Graph($"panel.{element}.l", colorLeft, Brushes.Transparent, 0, 22 * _u * _c.GraphHeightScale);
            graphLeft.Margin = new Thickness(-CardPad, 5 * _u * _rs, 0, GraphClearance);
            cellLeft.Children.Add(graphLeft);
            graphRight = Graph($"panel.{element}.r", colorRight, Brushes.Transparent, 0, 22 * _u * _c.GraphHeightScale);
            graphRight.Margin = new Thickness(0, 5 * _u * _rs, -CardPad, GraphClearance);
            cellRight.Children.Add(graphRight);
        }

        Grid.SetColumn(cellLeft, 0);
        Grid.SetColumn(cellRight, 1);
        grid.Children.Add(cellLeft);
        grid.Children.Add(cellRight);

        _binds.Add(m =>
        {
            valLeft.Text = left.text(m);
            valRight.Text = right.text(m);
            graphLeft?.Push(left.value(m));
            graphRight?.Push(right.value(m));
        });
        return grid;
    }

    FrameworkElement BuildTiles()
    {
        var root = new StackPanel();
        var grid = new UniformGrid { Columns = _c.Elements.Count() == 1 ? 1 : 2, Margin = new Thickness(-4) };
        root.Children.Add(grid);

        foreach (string el in _c.Elements)
        {
            var icon = new TextBlock
            {
                Text = ElGlyph(el),
                FontFamily = new FontFamily(IconFont),
                FontSize = 15 * _ts,
                Foreground = new SolidColorBrush(_p.TextDim),
            };
            var main = Txt("—", 26 * _ts, new SolidColorBrush(_p.Text), bold: true, mono: true);
            var name = Txt(ElName(el), 10 * _ts, new SolidColorBrush(_p.TextDim), bold: true);
            var sub = Txt("", 10 * _ts, new SolidColorBrush(_p.TextDim));

            var track = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(_p.Track),
                Margin = new Thickness(0, 5, 0, 5),
            };
            var fill = new Border
            {
                Height = 3,
                Width = 0,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(_p.Cpu),
            };
            var trackGrid = new Grid();
            trackGrid.Children.Add(track);
            trackGrid.Children.Add(fill);

            var graph = el == "ai" ? null : Graph($"tiles.{el}.main", new SolidColorBrush(_p.Text), Brushes.Transparent, 100,
                                                  18 * _ts * _c.GraphHeightScale);
            if (graph != null) graph.Margin = new Thickness(0, 2, 0, 0);

            var inner = new StackPanel();
            inner.Children.Add(icon);
            inner.Children.Add(name);
            inner.Children.Add(main);
            // la sezione AI è solo testo: niente barra né grafico
            if (el != "ai")
            {
                inner.Children.Add(trackGrid);
                inner.Children.Add(graph!);
            }
            inner.Children.Add(sub);

            var tile = Surface(10, _p.Card, new Thickness(10, 8, 10, 10), new Thickness(4));
            tile.Child = inner;
            grid.Children.Add(tile);

            _binds.Add(m =>
            {
                var d = Data(el, m);
                main.Text = d.main;
                sub.Text = d.sub;
                var c = d.lines[0].color;
                if (graph == null) return;
                fill.Background = c;
                graph.StrokeA = c;
                double w = trackGrid.ActualWidth > 2 ? trackGrid.ActualWidth : 90;
                fill.Width = Math.Max(0, Math.Min(1, d.bar)) * w;
                if (el == "net") graph.Push(d.lines[0].value, d.lines.Count > 1 ? d.lines[1].value : double.NaN);
                else graph.Push(d.lines[0].value);
            });
        }
        return root;
    }
}

