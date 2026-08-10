using System.Windows;
using Vmic.App.ViewModels;

namespace Vmic.App;

/// <summary>
/// Single application window. Hosts the role picker and the Host/Client views via
/// a <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
