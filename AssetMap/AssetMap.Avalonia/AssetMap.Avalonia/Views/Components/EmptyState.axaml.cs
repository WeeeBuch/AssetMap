using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace AssetMap.Avalonia.Views.Components;

public partial class EmptyState : UserControl
{
    public static readonly StyledProperty<string>    IconProperty          = AvaloniaProperty.Register<EmptyState, string>(nameof(Icon), "○");
    public static readonly StyledProperty<string>    TitleProperty         = AvaloniaProperty.Register<EmptyState, string>(nameof(Title), "Nic tu není");
    public static readonly StyledProperty<string?>   DescriptionProperty   = AvaloniaProperty.Register<EmptyState, string?>(nameof(Description));
    public static readonly StyledProperty<string?>   ActionTextProperty    = AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(ActionCommand));

    public string    Icon          { get => GetValue(IconProperty);          set => SetValue(IconProperty, value); }
    public string    Title         { get => GetValue(TitleProperty);         set => SetValue(TitleProperty, value); }
    public string?   Description   { get => GetValue(DescriptionProperty);   set => SetValue(DescriptionProperty, value); }
    public string?   ActionText    { get => GetValue(ActionTextProperty);    set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public EmptyState()
    {
        InitializeComponent();
        DataContext = this;
    }
}
