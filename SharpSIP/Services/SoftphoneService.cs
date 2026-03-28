using SharpSIP.Models;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace SharpSIP.Services;

/// <summary>
/// Represents the current state of the softphone.
/// </summary>
public enum PhoneState
{
    Idle,
    Registering,
    Registered,
    IncomingCall,
    Calling,
    Active,
    Ended,
    Failed
}

/// <summary>
/// Facade that owns all SIP and audio logic.
/// The UI calls these methods; all SIP and media internals are isolated here.
/// </summary>
public class SoftphoneService : IDisposable
{
    private readonly AppLogger _logger;

    // ── SIP transport and agents ────────────────────────────────────────────────
    private SIPTransport?               _transport;
    private SIPRegistrationUserAgent?   _regAgent;
    private SIPUserAgent?               _userAgent;

    // Holds the server-side transaction for an inbound call until the user answers or rejects.
    private SIPServerUserAgent?         _pendingUas;

    // ── Audio ───────────────────────────────────────────────────────────────────
    private VoIPMediaSession?           _mediaSession;
    private WindowsAudioEndPoint?       _winAudio;

    // ── Misc ────────────────────────────────────────────────────────────────────
    private SipSettings?                _currentSettings;
    private bool                        _disposed;

    // ── State ───────────────────────────────────────────────────────────────────

    public PhoneState State { get; private set; } = PhoneState.Idle;

    // ── Events ──────────────────────────────────────────────────────────────────

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event Action<PhoneState>? StateChanged;

    /// <summary>Raised when an inbound call arrives. Argument is the caller display string.</summary>
    public event Action<string>? IncomingCall;

    // ── Constructor ─────────────────────────────────────────────────────────────

    public SoftphoneService(AppLogger logger) => _logger = logger;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Register the softphone with the SIP server.</summary>
    public void Register(SipSettings settings)
    {
        // Normalize before storing so the clean value is used for calls too.
        settings.SipServer = NormalizeServer(settings.SipServer);
        _currentSettings   = settings;

        _logger.Log($"Registering {settings.Username}@{settings.SipServer}...");
        SetState(PhoneState.Registering);

        try
        {
            TearDownTransport();

            _transport = new SIPTransport();

            // Create the user agent. It listens for inbound INVITEs on the transport.
            _userAgent = new SIPUserAgent(_transport, null);
            _userAgent.OnIncomingCall      += OnIncomingCallReceived;
            _userAgent.OnCallHungup        += OnRemoteHangup;
            _userAgent.ClientCallAnswered  += OnClientCallAnswered;
            _userAgent.ClientCallFailed    += OnClientCallFailed;
            _userAgent.ServerCallCancelled += OnServerCallCancelled;

            // The last five parameters of this constructor have sensible defaults
            // (retry logic, no exit on failure, etc.).
            _regAgent = new SIPRegistrationUserAgent(
                _transport,
                settings.Username,
                settings.Password,
                settings.SipServer,
                300);   // expiry in seconds; auto-renews before it lapses

            _regAgent.RegistrationSuccessful       += OnRegistrationSuccessful;
            _regAgent.RegistrationFailed           += OnRegistrationFailed;
            _regAgent.RegistrationTemporaryFailure += OnRegistrationTempFail;

            _regAgent.Start();
        }
        catch (Exception ex)
        {
            _logger.Log($"Register error: {ex.Message}");
            SetState(PhoneState.Failed);
        }
    }

    /// <summary>Unregister from the SIP server and return to idle.</summary>
    public void Unregister()
    {
        _logger.Log("Unregistering...");
        // sendZeroExpiryRegister: true sends a REGISTER with expiry=0 to formally deregister.
        _regAgent?.Stop(sendZeroExpiryRegister: true);
        TearDownTransport();
        SetState(PhoneState.Idle);
    }

