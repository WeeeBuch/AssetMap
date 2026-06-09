using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AssetMap.Avalonia.Views.Components;

/// <summary>
/// Line chart s osami, mřížkou a hover tooltipem.
/// Čistý Avalonia Render — žádné závislosti, živé aktualizace.
/// </summary>
public partial class SimpleLineChart : UserControl
{
    // ── Bindable properties ───────────────────────────────────
    public static readonly StyledProperty<double[]> BalanceHistoryProperty =
        AvaloniaProperty.Register<SimpleLineChart, double[]>(nameof(BalanceHistory), []);

    public static readonly StyledProperty<int[]> DepositIndicesProperty =
        AvaloniaProperty.Register<SimpleLineChart, int[]>(nameof(DepositIndices), []);

    public static readonly StyledProperty<int[]> WithdrawalIndicesProperty =
        AvaloniaProperty.Register<SimpleLineChart, int[]>(nameof(WithdrawalIndices), []);

    public static readonly StyledProperty<bool> IsSparklineProperty =
        AvaloniaProperty.Register<SimpleLineChart, bool>(nameof(IsSparkline), false);

    public static readonly StyledProperty<double[]> PriceLineProperty =
        AvaloniaProperty.Register<SimpleLineChart, double[]>(nameof(PriceLine), []);

    public static readonly StyledProperty<string> PriceLabelProperty =
        AvaloniaProperty.Register<SimpleLineChart, string>(nameof(PriceLabel), "");

    public double[] BalanceHistory
    {
        get => GetValue(BalanceHistoryProperty);
        set => SetValue(BalanceHistoryProperty, value);
    }

    public int[] DepositIndices
    {
        get => GetValue(DepositIndicesProperty);
        set => SetValue(DepositIndicesProperty, value);
    }

    public int[] WithdrawalIndices
    {
        get => GetValue(WithdrawalIndicesProperty);
        set => SetValue(WithdrawalIndicesProperty, value);
    }

    public bool IsSparkline
    {
        get => GetValue(IsSparklineProperty);
        set => SetValue(IsSparklineProperty, value);
    }

    /// <summary>Druhá datová řada (cena aktiva) — normalizovaná na stejné Y jako BalanceHistory.</summary>
    public double[] PriceLine
    {
        get => GetValue(PriceLineProperty);
        set => SetValue(PriceLineProperty, value);
    }

    /// <summary>Popisek druhé čáry pro legendu (např. "BTC cena").</summary>
    public string PriceLabel
    {
        get => GetValue(PriceLabelProperty);
        set => SetValue(PriceLabelProperty, value);
    }

    // ── Layout ────────────────────────────────────────────────
    // Dynamické paddingsy — fullmode vs sparkline
    private double PadL => IsSparkline ?  2 : 62;
    private double PadR => IsSparkline ?  2 : 14;
    private double PadT => IsSparkline ?  4 : 12;
    private double PadB => IsSparkline ?  4 : 28;

    // ── Hover stav ────────────────────────────────────────────
    private int? _hoverIdx;

    // Cached pro hover výpočty
    private double _vMin, _vRange;

