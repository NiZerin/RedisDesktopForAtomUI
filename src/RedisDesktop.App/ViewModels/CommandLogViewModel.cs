using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public partial class CommandLogViewModel : ViewModelBase
{
    private readonly ICommandLog _log;
    private readonly MainWindowViewModel _owner;

    public CommandLogViewModel(ICommandLog log, MainWindowViewModel owner)
    {
        _log = log;
        _owner = owner;
        foreach (var entry in log.Entries)
        {
            Entries.Add(new CommandLogItemViewModel(entry));
        }

        RebuildVisible();
        _log.EntryAdded += OnEntryAdded;
    }

    public UiStrings Loc => _owner.Loc;

    public ObservableCollection<CommandLogItemViewModel> Entries { get; } = [];

    public ObservableCollection<CommandLogItemViewModel> VisibleEntries { get; } = [];

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private bool _showOnlyWrite;

    [ObservableProperty]
    private CommandLogItemViewModel? _selectedEntry;

    public event EventHandler? EntriesChanged;

    [RelayCommand]
    private void Clear()
    {
        _log.Clear();
        Entries.Clear();
        VisibleEntries.Clear();
        SelectedEntry = null;
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var top = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (top?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(SelectedEntry.FullCommand);
            _owner.Prompt.Info(Loc.Copied);
        }
    }

    partial void OnFilterChanged(string value) => RebuildVisible();

    partial void OnShowOnlyWriteChanged(bool value) => RebuildVisible();

    private void OnEntryAdded(object? sender, CommandLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = new CommandLogItemViewModel(entry);
            Entries.Add(item);
            while (Entries.Count > 5000)
            {
                Entries.RemoveAt(0);
            }

            if (Matches(item))
            {
                VisibleEntries.Add(item);
            }

            EntriesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void RebuildVisible()
    {
        VisibleEntries.Clear();
        foreach (var item in Entries)
        {
            if (Matches(item))
            {
                VisibleEntries.Add(item);
            }
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Matches(CommandLogItemViewModel item)
    {
        if (ShowOnlyWrite && !WriteCommands.IsWrite(item.Command))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        return item.Command.Contains(Filter, StringComparison.OrdinalIgnoreCase)
               || item.Args.Contains(Filter, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CommandLogItemViewModel
{
    public CommandLogItemViewModel(CommandLogEntry entry)
    {
        Time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Connection = entry.ConnectionName;
        Command = entry.Command;
        Args = Truncate(entry.Details);
        Cost = entry.ElapsedMilliseconds.ToString("0.00");
        IsSuccess = entry.Success;
        FullCommand = string.IsNullOrWhiteSpace(Args) ? Command : $"{Command} {Args}";
    }

    public string Time { get; }

    public string Connection { get; }

    public string Command { get; }

    public string Args { get; }

    public string Cost { get; }

    public bool IsSuccess { get; }

    public string FullCommand { get; }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var parts = value.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 100)
            {
                parts[i] = parts[i][..100] + "...";
            }
        }

        return string.Join(' ', parts);
    }
}
