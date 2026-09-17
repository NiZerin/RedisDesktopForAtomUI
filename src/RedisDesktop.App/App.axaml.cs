using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Localization;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RedisDesktop.App.ViewModels;
using RedisDesktop.App.Views;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        this.UseAtomUI(builder =>
        {
            builder.UseLanguages(LanguageTags.ZhCN, UiLanguages.AtomUiTags);
            builder.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID);
            builder.UseAlibabaSansFont();
            builder.UseDesktopControls();
            builder.UseDesktopDataGrid();
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("UI", e.Exception);
            e.Handled = true;
        };

        var services = new ServiceCollection();
        services.AddRedisDesktop();
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.MainWindow = window;
            window.Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() => _ = viewModel.InitializeAsync(), DispatcherPriority.Background);
            };
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
