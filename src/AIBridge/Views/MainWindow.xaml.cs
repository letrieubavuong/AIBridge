using System.Windows;

namespace AIBridge.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ViewModels.MainViewModel vm && e.NewValue != null)
        {
            vm.SelectNodeDetails(e.NewValue);
        }
    }
}
