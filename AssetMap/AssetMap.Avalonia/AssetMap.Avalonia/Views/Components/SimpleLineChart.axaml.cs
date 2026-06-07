using System;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace AssetMap.Avalonia.Views.Components;

public partial class SimpleLineChart : UserControl
{
    // ── Avalonia properties (bindable) ────────────────────────
    public static readonly StyledProperty<double[]> BalanceHistoryProperty =
        AvaloniaProperty.Register<SimpleLineChart, double[]>(nameof(BalanceHistory), []);

    public static readonly StyledProperty<int[]> DepositIndicesProperty =
        AvaloniaProperty.Register<SimpleLineChart, int[]>(nameof(DepositIndices), []);

    public static readonly StyledProperty<int[]> WithdrawalIndicesProperty =
        AvaloniaProperty.Register<SimpleLineChart, int[]>(nameof(WithdrawalIndices), []);

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

    public SimpleLineChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => DrawChart();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BalanceHistoryProperty    ||
            change.Property == DepositIndicesProperty    ||
            change.Property == WithdrawalIndicesProperty)
        {
            DrawChart();
        }
    }

    // ── Kreslení grafu ────────────────────────────────────────
    private void DrawChart()
    {
        double w = ChartCanvas.Bounds.Width;
        double h = ChartCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var pts = BalanceHistory;
        if (pts == null || pts.Length < 2) return;

        ChartCanvas.Children.Clear();

        const double padX = 8;
        const double padY = 12;

        double minV  = pts.Min();
        double maxV  = pts.Max();
        double range = maxV - minV;
        if (range < 1e-9) range = 1;

        int n = pts.Length;

        Point Map(int i, double v)
        {
            double x = padX + (w - 2 * padX) * i / (n - 1);
            double y = h - padY - (h - 2 * padY) * (v - minV) / range;
            return new Point(x, y);
        }

        var mapped = Enumerable.Range(0, n).Select(i => Map(i, pts[i])).ToList();

        // ── Gradient fill ─────────────────────────────────────
        var fillPts = new AvaloniaList<Point>(mapped);
        fillPts.Add(new Point(mapped[^1].X, h));
        fillPts.Add(new Point(mapped[0].X, h));

        ChartCanvas.Children.Add(new Polygon
        {
            Points = fillPts,
            Fill   = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint   = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop { Color = Color.Parse("#4400B3FF"), Offset = 0 },
                    new GradientStop { Color = Color.Parse("#0000B3FF"), Offset = 1 },
                }
            }
        });

        // ── Čára ─────────────────────────────────────────────
        ChartCanvas.Children.Add(new Polyline
        {
            Points          = new AvaloniaList<Point>(mapped),
            Stroke          = new SolidColorBrush(Color.Parse("#00B3FF")),
            StrokeThickness = 2,
            StrokeLineCap   = PenLineCap.Round,
        });

        // ── Tečky transakcí ───────────────────────────────────
        AddDots(pts, n, DepositIndices,    Color.Parse("#00E57A"), Map);
        AddDots(pts, n, WithdrawalIndices, Color.Parse("#FF3355"), Map);
    }

    private void AddDots(double[] pts, int n, int[] indices, Color fill,
                         Func<int, double, Point> map)
    {
        if (indices is null) return;
        foreach (int idx in indices)
        {
            if (idx < 0 || idx >= n) continue;
            var p   = map(idx, pts[idx]);
            var dot = new Ellipse
            {
                Width  = 8,
                Height = 8,
                Fill   = new SolidColorBrush(fill),
            };
            Canvas.SetLeft(dot, p.X - 4);
            Canvas.SetTop(dot,  p.Y - 4);
            ChartCanvas.Children.Add(dot);
        }
    }
}
