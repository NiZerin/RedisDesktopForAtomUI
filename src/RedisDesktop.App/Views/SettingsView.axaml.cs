using Avalonia.Controls;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnFontDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.EnsureFonts();
        }
    }
}
