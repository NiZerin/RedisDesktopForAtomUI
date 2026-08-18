using CommunityToolkit.Mvvm.ComponentModel;
using RedisDesktop.App;
using RedisDesktop.Core;

namespace RedisDesktop.App.ViewModels;

public sealed record NewKeyTypeOption(string Display, RedisKeyType Type)
{
    public override string ToString() => Display;
}

public partial class NewKeyDialogViewModel : ViewModelBase
{
    public NewKeyDialogViewModel(UiStrings loc)
    {
        Loc = loc;
        SelectedType = TypeOptions[0];
    }

    public UiStrings Loc { get; }

    public IReadOnlyList<NewKeyTypeOption> TypeOptions { get; } =
    [
        new("String", RedisKeyType.String),
        new("Hash", RedisKeyType.Hash),
        new("List", RedisKeyType.List),
        new("Set", RedisKeyType.Set),
        new("Zset", RedisKeyType.SortedSet),
        new("Stream", RedisKeyType.Stream)
    ];

    [ObservableProperty]
    private string _keyName = string.Empty;

    [ObservableProperty]
    private NewKeyTypeOption? _selectedType;
}
