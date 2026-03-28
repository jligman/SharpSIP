using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SharpSIP.Models;
using SharpSIP.Services;

namespace SharpSIP;

/// <summary>
/// Code-behind for the main window.
/// Wires UI events to SoftphoneService calls and reacts to state changes.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SoftphoneService _softphone;
    private readonly AppLogger _logger;
    private readonly SipSettings _settings;

    public MainWindow(SoftphoneService softphone, AppLogger logger)
    {
        _softphone = softphone;
        _logger = logger;

        InitializeComponent();

        // Load persisted settings and populate fields.
        _settings = SipSettings.Load();
        TxtServer.Text = _settings.SipServer;
        TxtUsername.Text = _settings.Username;
        PbPassword.Password = _settings.Password;
        TxtDisplayName.Text = _settings.DisplayName;

        // Bind the logger's collection to the log list.
        LstLog.ItemsSource = _logger.Entries;

        // Subscribe to softphone events.
        _softphone.StateChanged += OnStateChanged;
        _softphone.IncomingCall += OnIncomingCall;

        _logger.Log("SharpSIP started.");
    }

    // ── Button handlers ────────────────────────────────────────────────────────

    private void BtnRegister_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        _softphone.Register(_settings);
    }

    private void BtnUnregister_Click(object sender, RoutedEventArgs e)
    {
        _softphone.Unregister();
    }

    private void BtnCall_Click(object sender, RoutedEventArgs e)
    {
        var number = TxtDialNumber.Text.Trim();
        if (string.IsNullOrEmpty(number))
        {
            _logger.Log("Enter a number to dial.");
            return;
        }

        _softphone.Call(number);
    }

    private void BtnAnswer_Click(object sender, RoutedEventArgs e)
    {
        _softphone.Answer();
    }

    private void BtnReject_Click(object sender, RoutedEventArgs e)
    {
        _softphone.Reject();
    }

    private void BtnHangUp_Click(object sender, RoutedEventArgs e)
    {
        _softphone.HangUp();
    }

    private void TglMute_Click(object sender, RoutedEventArgs e)
    {
        bool muted = TglMute.IsChecked == true;
        TglMute.Content = muted ? "Unmute" : "Mute";
        _softphone.SetMute(muted);
    }

    // ── Softphone event handlers ───────────────────────────────────────────────

    private void OnStateChanged(PhoneState state)
    {
        // Already on the UI thread (AppLogger dispatches; SoftphoneService stubs are synchronous).
        // Once real async SIP code is added, dispatch will be needed here too.
        Dispatcher.Invoke(() => ApplyState(state));
    }

    private void OnIncomingCall(string caller)
    {
        Dispatcher.Invoke(() =>
        {
            LblCallStatus.Content = $"Incoming: {caller}";
            _logger.Log($"Incoming call from {caller}");
        });
    }

    // ── State → UI mapping ────────────────────────────────────────────────────

    private void ApplyState(PhoneState state)
    {
        LblStatus.Content = state.ToString();
        LblStatus.Foreground = state switch
        {
            PhoneState.Registered  => Brushes.Green,
            PhoneState.Active      => Brushes.DarkGreen,
            PhoneState.IncomingCall => Brushes.DarkOrange,
            PhoneState.Calling     => Brushes.CornflowerBlue,
            PhoneState.Failed      => Brushes.Red,
            PhoneState.Ended       => Brushes.Gray,
            _                      => Brushes.Gray
        };

        bool registered   = state == PhoneState.Registered;
        bool incoming     = state == PhoneState.IncomingCall;
        bool active       = state == PhoneState.Active;
        bool calling      = state == PhoneState.Calling;
        bool callInProgress = active || calling;

        BtnRegister.IsEnabled   = state == PhoneState.Idle || state == PhoneState.Failed || state == PhoneState.Ended;
        BtnUnregister.IsEnabled = registered || calling || active || incoming;
        BtnCall.IsEnabled       = registered;
        BtnAnswer.IsEnabled     = incoming;
        BtnReject.IsEnabled     = incoming;
        BtnHangUp.IsEnabled     = callInProgress;
        TglMute.IsEnabled       = active;

        if (!callInProgress && !incoming)
            LblCallStatus.Content = "—";

        // Auto-scroll log to bottom.
        if (LstLog.Items.Count > 0)
            LstLog.ScrollIntoView(LstLog.Items[LstLog.Items.Count - 1]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SaveSettingsFromUi()
    {
        _settings.SipServer    = TxtServer.Text.Trim();
        _settings.Username     = TxtUsername.Text.Trim();
        _settings.Password     = PbPassword.Password;
        _settings.DisplayName  = TxtDisplayName.Text.Trim();
        _settings.Save();
    }
}
