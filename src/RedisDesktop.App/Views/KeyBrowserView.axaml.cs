using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class KeyBrowserView : UserControl
{
    public KeyBrowserView()
    {
        InitializeComponent();
    }

    private void OnPatternKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is KeyBrowserViewModel vm)
        {
            _ = vm.SearchCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CaptureOpenModifiers(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            && FindDataContext<KeyNodeViewModel>(e.Source) is { } node
            && DataContext is KeyBrowserViewModel vm)
        {
            vm.SuppressOpenOnce = true;
            vm.SelectedTreeNode = node;
        }
    }

    private void OnFlatPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CaptureOpenModifiers(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            && FindDataContext<KeyListItemViewModel>(e.Source) is { } item
            && DataContext is KeyBrowserViewModel vm)
        {
            vm.SuppressOpenOnce = true;
            vm.SelectedFlatItem = item;
        }
    }

    private void OnFlatKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is KeyBrowserViewModel { SelectedFlatItem: { } item } vm)
        {
            _ = vm.DetailOpen(item.Key);
            e.Handled = true;
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is KeyBrowserViewModel { SelectedTreeNode: { IsKey: true, FullKey: { } key } } vm)
        {
            _ = vm.DetailOpen(key);
            e.Handled = true;
        }
    }

    private void CaptureOpenModifiers(PointerPressedEventArgs e)
    {
        if (DataContext is KeyBrowserViewModel vm)
        {
            vm.OpenInNewTabOnce = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                                  || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        }
    }

    private static T? FindDataContext<T>(object? source) where T : class
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && control.DataContext is T match)
            {
                return match;
            }
        }

        return null;
    }
}
