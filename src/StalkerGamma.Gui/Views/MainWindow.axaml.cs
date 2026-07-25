using System.Collections.Specialized;
using Avalonia.Controls;
using StalkerGamma.Gui.ViewModels;

namespace StalkerGamma.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tail the log pane: without this it stays pinned to the first line while a 40GB
        // install scrolls thousands of entries past. Skipped when the pane is closed (nothing
        // to scroll) or when the user turned Follow off to read back through it.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }
            vm.LogLines.CollectionChanged += (_, e) =>
            {
                if (
                    e.Action != NotifyCollectionChangedAction.Add
                    || !vm.LogPaneOpen
                    || !vm.FollowLog
                    || LogList.ItemCount == 0
                )
                {
                    return;
                }
                LogList.ScrollIntoView(LogList.ItemCount - 1);
            };
        };
    }
}