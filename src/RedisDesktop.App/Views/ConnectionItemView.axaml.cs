using Avalonia.Controls;
using Avalonia.Input;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class ConnectionItemView : UserControl
{
    public ConnectionItemView()
    {
        InitializeComponent();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ConnectionItemViewModel item)
        {
            item.SelectSelf();
        }
    }

    private void OnAddonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ConnectionItemViewModel item)
        {
            item.SelectSelf();
        }

        e.Handled = true;
    }
}
