using SharpSIP.Models;

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
/// The UI calls these methods; SIP internals are isolated here.
///
/// This is a stub – SIP registration and call handling will be
/// implemented in subsequent steps.
/// </summary>
public class SoftphoneService
{
    private readonly AppLogger _logger;

    // ── State ──────────────────────────────────────────────────────────────────

    public PhoneState State { get; private set; } = PhoneState.Idle;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event Action<PhoneState>? StateChanged;

    /// <summary>Raised when an inbound call arrives (caller display string).</summary>
    // The event will be fired once SIP inbound-call handling is implemented (next steps).
#pragma warning disable CS0067
    public event Action<string>? IncomingCall;
#pragma warning restore CS0067

    // ── Constructor ────────────────────────────────────────────────────────────

    public SoftphoneService(AppLogger logger)
    {
        _logger = logger;
    }

    // ── Public API (stubs) ─────────────────────────────────────────────────────

    /// <summary>Register the softphone with the SIP server.</summary>
    public void Register(SipSettings settings)
    {
        _logger.Log($"[STUB] Register called – server={settings.SipServer}, user={settings.Username}");
        SetState(PhoneState.Registering);

        // TODO: implement SIP registration via SIPSorcery.
    }

    /// <summary>Unregister from the SIP server.</summary>
    public void Unregister()
    {
        _logger.Log("[STUB] Unregister called.");
        SetState(PhoneState.Idle);

        // TODO: send SIP un-REGISTER.
    }

    /// <summary>Place an outbound call to <paramref name="destination"/>.</summary>
    public void Call(string destination)
    {
        _logger.Log($"[STUB] Call called – destination={destination}");
        SetState(PhoneState.Calling);

        // TODO: initiate SIP INVITE.
    }

    /// <summary>Answer a ringing inbound call.</summary>
    public void Answer()
    {
        _logger.Log("[STUB] Answer called.");
        SetState(PhoneState.Active);

        // TODO: send SIP 200 OK.
    }

    /// <summary>Reject a ringing inbound call.</summary>
    public void Reject()
    {
        _logger.Log("[STUB] Reject called.");
        SetState(PhoneState.Idle);

        // TODO: send SIP 603 Decline.
    }

    /// <summary>Hang up the active (or outbound-ringing) call.</summary>
    public void HangUp()
    {
        _logger.Log("[STUB] HangUp called.");
        SetState(PhoneState.Ended);

        // TODO: send SIP BYE / CANCEL.
    }

    /// <summary>Toggle microphone mute on the active call.</summary>
    public void SetMute(bool muted)
    {
        _logger.Log($"[STUB] SetMute called – muted={muted}");

        // TODO: mute/unmute audio session.
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetState(PhoneState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }
}
