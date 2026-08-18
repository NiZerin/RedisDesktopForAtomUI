using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RedisDesktop.App.Views;

public partial class NewKeyDialogView : UserControl
{
    public NewKeyDialogView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        KeyNameBox.Focus();
        KeyNameBox.SelectAll();
    }
}
