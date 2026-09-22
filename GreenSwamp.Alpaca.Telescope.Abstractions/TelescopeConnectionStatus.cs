namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// The discrete connection lifecycle states an ITelescopeSession can be in. Distinguishes a
/// deliberate Disconnected state from a Faulted one so the ViewModel can tell "never connected /
/// cleanly disconnected" apart from "something went wrong" (requirements FR44-48).
/// </summary>
public enum TelescopeConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Faulted
}

/// <summary>
/// Reports connection lifecycle plus the health of each transport this session may be using.
/// IsSignalRActive/IsSignalRDegraded are always false for a non-GreenSwamp-class device, and for
/// a GreenSwamp-class device until the future TelescopeStateHub (architecture §6.4) ships and a
/// live connection is established - this is not an error condition, just "not available yet".
/// </summary>
/// <param name="State">The overall session lifecycle state.</param>
/// <param name="IsAlpacaRestActive">True while the Alpaca REST transport (always present) is healthy.</param>
/// <param name="IsSignalRActive">True while the supplementary GreenSwamp SignalR transport is connected and healthy.</param>
/// <param name="IsSignalRDegraded">True when a GreenSwamp-class device's SignalR transport has been lost/is unavailable, so the session has fallen back to Alpaca REST alone (requirements FR47).</param>
/// <param name="ErrorMessage">An application-facing description of the most recent fault, if State is Faulted; null otherwise.</param>
public sealed record TelescopeConnectionStatus(
    TelescopeConnectionState State,
    bool IsAlpacaRestActive,
    bool IsSignalRActive,
    bool IsSignalRDegraded,
    string? ErrorMessage)
{
    /// <summary>The initial status of a freshly created, not-yet-connected session.</summary>
    public static TelescopeConnectionStatus Initial { get; } = new(
        TelescopeConnectionState.Disconnected,
        IsAlpacaRestActive: false,
        IsSignalRActive: false,
        IsSignalRDegraded: false,
        ErrorMessage: null);
}

/// <summary>
/// Payload for ITelescopeSession.ConnectionStatusChanged (architecture §6.2/§6.6).
/// </summary>
/// <param name="Status">The new connection status.</param>
public sealed class TelescopeConnectionStatusChangedEventArgs(TelescopeConnectionStatus status) : EventArgs
{
    public TelescopeConnectionStatus Status { get; } = status;
}
