using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AssetMap.Avalonia.Views.Components;

public partial class AssetCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<AssetCard, string>(nameof(Title), "Asset");

    public static readonly StyledProperty<string> TotalValueProperty =
        AvaloniaProperty.Register<AssetCard, string>(nameof(TotalValue), "$0,00");

    public static readonly StyledProperty<string> QuantityProperty =
        AvaloniaProperty.Register<AssetCard, string>(nameof(Quantity), "0");

    public static readonly StyledProperty<string> ChangeProperty =
        AvaloniaProperty.Register<AssetCard, string>(nameof(Change), "0%");

    public static readonly StyledProperty<bool> IsPositiveProperty =
        AvaloniaProperty.Register<AssetCard, bool>(nameof(IsPositive), true);

    public static readonly StyledProperty<IBrush> DotBrushProperty =
        AvaloniaProperty.Register<AssetCard, IBrush>(nameof(DotBrush), Brushes.Gray);

    public string Title       { get => GetValue(TitleProperty);      set => SetValue(TitleProperty, value); }
    public string TotalValue  { get => GetValue(TotalValueProperty); set => SetValue(TotalValueProperty, value); }
    public string Quantity    { get => GetValue(QuantityProperty);   set => SetValue(QuantityProperty, value); }
    public string Change      { get => GetValue(ChangeProperty);     set => SetValue(ChangeProperty, value); }
    public bool   IsPositive  { get => GetValue(IsPositiveProperty); set => SetValue(IsPositiveProperty, value); }
    public IBrush DotBrush    { get => GetValue(DotBrushProperty);   set => SetValue(DotBrushProperty, value); }

    public AssetCard()
    {
        InitializeComponent();
        DataContext = this;
    }
}
