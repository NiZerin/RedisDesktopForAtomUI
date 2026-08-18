using Avalonia.Controls;
using Avalonia.Input;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class KeyDetailView : UserControl
{
    public KeyDetailView()
    {
        InitializeComponent();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not KeyDetailViewModel vm)
        {
            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                      || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.S)
        {
            _ = vm.SaveCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 || (command && e.Key == Key.R))
        {
            _ = vm.RefreshCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.D)
        {
            _ = vm.DeleteCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void OnKeyNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is KeyDetailViewModel vm)
        {
            _ = vm.RenameCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void OnTtlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is KeyDetailViewModel vm)
        {
            _ = vm.ApplyTtlCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }
}
