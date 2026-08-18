using CommunityToolkit.Mvvm.Input;

namespace RedisDesktop.App.ViewModels;

public interface IWorkspaceTab
{
    string Title { get; }

    ConnectionItemViewModel Connection { get; }

    IAsyncRelayCommand CloseCommand { get; }
}
