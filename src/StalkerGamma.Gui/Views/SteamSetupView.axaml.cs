using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class SteamSetupView : UserControl
{
    public SteamSetupView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is SteamSetupViewModel vm)
            {
                await vm.EnsurePreflightAsync();
            }
        };
    }
}
