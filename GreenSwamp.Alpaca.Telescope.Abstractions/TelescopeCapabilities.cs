namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// Standard Alpaca ITelescopeV3/ITelescopeV4 capability flags, plus the single GreenSwamp-class
/// extension flag (implementation design §5.2). Populated once at connect time and held for the
/// lifetime of the session; a device does not change class or standard capabilities mid-session.
/// </summary>
public sealed class TelescopeCapabilities
{
    // --- Capability flags in scope for the four in-scope commands (§6) ---
    public bool CanFindHome { get; init; }
    public bool CanPark { get; init; }
    public bool CanUnpark { get; init; }

    // --- Capability flags exposed now for future command-surface design (§7), not yet used by any command ---
    public bool CanSetPark { get; init; }
    public bool CanPulseGuide { get; init; }
    public bool CanSetTracking { get; init; }
    public bool CanSetDeclinationRate { get; init; }
    public bool CanSetRightAscensionRate { get; init; }
    public bool CanSetGuideRates { get; init; }
    public bool CanSetPierSide { get; init; }
    public bool CanSlew { get; init; }
    public bool CanSlewAsync { get; init; }
    public bool CanSlewAltAz { get; init; }
    public bool CanSlewAltAzAsync { get; init; }
    public bool CanSync { get; init; }
    public bool CanSyncAltAz { get; init; }

    /// <summary>
    /// True when the connected device has been identified as a GreenSwampAlpacaServer-class
    /// telescope (architecture §6.8/§6.10 detection, via the standard Alpaca management
    /// description endpoint). When true, TelescopeSession's state stream additionally populates
    /// GreenSwampTelescopeState (implementation design §5.3); when false, that property is null.
    /// </summary>
    public bool IsGreenSwampClass { get; init; }
}
