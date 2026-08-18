using Avalonia.Controls;
using Avalonia.Input;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class CliTabView : UserControl
{
    public CliTabView()
    {
        InitializeComponent();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CliTabViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = vm.ExecuteCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.HistoryUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            vm.HistoryDown();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            vm.AcceptFirstSuggestion();
            e.Handled = true;
        }
        else if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ClearOutput();
            e.Handled = true;
        }
    }

    private void OnSuggestionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: string command } && DataContext is CliTabViewModel vm)
        {
            vm.UseSuggestionCommand.Execute(command);
        }
    }
}