    public SimpleLineChart()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BalanceHistoryProperty    ||
            change.Property == DepositIndicesProperty    ||
            change.Property == WithdrawalIndicesProperty ||
            change.Property == PriceLineProperty         ||
            change.Property == PriceLabelProperty)
            InvalidateVisual();
    }

    // ── Vstupní události ──────────────────────────────────────
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pts = BalanceHistory;
        if (pts is null || pts.Length < 2) return;

        double w     = Bounds.Width;
        double h     = Bounds.Height;
        double plotW = w - PadL - PadR;
        double plotH = h - PadT - PadB;
        if (plotW <= 0 || plotH <= 0) return;

        var pos = e.GetPosition(this);

        // Hover aktivní nad celou plochou grafu (ne jen nad čárou)
        // Clampujeme X do plotové oblasti → tooltip vždy viditelný
        double clampedX = Math.Clamp(pos.X, PadL, w - PadR);
        double t   = (clampedX - PadL) / plotW;
        int    idx = (int)Math.Clamp(Math.Round(t * (pts.Length - 1)), 0, pts.Length - 1);

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

        var pts = BalanceHistory;
        if (pts is null || pts.Length < 2) return;

        double plotW = w - PadL - PadR;
        double plotH = h - PadT - PadB;
        if (plotW <= 0 || plotH <= 0) return;

        // ── Hodnoty a mapování ────────────────────────────────
        double minV  = pts.Min();
        double maxV  = pts.Max();
        double range = maxV - minV;
        if (range < 1e-9) range = 1;

        double vPad   = range * 0.08;
        _vMin         = minV - vPad;
        _vRange       = maxV + vPad - _vMin;
        if (_vRange < 1e-9) _vRange = 1;

        int n = pts.Length;

        double MapX(int i) => PadL + plotW * i / (n - 1);
        double MapY(double v) => PadT + plotH * (1.0 - (v - _vMin) / _vRange);

        // ── Barvy ─────────────────────────────────────────────
        var linePen  = new Pen(new SolidColorBrush(Color.Parse("#00B3FF")), 2,
                          lineCap: PenLineCap.Round);
        var typeface = Typeface.Default;
        const double fs = 10;

        // ── Mřížka + popisky Y (jen v plném módu) ────────────
        if (!IsSparkline)
        {
            var labelBrush = new SolidColorBrush(Color.Parse("#6A6A82"));
            var gridPen    = new Pen(new SolidColorBrush(Color.Parse("#1C1C28")), 1);
            var axisPen    = new Pen(new SolidColorBrush(Color.Parse("#2A2A38")), 1);
            const int steps = 4;
            for (int g = 0; g <= steps; g++)
            {
                double t = (double)g / steps;
                double y = PadT + plotH * t;
                double v = _vMin + _vRange * (1 - t);

                ctx.DrawLine(g == 0 || g == steps ? axisPen : gridPen,
                             new Point(PadL, y), new Point(w - PadR, y));

                var ft = new FormattedText(
                    FormatValue(v), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, fs, labelBrush);
                ctx.DrawText(ft, new Point(PadL - ft.Width - 5, y - ft.Height / 2));
            }

            // Popisky X (5 datumů)
            var baseDate2  = DateTime.Now.Date.AddDays(-(n - 1));
            const int xlbl = 5;
            for (int i = 0; i < xlbl; i++)
            {
                int    idx  = i == xlbl - 1 ? n - 1 : (n - 1) * i / (xlbl - 1);
                double x    = MapX(idx);
                var    ft   = new FormattedText(
                    baseDate2.AddDays(idx).ToString("d. M."),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, fs, labelBrush);
                ctx.DrawText(ft, new Point(x - ft.Width / 2, h - PadB + 5));
            }
        }

        // ── Gradient fill pod čárou ───────────────────────────
        var fillGeo = new StreamGeometry();
        using (var sgc = fillGeo.Open())
        {
            sgc.BeginFigure(new Point(MapX(0), MapY(pts[0])), true);
            for (int i = 1; i < n; i++)
                sgc.LineTo(new Point(MapX(i), MapY(pts[i])));
            sgc.LineTo(new Point(MapX(n - 1), PadT + plotH));
            sgc.LineTo(new Point(MapX(0),     PadT + plotH));
            sgc.EndFigure(true);
        }

        var fillBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop { Color = Color.Parse("#3300B3FF"), Offset = 0 },
                new GradientStop { Color = Color.Parse("#0000B3FF"), Offset = 1 },
            }
        };
        ctx.DrawGeometry(fillBrush, null, fillGeo);

        // ── Čára ──────────────────────────────────────────────
        var lineGeo = new StreamGeometry();
        using (var sgc = lineGeo.Open())
        {
            sgc.BeginFigure(new Point(MapX(0), MapY(pts[0])), false);
            for (int i = 1; i < n; i++)
                sgc.LineTo(new Point(MapX(i), MapY(pts[i])));
        }
        ctx.DrawGeometry(null, linePen, lineGeo);

        // ── Tečky transakcí ───────────────────────────────────
        DrawDots(ctx, pts, n, DepositIndices,    Color.Parse("#00E57A"), MapX, MapY);
        DrawDots(ctx, pts, n, WithdrawalIndices, Color.Parse("#FF3355"), MapX, MapY);

        // ── Druhá čára (cena aktiva) ─────────────────────────
        var pricePts = PriceLine;
        if (!IsSparkline && pricePts is { Length: > 1 })
        {
            // Normalizuj: škáluj cenovou čáru tak, aby začínala na stejné hodnotě jako balance
            // priceNorm[i] = pts[0] * pricePts[i] / pricePts[0]
            double priceScale = pricePts[0] > 1e-9 ? pts[0] / pricePts[0] : 1.0;
            int    pn         = Math.Min(pricePts.Length, n);

            var priceLinePen = new Pen(new SolidColorBrush(Color.Parse("#F7931A")), 1.5,
                                   lineCap: PenLineCap.Round,
                                   dashStyle: new DashStyle(new double[] { 5, 3 }, 0));

            var priceGeo = new StreamGeometry();
            using (var sgc = priceGeo.Open())
            {
                // Map price pts to same X scale as balance pts
                double priceMapX(int i) => PadL + plotW * i / (pn - 1);
                sgc.BeginFigure(new Point(priceMapX(0), MapY(pricePts[0] * priceScale)), false);
                for (int i = 1; i < pn; i++)
                    sgc.LineTo(new Point(priceMapX(i), MapY(pricePts[i] * priceScale)));
            }
            ctx.DrawGeometry(null, priceLinePen, priceGeo);

            // Legenda (pravý dolní roh plot area)
            if (!string.IsNullOrEmpty(PriceLabel))
            {
                var legendBrush = new SolidColorBrush(Color.Parse("#F7931A"));
                var legFt = new FormattedText(
                    "── " + PriceLabel,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 9.5, legendBrush);
                ctx.DrawText(legFt, new Point(w - PadR - legFt.Width, PadT + 2));

                var balFt = new FormattedText(
                    "── Zůstatek",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 9.5, new SolidColorBrush(Color.Parse("#00B3FF")));
                ctx.DrawText(balFt, new Point(w - PadR - balFt.Width, PadT + 14));
            }
        }

        // ── Hover (jen v plném módu) ──────────────────────────
        if (!IsSparkline && _hoverIdx.HasValue && _hoverIdx.Value < n)
        {
            int    hi  = _hoverIdx.Value;
            double hx  = MapX(hi);
            double hy  = MapY(pts[hi]);

            // Svislá přerušovaná čára
            var hoverPen = new Pen(new SolidColorBrush(Color.Parse("#6A6A82")), 1,
                               dashStyle: new DashStyle(new double[] { 4, 4 }, 0));
            ctx.DrawLine(hoverPen, new Point(hx, PadT), new Point(hx, PadT + plotH));

            // Tečka na čáře
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#00B3FF")),
                            new Pen(new SolidColorBrush(Color.Parse("#141419")), 2),
                            new Point(hx, hy), 5, 5);

            // Tooltip
            var baseDate = DateTime.Now.Date.AddDays(-(n - 1));
            var date = baseDate.AddDays(hi);
            string tip = $"{date:d. M. yyyy}   {FormatValue(pts[hi])}";
            var tipFt  = new FormattedText(
                tip, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 11, new SolidColorBrush(Color.Parse("#EAEAF2")));

            double tipW = tipFt.Width + 16;
            double tipH = tipFt.Height + 10;
            double tipX = hx + 10;
            if (tipX + tipW > w - PadR) tipX = hx - tipW - 10;
            double tipY = PadT + 4;

            var tipRect = new Rect(tipX, tipY, tipW, tipH);
            ctx.DrawRectangle(
                new SolidColorBrush(Color.Parse("#1A1A22")),
                new Pen(new SolidColorBrush(Color.Parse("#2A2A38")), 1),
                tipRect, 4, 4);
            ctx.DrawText(tipFt, new Point(tipX + 8, tipY + 5));
        }
    }

    // ── Helpers ───────────────────────────────────────────────
    private static void DrawDots(DrawingContext ctx,
        double[] pts, int n, int[] indices, Color fill,
        Func<int, double> mapX, Func<double, double> mapY)
    {
        if (indices is null) return;
        var fillBrush = new SolidColorBrush(fill);
        foreach (int idx in indices)
        {
            if (idx < 0 || idx >= n) continue;
            ctx.DrawEllipse(fillBrush, null,
                            new Point(mapX(idx), mapY(pts[idx])), 5, 5);
        }
    }

    private static string FormatValue(double v)
    {
        double abs = Math.Abs(v);
        if (abs >= 1_000_000) return (v / 1_000_000).ToString("0.#") + "M";
        if (abs >= 10_000)    return (v / 1_000).ToString("0.#") + "k";
        if (abs >= 1_000)     return v.ToString("N0");
        if (abs >= 1)         return v.ToString("N2");
        if (abs >= 0.001)     return v.ToString("0.####", CultureInfo.InvariantCulture);
        if (abs > 0)          return v.ToString("G4",    CultureInfo.InvariantCulture);
        return "0";
    }
}
