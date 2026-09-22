namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// GreenSwampAlpacaServer-only telescope state and capability data with no standard-Alpaca
/// equivalent (implementation design §4.3). Non-null only when TelescopeCapabilities.IsGreenSwampClass
/// is true. Field set is a direct, evidence-based mirror of TelescopeStateModel's GreenSwamp-specific
/// members (see design §3 for the exposure decision behind each field) - no fields are invented.
///
/// Not yet populated by any provider in this implementation slice (§5.3 population is deferred
/// until the GreenSwamp SignalR provider work begins) - defined now so the shape exists and
/// TelescopeState.GreenSwamp can be typed against it.
/// </summary>
public sealed class GreenSwampTelescopeState
{
    // --- Safety / soft limits (§3.2) ---
    public bool LimitsOn { get; init; }
    public bool LimitTriggered { get; init; }
    public bool LimitWarningActive { get; init; }
    public string LimitWarningMessage { get; init; } = string.Empty;
    public long LimitWarningSequence { get; init; }

    // --- Server / connection diagnostics (§3.2) ---
    public bool IsMountRunning { get; init; }
    public string ComPort { get; init; } = string.Empty;
    public int ConnectedClientCount { get; init; }
    public bool HasEverBeenConnected { get; init; }

    // --- Named park positions (§3.3) ---
    public string? ParkSelectedName { get; init; }
    public IReadOnlyList<string> ParkPositionNames { get; init; } = [];

    // --- Mount-specific axis positions (§3.5) ---
    public double ActualAxisX { get; init; }
    public double ActualAxisY { get; init; }
    public double AppAxisX { get; init; }
    public double AppAxisY { get; init; }
    public IReadOnlyList<double> AxisSteps { get; init; } = [];

    // --- Per-axis pulse guiding (§3.6) ---
    public bool IsPulseGuidingRa { get; init; }
    public bool IsPulseGuidingDec { get; init; }

    // --- Slew kind / pier flip (§3.2) ---
    public GreenSwampSlewType SlewState { get; init; }
    public bool FlipOnNextGoto { get; init; }

    // --- Performance / diagnostics (§3.7) ---
    public ulong LoopCounter { get; init; }
    public int TimerOverruns { get; init; }
    public DateTime LastUpdate { get; init; }

    // --- Timing not standard-modeled (§3.1) ---
    public double LocalHourAngle { get; init; }
    public DateTime LocalDate { get; init; }

    // --- SkyWatcher hardware telemetry (§3.8) ---
    public double ControllerVoltage { get; init; }
    public bool LowVoltageEvent { get; init; }
    public bool VoiceActive { get; init; }
    public string VoiceName { get; init; } = string.Empty;
    public int VoiceVolume { get; init; }

    // --- AutoHome routine (§3.9, distinct from standard FindHome) ---
    public bool IsAutoHomeRunning { get; init; }
    public int AutoHomeProgressBar { get; init; }
    public double AutoHomeAxisX { get; init; }
    public double AutoHomeAxisY { get; init; }
    public bool IsGermanPolarMode { get; init; }

    // --- Mount gearing details (§3.10) ---
    public IReadOnlyList<long> StepsPerRevolution { get; init; } = [];
    public IReadOnlyList<double> StepsWormPerRevolution { get; init; } = [];
    public IReadOnlyList<long> StepsTimeFreq { get; init; } = [];
    public (double X, double Y) TrackingOffsetRate { get; init; }

    // --- Mount capabilities / identity (§3.11) ---
    public bool CanPPec { get; init; }
    public bool CanHomeSensor { get; init; }
    public bool CanPolarLed { get; init; }
    public bool CanAdvancedCmdSupport { get; init; }
    public string MountName { get; init; } = string.Empty;
    public IReadOnlyList<string> MountVersion { get; init; } = [];
    public string Capabilities { get; init; } = string.Empty;

    // --- Site / mount type (§3.12) ---
    public GreenSwampMountType MountType { get; init; }
}

/// <summary>Neutral mirror of GreenSwamp.Alpaca.MountControl.SlewType (protocol insulation, same rationale as TelescopeAxis/TelescopePierSide).</summary>
public enum GreenSwampSlewType
{
    None,
    Settle,
    MoveAxis,
    RaDec,
    AltAz,
    Park,
    Home,
    Handpad,
    Complete
}

/// <summary>Neutral mirror of GreenSwamp.Alpaca.MountControl.MountType.</summary>
public enum GreenSwampMountType
{
    Simulator,
    SkyWatcher
}
