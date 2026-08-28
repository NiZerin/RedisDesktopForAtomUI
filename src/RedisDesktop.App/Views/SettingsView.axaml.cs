using Avalonia.Controls;
using Avalonia.Interactivity;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.EnsureFonts();
        }
    }

    private void OnFontDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.EnsureFonts();
        }
    }
}
