using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
            (DataContext as InstallViewModel)?.RefreshProfilePaths();
    }
}
