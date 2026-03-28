using System.Windows;
using SharpSIP.Services;

namespace SharpSIP;

/// <summary>
/// Application entry-point. Creates the shared services and passes them to MainWindow.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = new AppLogger(Dispatcher);
        var softphone = new SoftphoneService(logger);

        var window = new MainWindow(softphone, logger);
        window.Show();
    }
}
