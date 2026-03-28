using System.Windows;
using SharpSIP.Services;

namespace SharpSIP;

/// <summary>
/// Application entry-point. Creates the shared services and passes them to MainWindow.
/// </summary>
public partial class App : Application
{
    private SoftphoneService? _softphone;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = new AppLogger(Dispatcher);
        _softphone  = new SoftphoneService(logger);

        var window = new MainWindow(_softphone, logger);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _softphone?.Dispose();
        base.OnExit(e);
    }
}