    /// <summary>Place an outbound call to <paramref name="destination"/>.</summary>
    public async Task Call(string destination)
    {
        if (_currentSettings == null || _transport == null || _userAgent == null)
        {
            _logger.Log("Cannot call: not registered.");
            return;
        }

        _logger.Log($"Calling {destination}...");
        SetState(PhoneState.Calling);

        try
        {
            var callUri      = $"sip:{destination}@{_currentSettings.SipServer}";
            var mediaSession = CreateMediaSession();

            bool answered = await _userAgent.Call(
                callUri,
                _currentSettings.Username,
                _currentSettings.Password,
                mediaSession,
                ringTimeout: 0);    // 0 = no timeout; ring until answered or cancelled

            if (answered)
            {
                _mediaSession = mediaSession;
                _logger.Log("Call connected.");
                SetState(PhoneState.Active);
            }
            else
            {
                _logger.Log("Call ended before being answered.");
                mediaSession.Close("not-answered");
                SetState(PostCallState());
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Call error: {ex.Message}");
            SetState(PhoneState.Failed);
        }
    }

    /// <summary>Answer a ringing inbound call.</summary>
    public async Task Answer()
    {
        if (_pendingUas == null || _userAgent == null)
        {
            _logger.Log("No incoming call to answer.");
            return;
        }

        _logger.Log("Answering call...");

        try
        {
            var mediaSession = CreateMediaSession();

            // publicIpAddress has a default of null — SIPSorcery will detect the local IP.
            bool ok = await _userAgent.Answer(_pendingUas, mediaSession);

            if (ok)
            {
                _mediaSession = mediaSession;
                _logger.Log("Call answered.");
                SetState(PhoneState.Active);
            }
            else
            {
                _logger.Log("Answer failed.");
                mediaSession.Close("answer-failed");
                SetState(PostCallState());
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Answer error: {ex.Message}");
            SetState(PostCallState());
        }
        finally
        {
            _pendingUas = null;
        }
    }

    /// <summary>Reject a ringing inbound call.</summary>
    public void Reject()
    {
        if (_pendingUas == null)
        {
            _logger.Log("No incoming call to reject.");
            return;
        }

        _logger.Log("Rejecting call.");
        _pendingUas.Reject(SIPResponseStatusCodesEnum.Decline, null);
        _pendingUas = null;
        SetState(PostCallState());
    }

    /// <summary>Hang up the active or outbound-ringing call.</summary>
    public void HangUp()
    {
        _logger.Log("Hanging up.");

        if (_userAgent != null)
        {
            if (_userAgent.IsCallActive)
                _userAgent.Hangup();
            else if (_userAgent.IsCalling || _userAgent.IsRinging)
                _userAgent.Cancel();
        }

        CloseMediaSession();
        SetState(PostCallState());
    }

    /// <summary>Mute or unmute the local microphone on an active call.</summary>
    public void SetMute(bool muted)
    {
        if (_winAudio == null) return;

        _logger.Log($"Microphone {(muted ? "muted" : "unmuted")}.");

        // PauseAudio stops the microphone capture; ResumeAudio restarts it.
        var task = muted ? _winAudio.PauseAudio() : _winAudio.ResumeAudio();
        task.ContinueWith(
            t => _logger.Log($"Audio mute error: {t.Exception?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ── Registration event handlers ─────────────────────────────────────────────

    private void OnRegistrationSuccessful(SIPURI aor, SIPResponse response)
    {
        _logger.Log($"Registered as {aor}.");
        SetState(PhoneState.Registered);
    }

    private void OnRegistrationFailed(SIPURI aor, SIPResponse response, string errorMessage)
    {
        _logger.Log($"Registration failed: {errorMessage}");
        SetState(PhoneState.Failed);
    }

    private void OnRegistrationTempFail(SIPURI aor, SIPResponse response, string errorMessage)
    {
        _logger.Log($"Registration retry: {errorMessage}");
        // State stays Registering; the agent will retry automatically.
    }

    // ── Call event handlers ─────────────────────────────────────────────────────

    private void OnIncomingCallReceived(SIPUserAgent ua, SIPRequest inviteRequest)
    {
        if (_userAgent?.IsCallActive == true)
        {
            // Already in a call — send Busy Here and ignore.
            var busyUas = ua.AcceptCall(inviteRequest);
            busyUas.Reject(SIPResponseStatusCodesEnum.BusyHere, null);
            _logger.Log("Incoming call rejected (busy).");
            return;
        }

        // AcceptCall sends a 100 Trying and holds the transaction open until we answer or reject.
        _pendingUas = ua.AcceptCall(inviteRequest);

        var caller = inviteRequest.Header.From?.FromName
                     ?? inviteRequest.Header.From?.FromURI?.User
                     ?? "Unknown";

        _logger.Log($"Incoming call from {caller}.");
        SetState(PhoneState.IncomingCall);
        IncomingCall?.Invoke(caller);
    }

    private void OnRemoteHangup(SIPDialogue dialogue)
    {
        _logger.Log("Call ended by remote party.");
        CloseMediaSession();
        _pendingUas = null;
        SetState(PostCallState());
    }

    private void OnClientCallAnswered(ISIPClientUserAgent uac, SIPResponse response)
    {
        _logger.Log($"Remote party answered ({(int)response.Status} {response.ReasonPhrase}).");
    }

    private void OnClientCallFailed(ISIPClientUserAgent uac, string errorMessage, SIPResponse response)
    {
        _logger.Log($"Call failed: {errorMessage}");
        CloseMediaSession();
        SetState(PhoneState.Failed);
    }

    private void OnServerCallCancelled(ISIPServerUserAgent uas, SIPRequest cancelRequest)
    {
        _logger.Log("Incoming call cancelled by caller.");
        CloseMediaSession();
        _pendingUas = null;
        SetState(PostCallState());
    }

    // ── Audio helpers ───────────────────────────────────────────────────────────

    private VoIPMediaSession CreateMediaSession()
    {
        var audioEncoder = new AudioEncoder();
        _winAudio        = new WindowsAudioEndPoint(audioEncoder);

        return new VoIPMediaSession(new MediaEndPoints
        {
            AudioSource = _winAudio,
            AudioSink   = _winAudio
        });
    }

    private void CloseMediaSession()
    {
        if (_mediaSession != null)
        {
            _mediaSession.Close("done");
            _mediaSession = null;
        }

        _winAudio = null;
    }

    // ── Transport teardown ──────────────────────────────────────────────────────

    private void TearDownTransport()
    {
        CloseMediaSession();
        _pendingUas = null;

        if (_regAgent != null)
        {
            _regAgent.RegistrationSuccessful       -= OnRegistrationSuccessful;
            _regAgent.RegistrationFailed           -= OnRegistrationFailed;
            _regAgent.RegistrationTemporaryFailure -= OnRegistrationTempFail;
            _regAgent.Stop(sendZeroExpiryRegister: false);
            _regAgent = null;
        }

        if (_userAgent != null)
        {
            _userAgent.OnIncomingCall      -= OnIncomingCallReceived;
            _userAgent.OnCallHungup        -= OnRemoteHangup;
            _userAgent.ClientCallAnswered  -= OnClientCallAnswered;
            _userAgent.ClientCallFailed    -= OnClientCallFailed;
            _userAgent.ServerCallCancelled -= OnServerCallCancelled;

            if (_userAgent.IsCallActive)
                _userAgent.Hangup();

            _userAgent = null;
        }

        if (_transport != null)
        {
            _transport.Shutdown();
            _transport = null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips any URL/SIP scheme prefix from a server address typed by the user,
    /// so that "https://host:5001/path" or "sip:host" becomes "host:5001" / "host".
    /// SIPSorcery's URI parser expects a bare host[:port] string here.
    /// </summary>
    private static string NormalizeServer(string server)
    {
        var s = server.Trim();

        // Use .NET's Uri class to extract host[:port] from any full URL the user may
        // have pasted (e.g., "https://host:5001/path").  This handles IPv6 brackets,
        // trailing paths, and query strings correctly without manual string-slicing.
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Authority;   // "host", "host:port", or "[::1]:port" for IPv6

        // Bare "sip:" or "sips:" prefix with no authority separator (//).
        if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase))
            s = s[4..].Trim();
        else if (s.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
            s = s[5..].Trim();

        return s;
    }

    /// <summary>
    /// Returns the appropriate idle state after a call ends.
    /// If still registered, go back to Registered; otherwise go to Idle.
    /// </summary>
    private PhoneState PostCallState() =>
        _regAgent?.IsRegistered == true ? PhoneState.Registered : PhoneState.Idle;

    private void SetState(PhoneState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TearDownTransport();
    }
}
