using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as UpdatesViewModel)?.RefreshResume();
    }
}
