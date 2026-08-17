using System.Windows;
using Poe2DesktopClock.Desktop.ViewModels;

namespace Poe2DesktopClock.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
