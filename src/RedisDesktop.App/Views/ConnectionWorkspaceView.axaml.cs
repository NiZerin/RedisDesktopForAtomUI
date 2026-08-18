using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class ConnectionWorkspaceView : UserControl
{
    public ConnectionWorkspaceView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnStatusKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnStatusKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConnectionWorkspaceViewModel vm)
        {
            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                      || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.F5 || (command && e.Key == Key.R))
        {
            _ = vm.RefreshStatusCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }
}
