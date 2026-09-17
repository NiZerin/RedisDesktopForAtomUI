using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;
using RedisDesktop.Infrastructure;

namespace RedisDesktop.App.ViewModels;

public partial class SlowLogTabViewModel : ViewModelBase, IWorkspaceTab
{
    public const int FetchCount = 1000;

    private readonly MainWindowViewModel _owner;
    private bool _costDesc = true;

    public SlowLogTabViewModel(MainWindowViewModel owner, ConnectionItemViewModel connection)
    {
        _owner = owner;
        Connection = connection;
        Title = $"{connection.Config.Name} {owner.Loc.SlowLog}";
        StatusText = owner.Loc.SlowLogHint;
    }

    public UiStrings Loc => _owner.Loc;

    public string Title { get; }

    public ConnectionItemViewModel Connection { get; }

    public override string ToString() => Title;

    public ObservableCollection<SlowLogRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowRows))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _configText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNode))]
    private bool _hasNode;

    public bool ShowEmpty => !IsBusy && Rows.Count == 0;

    public bool ShowRows => Rows.Count > 0;

    public bool ShowNode => HasNode;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || Connection.Session is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var session = Connection.Session;
            var entries = await session.Observability.GetSlowLogAsync(FetchCount);
            var config = await session.Observability.GetSlowLogConfigAsync();
            HasNode = entries.Any(item => !string.IsNullOrWhiteSpace(item.Node));
            Rows.Clear();
            foreach (var entry in entries)
            {
                Rows.Add(new SlowLogRowViewModel(entry));
            }

            ApplyOrder();
            ConfigText = $"slowlog-log-slower-than: {config.SlowerThan}    slowlog-max-len: {config.MaxLen}";
            StatusText = Rows.Count == 0
                ? Loc.NoSlowLog
                : Loc.Format("slow_log_count", "共 {0} 条", "{0} entries", Rows.Count);
        }
        catch (Exception ex)
        {
            AppLog.Error("SlowLog", ex);
            StatusText = ex.Message;
            await _owner.Prompt.ErrorAsync(Loc.SlowLog, ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowEmpty));
            OnPropertyChanged(nameof(ShowRows));
        }
    }

    [RelayCommand]
    private void ToggleCostOrder()
    {
        if (IsBusy || Rows.Count == 0)
        {
            return;
        }

        _costDesc = !_costDesc;
        ApplyOrder();
    }

    [RelayCommand]
    private Task CloseAsync() => _owner.Workspace.CloseTabCommand.ExecuteAsync(this);

    private void ApplyOrder()
    {
        var ordered = _costDesc
            ? Rows.OrderByDescending(row => row.CostMilliseconds).ToList()
            : Rows.OrderBy(row => row.CostMilliseconds).ToList();
        Rows.Clear();
        foreach (var row in ordered)
        {
            Rows.Add(row);
        }
    }
}

public sealed class SlowLogRowViewModel(SlowLogEntry entry)
{
    public long Id { get; } = entry.Id;

    public string TimeText { get; } = entry.TimeText;

    public string TimeOfDayText { get; } = entry.TimeOfDayText;

    public string Command { get; } = entry.Command;

    public string CostText { get; } = $"{entry.CostText} ms";

    public double CostMilliseconds { get; } = entry.CostMilliseconds;

    public string Source { get; } = entry.Source ?? string.Empty;

    public string ClientName { get; } = entry.ClientName ?? string.Empty;

    public string Node { get; } = entry.Node ?? string.Empty;
}
