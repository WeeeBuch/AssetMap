using System;
using System.Globalization;
using System.Linq;
using AssetMap.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AssetMap.Avalonia.Views.Components;

/// <summary>
/// Donut pie chart s hover zvýrazněním a popisky.
/// </summary>
public partial class PieChart : UserControl
{
    public static readonly StyledProperty<PieSliceData[]> SlicesProperty =
        AvaloniaProperty.Register<PieChart, PieSliceData[]>(nameof(Slices), []);

    public PieSliceData[] Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    private int? _hoverSlice;

    // Pre-compute per-render (needed in OnPointerMoved)
    private double _cx, _cy, _outerR, _innerR;
    private double[] _startAngles = [];

    public PieChart()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SlicesProperty) InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var slices = Slices;
        if (slices is null || slices.Length == 0 || _startAngles.Length == 0) return;

        var pos  = e.GetPosition(this);
        double dx = pos.X - _cx;
        double dy = pos.Y - _cy;
        double r  = Math.Sqrt(dx * dx + dy * dy);

        // Mimo donut
        if (r < _innerR || r > _outerR + 6)
        {
            if (_hoverSlice.HasValue) { _hoverSlice = null; InvalidateVisual(); }
            return;
        }

        double angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
        if (angle < 0)   angle += 360;
        if (angle >= 360) angle -= 360;

        double total = slices.Sum(s => s.Value);
        double cursor = 0;
        for (int i = 0; i < slices.Length; i++)
        {
            double sweep = slices[i].Value / total * 360;
            cursor += sweep;
            if (angle < cursor)
            {
                if (_hoverSlice != i) { _hoverSlice = i; InvalidateVisual(); }
                return;
            }
        }
        if (_hoverSlice.HasValue) { _hoverSlice = null; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverSlice.HasValue) { _hoverSlice = null; InvalidateVisual(); }
    }

    // ── Rendering ─────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var slices = Slices;
        if (slices is null || slices.Length == 0) return;

        double total = slices.Sum(s => s.Value);
        if (total <= 0) return;

        _cx     = w / 2;
        _cy     = h / 2;
        _outerR = Math.Min(w, h) / 2 - 6;
        _innerR = _outerR * 0.52;

        _startAngles = new double[slices.Length];
        var sepPen = new Pen(new SolidColorBrush(Color.Parse("#141419")), 2);

        double angle   = 0;
        double hoverOff = 8; // offset hvru výseče

        for (int i = 0; i < slices.Length; i++)
        {
            _startAngles[i] = angle;
            double sweep = slices[i].Value / total * 360;
            if (sweep < 0.1) { angle += sweep; continue; }

            bool   isHover = _hoverSlice == i;
            double outerR  = isHover ? _outerR + hoverOff : _outerR;
            double innerR  = isHover ? _innerR - 2 : _innerR;

            var geo = DonutSlice(new Point(_cx, _cy), innerR, outerR, angle, sweep);
            ctx.DrawGeometry(new SolidColorBrush(slices[i].SliceColor), sepPen, geo);

            // Popisek % ve středu výseče
            double midAngle  = (angle + sweep / 2 - 90) * Math.PI / 180;
            double labelR    = (_innerR + _outerR) / 2;
            double lx        = _cx + labelR * Math.Cos(midAngle);
            double ly        = _cy + labelR * Math.Sin(midAngle);

            if (sweep > 14) // jen pro slušně velké výseče
            {
                var ft = new FormattedText(
                    (slices[i].Value / total * 100).ToString("N0") + "%",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, isHover ? 12 : 10.5,
                    new SolidColorBrush(Colors.White));
                ctx.DrawText(ft, new Point(lx - ft.Width / 2, ly - ft.Height / 2));
            }

            angle += sweep;
        }

        // ── Hover tooltip uprostřed donutu ────────────────────
        if (_hoverSlice.HasValue)
        {
            var s    = slices[_hoverSlice.Value];
            double pct = s.Value / total * 100;

            var nameFt = new FormattedText(
                s.Label,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 11, new SolidColorBrush(Color.Parse("#EAEAF2")));
            var pctFt = new FormattedText(
                pct.ToString("N1") + " %",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, style: FontStyle.Normal,
                             weight: FontWeight.Bold),
                14, new SolidColorBrush(slices[_hoverSlice.Value].SliceColor));

            double cx2 = _cx;
            double cy2 = _cy - (nameFt.Height + pctFt.Height) / 2;

            ctx.DrawText(nameFt, new Point(cx2 - nameFt.Width / 2, cy2));
            ctx.DrawText(pctFt,  new Point(cx2 - pctFt.Width / 2,  cy2 + nameFt.Height + 2));
        }
    }

    // ── Geometrie výseče (donut) ──────────────────────────────
    private static Geometry DonutSlice(Point center, double innerR, double outerR,
                                       double startDeg, double sweepDeg)
    {
        if (sweepDeg >= 360) sweepDeg = 359.9999;

        double s  = ToRad(startDeg - 90);
        double e  = ToRad(startDeg + sweepDeg - 90);
        bool   lg = sweepDeg > 180;

        Point os  = Polar(center, outerR, s);
        Point oe  = Polar(center, outerR, e);
        Point ie  = Polar(center, innerR, e);
        Point is_ = Polar(center, innerR, s);

        var geo = new StreamGeometry();
        using (var sgc = geo.Open())
        {
            sgc.BeginFigure(os, true);
            sgc.ArcTo(oe, new Size(outerR, outerR), 0, lg, SweepDirection.Clockwise);
            sgc.LineTo(ie);
            sgc.ArcTo(is_, new Size(innerR, innerR), 0, lg, SweepDirection.CounterClockwise);
            sgc.EndFigure(true);
        }
        return geo;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
    private static Point  Polar(Point c, double r, double rad)
        => new(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
}
