using System.ComponentModel;
using AtomUI;
using AtomUI.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RedisDesktop.App.ViewModels;

namespace RedisDesktop.App.Views;

public partial class MainWindow : AtomUI.Desktop.Controls.Window
{
    private MainWindowViewModel? _subscribed;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = DataContext as MainWindowViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            ApplySideBarWidth(_subscribed.SideBarWidth);
            ApplyUiFont(_subscribed.UiFontFamily);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SideBarWidth) && sender is MainWindowViewModel vm)
        {
            ApplySideBarWidth(vm.SideBarWidth);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.UiFontFamily) && sender is MainWindowViewModel fontVm)
        {
            ApplyUiFont(fontVm.UiFontFamily);
        }
    }

    private void ApplyUiFont(FontFamily? font)
    {
        if (font is null)
        {
            ClearValue(FontFamilyProperty);
            return;
        }

        FontFamily = font;
    }

    private void ApplySideBarWidth(double width)
    {
        var pixels = Math.Clamp(width < 220 ? 280 : width, 220, 520);
        Splitter.SetSize(AsidePane, new Dimension(pixels, DimensionUnitType.Pixel));
    }

    private void OnMainSplitterResizeCompleted(object? sender, SplitterResizeEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || e.Sizes.Count == 0)
        {
            return;
        }

        vm.SideBarWidth = Math.Clamp(e.Sizes[0], 220, 520);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Shutdown();
        }

        e.Cancel = false;
        base.OnClosing(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control)
        {
            return;
        }

        if (e.Key == Key.N)
        {
            _ = vm.NewConnectionCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma)
        {
            _ = vm.OpenSettingsCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (e.Key == Key.G)
        {
            _ = vm.ToggleCommandLogCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (e.Key is Key.Oem2 or Key.OemQuestion)
        {
            var tips = new SettingsViewModel(vm);
            _ = vm.Prompt.ShowHotkeysAsync(tips);
            e.Handled = true;
        }
    }
}