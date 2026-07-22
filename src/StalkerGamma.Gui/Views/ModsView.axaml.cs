using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as ModsViewModel)?.Refresh();
    }
}
