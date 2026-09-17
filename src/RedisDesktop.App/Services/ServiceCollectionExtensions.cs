using Microsoft.Extensions.DependencyInjection;
using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisDesktop(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<IConnectionStore, JsonConnectionStore>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<ICommandLog, MemoryCommandLog>();
        services.AddSingleton<RedisSessionFactory>();
        services.AddSingleton<IRedisSessionFactory>(provider => provider.GetRequiredService<RedisSessionFactory>());
        services.AddSingleton<IBenchmarkService, RedisBenchmarkService>();
        services.AddSingleton<IAppUpdateService, GitHubAppUpdateService>();
        services.AddSingleton<IUserPrompt, AtomUiUserPrompt>();
        services.AddSingleton<IThemeService, AtomUiThemeService>();
        services.AddSingleton<ViewModels.MainWindowViewModel>();
        return services;
    }
}
