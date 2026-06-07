using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AssetMap.Avalonia.Views.Components;

public partial class AccountRow : UserControl
{
    public static readonly StyledProperty<string>  IconTextProperty        = AvaloniaProperty.Register<AccountRow, string>(nameof(IconText), "?");
    public static readonly StyledProperty<IBrush>  IconBackgroundProperty  = AvaloniaProperty.Register<AccountRow, IBrush>(nameof(IconBackground), Brushes.Gray);
    public static readonly StyledProperty<IBrush>  IconForegroundProperty  = AvaloniaProperty.Register<AccountRow, IBrush>(nameof(IconForeground), Brushes.White);
    public static readonly StyledProperty<string>  AccountNameProperty     = AvaloniaProperty.Register<AccountRow, string>(nameof(AccountName), "Účet");
    public static readonly StyledProperty<string>  SubtitleProperty        = AvaloniaProperty.Register<AccountRow, string>(nameof(Subtitle), "");
    public static readonly StyledProperty<string>  ValueProperty           = AvaloniaProperty.Register<AccountRow, string>(nameof(Value), "$0");
    public static readonly StyledProperty<string>  SubValueProperty        = AvaloniaProperty.Register<AccountRow, string>(nameof(SubValue), "");
    public static readonly StyledProperty<bool>    ShowDividerProperty     = AvaloniaProperty.Register<AccountRow, bool>(nameof(ShowDivider), true);

    public string IconText       { get => GetValue(IconTextProperty);       set => SetValue(IconTextProperty, value); }
    public IBrush IconBackground { get => GetValue(IconBackgroundProperty); set => SetValue(IconBackgroundProperty, value); }
    public IBrush IconForeground { get => GetValue(IconForegroundProperty); set => SetValue(IconForegroundProperty, value); }
    public string AccountName    { get => GetValue(AccountNameProperty);    set => SetValue(AccountNameProperty, value); }
    public string Subtitle       { get => GetValue(SubtitleProperty);       set => SetValue(SubtitleProperty, value); }
    public string Value          { get => GetValue(ValueProperty);          set => SetValue(ValueProperty, value); }
    public string SubValue       { get => GetValue(SubValueProperty);       set => SetValue(SubValueProperty, value); }
    public bool   ShowDivider    { get => GetValue(ShowDividerProperty);    set => SetValue(ShowDividerProperty, value); }

    public AccountRow()
    {
        InitializeComponent();
        DataContext = this;
    }
}
