using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class WorkspaceViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;

    public WorkspaceViewModel(MainWindowViewModel owner)
    {
        _owner = owner;
    }

    public UiStrings Loc => _owner.Loc;

    public ObservableCollection<IWorkspaceTab> Tabs { get; } = [];

    [ObservableProperty]
    private IWorkspaceTab? _selectedTab;

    public bool HasTabs => Tabs.Count > 0;

    public bool HasNoTabs => Tabs.Count == 0;

    public void Open(ConnectionItemViewModel connection, bool scanKeys = false)
    {
        var status = connection.StatusHost;
        if (status is null)
        {
            return;
        }

        if (!Tabs.Contains(status))
        {
            Tabs.Add(status);
        }

        SelectedTab = status;
        status.ResumeStatus();
        connection.NotifyWorkspace();
        NotifyTabs();
        _ = scanKeys ? status.RefreshAsync() : status.RefreshStatusCommand.ExecuteAsync(null);
    }

    public void Activate(ConnectionItemViewModel connection) => Open(connection);

    public async Task OpenKeyAsync(ConnectionItemViewModel connection, RedisKeyBytes key, bool newTab = false)
    {
        var host = connection.StatusHost;
        if (host is null)
        {
            return;
        }

        var database = connection.Session?.CurrentDatabase ?? 0;
        var existing = Tabs.OfType<KeyDetailTabViewModel>()
            .FirstOrDefault(tab => tab.Matches(connection, key, database));
        if (existing is not null)
        {
            SelectedTab = existing;
            NotifyTabs();
            return;
        }

        if (!newTab && SelectedTab is KeyDetailTabViewModel current)
        {
            var index = Tabs.IndexOf(current);
            var replacement = new KeyDetailTabViewModel(host);
            await replacement.ShowKeyAsync(key);
            Tabs[index] = replacement;
            SelectedTab = replacement;
            NotifyTabs();
            return;
        }

        var tab = new KeyDetailTabViewModel(host);
        await tab.ShowKeyAsync(key);
        Tabs.Add(tab);
        SelectedTab = tab;
        NotifyTabs();
    }

    public Task OpenCliAsync(ConnectionItemViewModel connection)
    {
        if (connection.Session is null)
        {
            return Task.CompletedTask;
        }

        var existing = Tabs.OfType<CliTabViewModel>().FirstOrDefault(x => x.Connection == connection);
        if (existing is null)
        {
            existing = new CliTabViewModel(_owner, connection);
            Tabs.Add(existing);
        }

        SelectedTab = existing;
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasNoTabs));
        return Task.CompletedTask;
    }

    public Task OpenPubSubAsync(ConnectionItemViewModel connection)
    {
        if (connection.Session is null)
        {
            return Task.CompletedTask;
        }

        var existing = Tabs.OfType<PubSubTabViewModel>().FirstOrDefault(x => x.Connection == connection);
        if (existing is null)
        {
            existing = new PubSubTabViewModel(_owner, connection);
            Tabs.Add(existing);
        }

        SelectedTab = existing;
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasNoTabs));
        return Task.CompletedTask;
    }

    public async Task CloseAsync(ConnectionItemViewModel connection)
    {
        var toRemove = Tabs.Where(x => x.Connection == connection).ToList();
        foreach (var tab in toRemove)
        {
            if (tab is ConnectionWorkspaceViewModel workspace)
            {
                workspace.PauseAutoRefresh();
            }

            if (tab is PubSubTabViewModel pubSub)
            {
                await pubSub.StopAsync();
            }

            Tabs.Remove(tab);
        }

        if (SelectedTab is null || toRemove.Contains(SelectedTab))
        {
            SelectedTab = Tabs.LastOrDefault();
        }

        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasNoTabs));
        await Task.CompletedTask;
    }

    public async Task CloseAllAsync()
    {
        foreach (var tab in Tabs.OfType<PubSubTabViewModel>().ToList())
        {
            await tab.StopAsync();
        }

        foreach (var tab in Tabs.OfType<ConnectionWorkspaceViewModel>())
        {
            tab.PauseAutoRefresh();
        }

        Tabs.Clear();
        SelectedTab = null;
        NotifyTabs();
    }

    [RelayCommand]
    private async Task CloseTabAsync(IWorkspaceTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (tab is ConnectionWorkspaceViewModel status)
        {
            status.PauseAutoRefresh();
        }

        if (tab is PubSubTabViewModel pubSubTab)
        {
            await pubSubTab.StopAsync();
        }

        Tabs.Remove(tab);
        if (SelectedTab == tab)
        {
            SelectedTab = Tabs.LastOrDefault();
        }

        NotifyTabs();
    }

    private void NotifyTabs()
    {
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasNoTabs));
    }
}
