using System.Collections;
using AtomUI.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class CommandLogView : UserControl
{
    private CommandLogViewModel? _subscribed;

    public CommandLogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        LogGrid.CellPointerPressed += OnCellPointerPressed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.EntriesChanged -= OnEntriesChanged;
        }

        _subscribed = DataContext as CommandLogViewModel;
        if (_subscribed is not null)
        {
            _subscribed.EntriesChanged += OnEntriesChanged;
            ScrollToLatest();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.EntriesChanged -= OnEntriesChanged;
            _subscribed = null;
        }
    }

    private void OnEntriesChanged(object? sender, EventArgs e) => ScrollToLatest();

    private void OnCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (e.Row.DataContext is not CommandLogItemViewModel item
            || DataContext is not CommandLogViewModel vm)
        {
            return;
        }

        vm.SelectedEntry = item;
        _ = vm.CopySelectedCommand.ExecuteAsync(null);
    }

    private void ScrollToLatest()
    {
        if (LogGrid.ItemsSource is IList { Count: > 0 } items)
        {
            LogGrid.ScrollIntoView(items[items.Count - 1], null);
        }
    }
}
