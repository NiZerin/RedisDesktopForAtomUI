using CommunityToolkit.Mvvm.ComponentModel;

namespace RedisDesktop.App.ViewModels;

public partial class PromptTextViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;
}
