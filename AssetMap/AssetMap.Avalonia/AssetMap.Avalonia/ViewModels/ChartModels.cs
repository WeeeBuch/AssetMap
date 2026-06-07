using Avalonia.Media;

namespace AssetMap.Avalonia.ViewModels;

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
