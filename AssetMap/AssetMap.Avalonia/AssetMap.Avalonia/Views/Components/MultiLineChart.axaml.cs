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
/// Multi-line chart s osami, mřížkou a hover tooltipem (všechny série najednou).
/// </summary>
public partial class MultiLineChart : UserControl
{
    // ── Bindable properties ───────────────────────────────────
    public static readonly StyledProperty<ChartLine[]> LinesProperty =
        AvaloniaProperty.Register<MultiLineChart, ChartLine[]>(nameof(Lines), []);

    public static readonly StyledProperty<string> YSuffixProperty =
        AvaloniaProperty.Register<MultiLineChart, string>(nameof(YSuffix), "");

    public ChartLine[] Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public string YSuffix
    {
        get => GetValue(YSuffixProperty);
        set => SetValue(YSuffixProperty, value);
    }

    // ── Layout ────────────────────────────────────────────────
    private const double PadL = 58, PadR = 14, PadT = 12, PadB = 28;

    // ── Hover stav ────────────────────────────────────────────
    private int? _hoverIdx;
    private double _vMin, _vRange;

    public MultiLineChart()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LinesProperty || change.Property == YSuffixProperty)
            InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var lines = Lines;
        if (lines is null || lines.Length == 0) return;

        int cnt = lines.Max(l => l.Values.Length);
        if (cnt < 2) return;

        double w     = Bounds.Width;
        double plotW = w - PadL - PadR;
        if (plotW <= 0) return;

        double clampedX = Math.Clamp(e.GetPosition(this).X, PadL, w - PadR);
        double t   = (clampedX - PadL) / plotW;
        int    idx = (int)Math.Clamp(Math.Round(t * (cnt - 1)), 0, cnt - 1);

        if (_hoverIdx != idx) { _hoverIdx = idx; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverIdx.HasValue) { _hoverIdx = null; InvalidateVisual(); }
    }

    // ── Rendering ─────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var lines = Lines;
        if (lines is null || lines.Length == 0) return;

        double plotW = w - PadL - PadR;
        double plotH = h - PadT - PadB;
        if (plotW <= 0 || plotH <= 0) return;

        // ── Globální min/max ──────────────────────────────────
        var all  = lines.SelectMany(l => l.Values).ToArray();
        double minV  = all.Min();
        double maxV  = all.Max();
        double range = maxV - minV;
        if (range < 1e-9) range = 1;

        double vPad = range * 0.08;
        _vMin  = minV - vPad;
        _vRange = maxV + vPad - _vMin;
        if (_vRange < 1e-9) _vRange = 1;

        int cnt = lines.Max(l => l.Values.Length);

        double MapX(int i, int count) => PadL + plotW * i / (count - 1);
        double MapY(double v) => PadT + plotH * (1.0 - (v - _vMin) / _vRange);

        var labelBrush = new SolidColorBrush(Color.Parse("#6A6A82"));
        var gridPen    = new Pen(new SolidColorBrush(Color.Parse("#1C1C28")), 1);
        var axisPen    = new Pen(new SolidColorBrush(Color.Parse("#2A2A38")), 1);
        var typeface   = Typeface.Default;
        const double fs = 10;
        string suffix  = YSuffix;

        // ── Mřížka + popisky Y ────────────────────────────────
        const int steps = 4;
        for (int g = 0; g <= steps; g++)
        {
            double t = (double)g / steps;
            double y = PadT + plotH * t;
            double v = _vMin + _vRange * (1 - t);

            ctx.DrawLine(g == 0 || g == steps ? axisPen : gridPen,
                         new Point(PadL, y), new Point(w - PadR, y));

            string label = FormatValue(v) + suffix;
            var ft = new FormattedText(
                label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, fs, labelBrush);
            ctx.DrawText(ft, new Point(PadL - ft.Width - 5, y - ft.Height / 2));
        }

        // ── Popisky X ─────────────────────────────────────────
        if (cnt > 1)
        {
            var baseDate = DateTime.Now.Date.AddDays(-(cnt - 1));
            const int xlbl = 5;
            for (int i = 0; i < xlbl; i++)
            {
                int    idx = i == xlbl - 1 ? cnt - 1 : (cnt - 1) * i / (xlbl - 1);
                double x   = MapX(idx, cnt);
                var    ft  = new FormattedText(
                    baseDate.AddDays(idx).ToString("d. M."),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, fs, labelBrush);
                ctx.DrawText(ft, new Point(x - ft.Width / 2, h - PadB + 5));
            }
        }

        // ── Čáry ──────────────────────────────────────────────
        foreach (var line in lines)
        {
            int lc = line.Values.Length;
            if (lc < 2) continue;

            var pen = new Pen(new SolidColorBrush(line.LineColor), 2,
                              lineCap: PenLineCap.Round);
            var geo = new StreamGeometry();
            using (var sgc = geo.Open())
            {
                sgc.BeginFigure(new Point(MapX(0, lc), MapY(line.Values[0])), false);
                for (int i = 1; i < lc; i++)
                    sgc.LineTo(new Point(MapX(i, lc), MapY(line.Values[i])));
            }
            ctx.DrawGeometry(null, pen, geo);
        }

        // ── Hover ─────────────────────────────────────────────
        if (_hoverIdx.HasValue && _hoverIdx.Value < cnt)
        {
            int hi = _hoverIdx.Value;
            double hx = MapX(hi, cnt);

            var hoverPen = new Pen(new SolidColorBrush(Color.Parse("#6A6A82")), 1,
                               dashStyle: new DashStyle(new double[] { 4, 4 }, 0));
            ctx.DrawLine(hoverPen, new Point(hx, PadT), new Point(hx, PadT + plotH));

            // Tečky na čárách
            foreach (var line in lines)
            {
                if (hi >= line.Values.Length) continue;
                double hy = MapY(line.Values[hi]);
                ctx.DrawEllipse(new SolidColorBrush(line.LineColor),
                                new Pen(new SolidColorBrush(Color.Parse("#141419")), 2),
                                new Point(hx, hy), 5, 5);
            }

            // Tooltip (multi-series)
            var baseDate = DateTime.Now.Date.AddDays(-(cnt - 1));
            var date = baseDate.AddDays(hi);

            // Výška tooltipu = datum + každá série
            int tipLines    = 1 + lines.Length;
            double tipLineH = 17;
            double tipW     = 160;
            double tipH     = tipLines * tipLineH + 10;
            double tipX     = hx + 12;
            if (tipX + tipW > w - PadR) tipX = hx - tipW - 12;
            double tipY     = Math.Min(PadT + 4, PadT + plotH - tipH);

            ctx.DrawRectangle(
                new SolidColorBrush(Color.Parse("#1A1A22")),
                new Pen(new SolidColorBrush(Color.Parse("#2A2A38")), 1),
                new Rect(tipX, tipY, tipW, tipH), 4, 4);

            // Datum
            var dateFt = new FormattedText(
                date.ToString("d. M. yyyy"),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 10.5, new SolidColorBrush(Color.Parse("#EAEAF2")));
            ctx.DrawText(dateFt, new Point(tipX + 8, tipY + 5));

            // Každá série
            for (int li = 0; li < lines.Length; li++)
            {
                var line = lines[li];
                if (hi >= line.Values.Length) continue;

                double rowY = tipY + 5 + (li + 1) * tipLineH;

                // Barevná tečka
                ctx.DrawEllipse(new SolidColorBrush(line.LineColor), null,
                                new Point(tipX + 12, rowY + 6), 4, 4);

                // Hodnota
                string val = FormatValue(line.Values[hi]) + suffix;
                var ft = new FormattedText(
                    $"{line.Label}  {val}",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 10.5, new SolidColorBrush(Color.Parse("#AAAABF")));
                ctx.DrawText(ft, new Point(tipX + 22, rowY));
            }
        }
    }

    private static string FormatValue(double v)
    {
        if (Math.Abs(v) >= 1_000_000) return (v / 1_000_000).ToString("0.#") + "M";
        if (Math.Abs(v) >= 10_000)    return (v / 1_000).ToString("0.#") + "k";
        if (Math.Abs(v) >= 1_000)     return v.ToString("N0");
        return v.ToString("0.#");
    }
}
