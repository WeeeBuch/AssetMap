using Avalonia;
using Avalonia.Controls;

namespace AssetMap.Avalonia.Views.Components;

public partial class StatBadge : UserControl
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<StatBadge, string>(nameof(Value), "0%");

    public static readonly StyledProperty<bool> IsPositiveProperty =
        AvaloniaProperty.Register<StatBadge, bool>(nameof(IsPositive), false);

    public static readonly StyledProperty<bool> IsNegativeProperty =
        AvaloniaProperty.Register<StatBadge, bool>(nameof(IsNegative), false);

    // IsNeutral = ani pozitivní ani negativní
    public string Value      { get => GetValue(ValueProperty);      set => SetValue(ValueProperty, value); }
    public bool   IsPositive { get => GetValue(IsPositiveProperty); set => SetValue(IsPositiveProperty, value); }
    public bool   IsNegative { get => GetValue(IsNegativeProperty); set => SetValue(IsNegativeProperty, value); }

    public StatBadge()
    {
        InitializeComponent();
        DataContext = this;
    }
}
