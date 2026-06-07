using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace AssetMap.Avalonia.Views.Components;

public partial class SectionHeader : UserControl
{
    public static readonly StyledProperty<string>   TitleProperty      = AvaloniaProperty.Register<SectionHeader, string>(nameof(Title), "");
    public static readonly StyledProperty<string?>  SubtitleProperty   = AvaloniaProperty.Register<SectionHeader, string?>(nameof(Subtitle));
    public static readonly StyledProperty<string?>  ActionTextProperty = AvaloniaProperty.Register<SectionHeader, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<SectionHeader, ICommand?>(nameof(ActionCommand));

    public string    Title         { get => GetValue(TitleProperty);         set => SetValue(TitleProperty, value); }
    public string?   Subtitle      { get => GetValue(SubtitleProperty);      set => SetValue(SubtitleProperty, value); }
    public string?   ActionText    { get => GetValue(ActionTextProperty);    set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public SectionHeader()
    {
        InitializeComponent();
        DataContext = this;
    }
}
