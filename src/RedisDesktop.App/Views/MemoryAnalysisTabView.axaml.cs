using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class MemoryAnalysisTabView : UserControl
{
    public MemoryAnalysisTabView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: MemoryKeyRowViewModel row }
            && DataContext is MemoryAnalysisTabViewModel vm)
        {
            _ = vm.OpenKeyCommand.ExecuteAsync(row);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MemoryAnalysisTabViewModel vm)
        {
            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                      || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if ((e.Key == Key.F5 || (command && e.Key == Key.R)) && vm.CanRestart)
        {
            _ = vm.RestartCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }
}

