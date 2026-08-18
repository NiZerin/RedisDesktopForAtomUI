using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RedisDesktop.App.ViewModels;
using AtomTabControl = AtomUI.Desktop.Controls.TabControl;
using AtomTabItem = AtomUI.Desktop.Controls.TabItem;

namespace RedisDesktop.App.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
    }

    private void OnTabClosed(object? sender, TabClosedEventArgs e)
    {
        var tab = e.TabItem.DataContext as IWorkspaceTab ?? e.TabItem.Content as IWorkspaceTab;
        if (tab is not null && DataContext is WorkspaceViewModel vm)
        {
            _ = vm.CloseTabCommand.ExecuteAsync(tab);
        }
    }

    private void OnTabsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        if (sender is not AtomTabControl tabs)
        {
            return;
        }

        var source = e.Source as Visual;
        while (source is not null)
        {
            if (source is AtomTabItem tabItem)
            {
                tabs.CloseTab(tabItem);
                e.Handled = true;
                return;
            }

            source = source.GetVisualParent();
        }
    }
}
