namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// Full-snapshot telescope state, delivered as a single unit via ITelescopeSession.StateUpdated
/// (implementation design §2a/§3/§4; collapsed from the architecture §6.2 sketch's separate
/// PositionUpdated/StateUpdated events into one snapshot event, since both the standard Alpaca
/// DeviceState call and the future GreenSwamp TelescopeStateHub each deliver one full refresh per
/// tick rather than separately-timed position/state updates - see implementation-design-stage
/// amendment note).
///
/// For this implementation slice, only the fields bundled in the standard Alpaca DeviceState
/// snapshot (implementation design §4.1) are populated by AlpacaTelescopeProvider; the wider
/// individually-gettable (AP) property surface (§4.2) and GreenSwampTelescopeState (§5.3) are
/// deferred to a later slice - GreenSwamp is always null until that work is done.
/// </summary>
public sealed class TelescopeState
{
    public double Altitude { get; init; }
    public double Azimuth { get; init; }
    public double Declination { get; init; }
    public double RightAscension { get; init; }
    public TelescopePierSide SideOfPier { get; init; }
    public bool Slewing { get; init; }
    public bool Tracking { get; init; }
    public bool AtHome { get; init; }
    public bool AtPark { get; init; }
    public bool IsPulseGuiding { get; init; }
    public DateTime UtcDate { get; init; }

    /// <summary>Snapshot capture time (client-assigned via TimeProvider at ingestion, not a transport wall-clock - architecture §6.5).</summary>
    public DateTimeOffset TimeStamp { get; init; }

    /// <summary>
    /// Non-null only when TelescopeCapabilities.IsGreenSwampClass is true and the GreenSwamp
    /// SignalR provider is populating it (implementation design §5.3). Always null for this
    /// implementation slice.
    /// </summary>
    public GreenSwampTelescopeState? GreenSwamp { get; init; }
}

/// <summary>
/// Payload for ITelescopeSession.StateUpdated (implementation-design-stage amendment to
/// architecture §6.2/§6.6 - see TelescopeState remarks).
/// </summary>
/// <param name="State">The full telescope state snapshot.</param>
public sealed class TelescopeStateUpdatedEventArgs(TelescopeState state) : EventArgs
{
    public TelescopeState State { get; } = state;
}
