namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// One connected telescope instance: one per open tab (architecture §6.2). Owns whichever
/// transport provider(s) are appropriate for the connected device and exposes only
/// telescope-concept-level members - never raw protocol calls (requirements FR1-5).
///
/// Scope note: this interface currently exposes only the four commands analyzed in
/// implementation design §6 (Connect, FindHome, Park, Abort), plus Disconnect (session
/// lifecycle, not a telescope command). The wider command surface sketched in architecture §6.2
/// (SlewToCoordinatesAsync, SlewToAltAzAsync, JogAsync/StopJogAsync, SetTrackingAsync, and
/// UnparkAsync) is deferred to future design passes (implementation design §7) and intentionally
/// omitted here until each has been analyzed the same way FindHome/Park/Abort/Connect were.
/// </summary>
public interface ITelescopeSession : IAsyncDisposable
{
    Guid InstanceId { get; }

    /// <summary>Populated once at connect time; unchanged for the lifetime of the session.</summary>
    TelescopeCapabilities Capabilities { get; }

    TelescopeConnectionStatus Status { get; }

    /// <summary>
    /// Connects via ASCOM.Common.ClientExtensions.ConnectAsync (polls Connecting until false -
    /// implementation design §6.1). Connection failures are surfaced via ConnectionStatusChanged,
    /// not thrown as raw exceptions (requirements FR44-45).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Initiates and awaits homing. Gated only by Capabilities.CanFindHome; no additional
    /// client-side precondition checking is performed (implementation design §6.2) - any
    /// device-rejected call (e.g. GreenSwamp's ParkedException while parked) is caught here and
    /// surfaced as an application-facing error, never as a raw exception to the caller.
    /// Deliberately named without an "Async" suffix, matching the ASCOM FindHome() method name
    /// itself (implementation design §6.5) - this is the one intentional exception to this
    /// interface's XxxAsync naming convention.
    /// </summary>
    Task FindHome(CancellationToken ct = default);

    /// <summary>
    /// Initiates and awaits parking. Gated only by Capabilities.CanPark; no additional
    /// client-side precondition checking is performed (implementation design §6.3) - GreenSwamp's
    /// "no park position selected" DriverException (and any other device-rejected call) is caught
    /// here and surfaced as an application-facing error. A no-op/idempotent call while already
    /// parked is expected to succeed silently, per GreenSwamp's confirmed behavior.
    /// </summary>
    Task ParkAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops any motion in progress: slewing, parking, find-home, and move-axis (standard
    /// ITelescopeV4.AbortSlew semantics). No capability gate exists in the standard interface;
    /// always enabled whenever Connected (implementation design §6.4).
    /// </summary>
    Task AbortSlewAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised whenever a full telescope state snapshot is available from any active transport
    /// (architecture §6.5/§6.6, collapsed to a single full-snapshot event per implementation-design-
    /// stage amendment - see TelescopeState remarks). Raised on an arbitrary background thread;
    /// never assume the UI thread (architecture §6.6, item 2).
    /// </summary>
    event EventHandler<TelescopeStateUpdatedEventArgs>? StateUpdated;

    event EventHandler<TelescopeConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
}
