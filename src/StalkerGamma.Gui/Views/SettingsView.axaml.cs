using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // A one-click install can create the first profile while another page is showing, so
        // re-sync the list every time this page comes up.
        AttachedToVisualTree += (_, _) => (DataContext as SettingsViewModel)?.Reload();
    }
}
