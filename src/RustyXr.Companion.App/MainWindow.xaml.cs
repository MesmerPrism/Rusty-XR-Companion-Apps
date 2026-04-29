using System.Windows;
using RustyXr.Companion.App.ViewModels;

namespace RustyXr.Companion.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }
}
