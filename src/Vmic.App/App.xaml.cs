using System.Windows;

namespace Vmic.App;

/// <summary>
/// Application entry point / composition root. Wires up the main window.
/// Kept intentionally thin — most logic lives in ViewModels and Vmic.Core.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep a single instance; the app is single-role-per-process.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.Message,
                "Vmic — unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
