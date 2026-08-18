using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class KeyDetailTabViewModel : ViewModelBase, IWorkspaceTab
{
    public KeyDetailTabViewModel(ConnectionWorkspaceViewModel host)
    {
        Host = host;
        Connection = host.Connection;
        Detail = new KeyDetailViewModel(host);
        Title = host.Connection.Config.Name;
    }

    public ConnectionWorkspaceViewModel Host { get; }

    public ConnectionItemViewModel Connection { get; }

    public KeyDetailViewModel Detail { get; }

    public RedisKeyBytes? CurrentKey { get; private set; }

    public int Database { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    public override string ToString() => Title;

    public bool Matches(ConnectionItemViewModel connection, RedisKeyBytes key, int database)
        => Connection == connection
           && Database == database
           && CurrentKey is { } current
           && current.Equals(key);

    public async Task ShowKeyAsync(RedisKeyBytes key)
    {
        await Detail.OpenAsync(key);
        CurrentKey = key;
        Database = Connection.Session?.CurrentDatabase ?? 0;
        Title = $"{key.ToDisplayString()} | {Connection.Config.Name} | DB{Database}";
    }

    [RelayCommand]
    private Task CloseAsync() => Host.Owner.Workspace.CloseTabCommand.ExecuteAsync(this);
}
