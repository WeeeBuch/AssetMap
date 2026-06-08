using Avalonia.Media;

namespace AssetMap.Avalonia.ViewModels;

/// <summary>Časové okno zobrazované v grafech.</summary>
public enum ChartPeriod { D1, D7, M1, M3, Y1, All }

/// <summary>Typ nové transakce v editačním panelu.</summary>
public enum NewTxMode { In, Out, Transfer }

/// <summary>Jeden výseč koláčového grafu.</summary>
public class PieSliceData
{
    public string Label      { get; init; } = "";
    public double Value      { get; init; }
    public string Percent    { get; init; } = "";
    public IBrush SliceBrush { get; init; } = Brushes.Transparent;

    // Pro kreslení na Canvas (PieChart.axaml.cs)
    public Color SliceColor =>
        (SliceBrush as SolidColorBrush)?.Color ?? Colors.Gray;
}

/// <summary>Jedna čára v multi-line grafu.</summary>
public class ChartLine
{
    public string   Label     { get; init; } = "";
    public double[] Values    { get; init; } = [];
    public IBrush   LineBrush { get; init; } = Brushes.Gray;

    // Pro kreslení na Canvas (MultiLineChart.axaml.cs)
    public Color LineColor =>
        (LineBrush as SolidColorBrush)?.Color ?? Colors.Gray;
}
