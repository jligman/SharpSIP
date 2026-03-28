using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace SharpSIP.Services;

/// <summary>
/// Thread-safe logger that posts entries to an ObservableCollection
/// so they can be data-bound to a WPF TextBox / ListBox.
/// </summary>
public class AppLogger
{
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<string> Entries { get; } = new();

    public AppLogger(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";

        if (_dispatcher.CheckAccess())
            Entries.Add(entry);
        else
            _dispatcher.Invoke(() => Entries.Add(entry));
    }
}
