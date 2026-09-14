using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace HWWidget;

/// <summary>Ring buffer of samples. Lives outside the visual tree so rebuilding the
/// widget (resize, layout change, theme change) never loses the graph history.</summary>
public sealed class Series
{
    const int Cap = 1200;
    readonly double[] _a = new double[Cap];
    readonly double[] _b = new double[Cap];
    int _head, _count;
    bool _hasB;

    public int Count => _count;
    public bool HasB => _hasB;
    public int Head => _head;
    public double A(int i) => _a[i];
    public double B(int i) => _b[i];
    public double Last => _a[(_head - 1 + Cap) % Cap];
    public double Oldest(int visible) => _a[(_head - Math.Min(visible, _count) + Cap) % Cap];

    public void Push(double a, double b = double.NaN)
    {
        _a[_head] = a;
        _b[_head] = b;
        if (!double.IsNaN(b)) _hasB = true;
        _head = (_head + 1) % Cap;
        if (_count < Cap) _count++;
    }

    public void Clear()
    {
        Array.Clear(_a);
        Array.Clear(_b);
        _head = _count = 0;
        _hasB = false;
    }
}

/// <summary>Sparkline view bound to a <see cref="Series"/>. Four styles, fixed or auto scale.</summary>
public sealed class Sparkline : FrameworkElement
{
    const int MaxBars = 60;

    public Series Series { get; set; } = new();
    public Brush StrokeA { get; set; } = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
    public Brush StrokeB { get; set; } = new SolidColorBrush(Color.FromRgb(0x8A, 0xE0, 0x7A));
    /// <summary>Not named Style: FrameworkElement already owns that name.</summary>
    public GraphStyle GraphStyle { get; set; } = HWWidget.GraphStyle.Area;
    public double VMax { get; set; } = 100;      // 0 => auto-scale
    public int WindowSamples { get; set; } = 60;

    static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    static readonly Pen GridPen = new Pen(GridBrush, 1);

    public void Push(double a, double b = double.NaN)
    {
        Series.Push(a, b);
        InvalidateVisual();
    }

    public void Clear()
    {
        Series.Clear();
        InvalidateVisual();
    }

    /// <summary>Da chiamare quando la serie è stata riempita direttamente.</summary>
    public void Refresh() => InvalidateVisual();

    public int Count => Series.Count;
    public double ProbeLast() => Series.Last;
    public double ProbeOldest(int visible) => Series.Oldest(visible);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 3 || h < 3) return;

        int vis = Math.Min(Math.Max(WindowSamples, 2), Series.Count);
        if (vis < 2)
        {
            if (GraphStyle != HWWidget.GraphStyle.Bars) dc.DrawLine(GridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));
            return;
        }

        double vmax = VMax > 0 ? VMax : AutoMax(vis);
        if (vmax <= 0) vmax = 1;
        int start = (Series.Head - vis + 1200 * 8) % 1200;

        // sfumatura verso i bordi del box: il grafico arriva ai lati e si spegne dolcemente
        dc.PushOpacityMask(EdgeFade(w));
        if (GraphStyle == HWWidget.GraphStyle.Bars)
        {
            DrawBars(dc, false, start, vis, w, h, vmax, StrokeA);
            if (Series.HasB) DrawBars(dc, true, start, vis, w, h, vmax, StrokeB);
            dc.Pop();
            return;
        }

        dc.DrawLine(GridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        double step = w / (vis - 1);
        DrawLine(dc, false, start, vis, step, h, vmax, StrokeA, GraphStyle == HWWidget.GraphStyle.Area);
        if (Series.HasB) DrawLine(dc, true, start, vis, step, h, vmax, StrokeB, false);
        dc.Pop();
    }

    /// <summary>Maschera orizzontale: pieno al centro, trasparente sui due bordi.</summary>
    static Brush EdgeFade(double w)
    {
        var g = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(Math.Max(1, w), 0),
        };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 0.07));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.20));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.80));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 0.93));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        g.Freeze();
        return g;
    }

    static double San(double v) => double.IsNaN(v) || v < 0 ? 0 : v;

    double AutoMax(int vis)
    {
        double m = 0;
        for (int i = 0; i < vis; i++)
        {
            int idx = (Series.Head - vis + i + 1200 * 8) % 1200;
            double v = Series.A(idx);
            if (!double.IsNaN(v) && v > m) m = v;
            if (Series.HasB)
            {
                double b = Series.B(idx);
                if (!double.IsNaN(b) && b > m) m = b;
            }
        }
        return m <= 0 ? 1 : m * 1.15;
    }

    void DrawLine(DrawingContext dc, bool second, int start, int vis, double step, double h,
                  double vmax, Brush stroke, bool fill)
    {
        var pts = new Point[vis];
        var pen = new Pen(stroke, 1.4) { LineJoin = PenLineJoin.Round };

        for (int i = 0; i < vis; i++)
        {
            double v = second ? Series.B((start + i) % 1200) : Series.A((start + i) % 1200);
            if (double.IsNaN(v)) v = 0;
            double y = h - Math.Min(1, v / vmax) * (h - 2) - 1;
            pts[i] = new Point(i * step, y);
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], fill, false);
            ctx.PolyLineTo(new ArraySegment<Point>(pts, 1, vis - 1), true, false);
            if (fill)
            {
                ctx.LineTo(new Point(pts[vis - 1].X, h), false, false);
                ctx.LineTo(new Point(pts[0].X, h), false, false);
            }
        }
        if (fill && stroke is SolidColorBrush sc)
            dc.DrawGeometry(FillGradient(sc.Color), null, geo);
        dc.DrawGeometry(null, pen, geo);
    }

    /// <summary>Pieno sotto la linea: dal colore pieno in alto al trasparente in basso.</summary>
    static Brush FillGradient(Color c)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x5E, c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, c.R, c.G, c.B), 0.75));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
        g.Freeze();
        return g;
    }

    void DrawBars(DrawingContext dc, bool second, int start, int vis, double w, double h,
                  double vmax, Brush stroke)
    {
        int bars = Math.Min(MaxBars, vis);
        double slot = w / bars;
        double barW = Math.Max(1.0, slot * 0.62);
        var brush = stroke is SolidColorBrush sc
            ? new SolidColorBrush(Color.FromArgb(0xCC, sc.Color.R, sc.Color.G, sc.Color.B))
            : stroke;
        for (int i = 0; i < bars; i++)
        {
            int idx = (start + (int)((double)i / bars * vis)) % 1200;
            double v = second ? Series.B(idx) : Series.A(idx);
            if (double.IsNaN(v)) v = 0;
            double bh = Math.Max(1, Math.Min(1, v / vmax) * (h - 2));
            double x = i * slot + (slot - barW) / 2;
            dc.DrawRectangle(brush, null, new Rect(x, h - bh, barW, bh));
        }
    }
}
