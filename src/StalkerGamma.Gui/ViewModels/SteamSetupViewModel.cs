using CommunityToolkit.Mvvm.ComponentModel;

namespace StalkerGamma.Gui.ViewModels;

public partial class SteamSetupViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Steam integration coming in the next step.";
}
