using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class SlowLogTabView : UserControl
{
    public SlowLogTabView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SlowLogTabViewModel vm)
        {
            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                      || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.F5 || (command && e.Key == Key.R))
        {
            _ = vm.RefreshCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }
}

