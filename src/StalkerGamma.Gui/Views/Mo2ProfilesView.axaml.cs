using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class Mo2ProfilesView : UserControl
{
    public Mo2ProfilesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as Mo2ProfilesViewModel)?.Refresh();
    }
}
