using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AssetMap.Avalonia.Views.Components;

public partial class TransactionRow : UserControl
{
    public static readonly StyledProperty<string>  IconTextProperty        = AvaloniaProperty.Register<TransactionRow, string>(nameof(IconText), "?");
    public static readonly StyledProperty<IBrush>  IconBackgroundProperty  = AvaloniaProperty.Register<TransactionRow, IBrush>(nameof(IconBackground), Brushes.Gray);
    public static readonly StyledProperty<IBrush>  IconForegroundProperty  = AvaloniaProperty.Register<TransactionRow, IBrush>(nameof(IconForeground), Brushes.White);
    public static readonly StyledProperty<string>  DescriptionProperty     = AvaloniaProperty.Register<TransactionRow, string>(nameof(Description), "Transakce");
    public static readonly StyledProperty<string>  DateProperty            = AvaloniaProperty.Register<TransactionRow, string>(nameof(Date), "");
    public static readonly StyledProperty<string>  AmountProperty          = AvaloniaProperty.Register<TransactionRow, string>(nameof(Amount), "");
    public static readonly StyledProperty<bool>    IsPositiveProperty      = AvaloniaProperty.Register<TransactionRow, bool>(nameof(IsPositive), false);
    public static readonly StyledProperty<bool>    IsNeutralProperty       = AvaloniaProperty.Register<TransactionRow, bool>(nameof(IsNeutral), false);
    public static readonly StyledProperty<bool>    IsNegativeProperty      = AvaloniaProperty.Register<TransactionRow, bool>(nameof(IsNegative), false);

    public string IconText       { get => GetValue(IconTextProperty);       set => SetValue(IconTextProperty, value); }
    public IBrush IconBackground { get => GetValue(IconBackgroundProperty); set => SetValue(IconBackgroundProperty, value); }
    public IBrush IconForeground { get => GetValue(IconForegroundProperty); set => SetValue(IconForegroundProperty, value); }
    public string Description    { get => GetValue(DescriptionProperty);    set => SetValue(DescriptionProperty, value); }
    public string Date           { get => GetValue(DateProperty);           set => SetValue(DateProperty, value); }
    public string Amount         { get => GetValue(AmountProperty);         set => SetValue(AmountProperty, value); }
    public bool   IsPositive     { get => GetValue(IsPositiveProperty);     set => SetValue(IsPositiveProperty, value); }
    public bool   IsNeutral      { get => GetValue(IsNeutralProperty);      set => SetValue(IsNeutralProperty, value); }
    public bool   IsNegative     { get => GetValue(IsNegativeProperty);     set => SetValue(IsNegativeProperty, value); }

    public TransactionRow()
    {
        InitializeComponent();
        DataContext = this;
    }
}
