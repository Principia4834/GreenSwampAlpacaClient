﻿using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Wire DTO for the GreenSwamp TelescopeStateHub's "ReceiveTelescopeState" payload
/// (greenswamp-telescopestate-signalr-spec.md §11.4 - field-by-field contract table). Deliberately
/// not a shared assembly/package with the server (§11.1/§11.2 - rejected in favor of loosely-typed
/// JSON with a maintained contract table): property names match TelescopeStateModel exactly
/// (server serializes as-declared PascalCase, §11.3), and enum-typed properties deserialize
/// directly into the client's own neutral mirror enums (TelescopePierSide, GreenSwampDriveRate,
/// GreenSwampSlewType, GreenSwampMountType, GreenSwampAlignmentMode) since JsonStringEnumConverter
/// (registered by GreenSwampSignalRProvider) matches by member name, not declaring type.
///
/// Internal - never referenced outside GreenSwamp.Alpaca.Telescope.Model; GreenSwampTelescopeStatePayloadMapper
/// is the only consumer, translating this DTO into the neutral TelescopeState/GreenSwampTelescopeState pair.
/// </summary>
internal sealed class GreenSwampTelescopeStatePayload
{
    public double Altitude { get; set; }
    public double Azimuth { get; set; }
    public double Declination { get; set; }
    public double RightAscension { get; set; }
    public TelescopePierSide SideOfPier { get; set; }
    public double LocalHourAngle { get; set; }
    public DateTime UTCDate { get; set; }
    public DateTime LocalDate { get; set; }
    public bool Slewing { get; set; }
    public bool Tracking { get; set; }
    public bool LimitsOn { get; set; }
    public bool LimitWarningActive { get; set; }
    public string LimitWarningMessage { get; set; } = string.Empty;
    public long LimitWarningSequence { get; set; }
    public bool AtPark { get; set; }
    public bool AtHome { get; set; }
    public bool LimitTriggered { get; set; }
    public bool IsMountRunning { get; set; }
    public string ComPort { get; set; } = string.Empty;
    public int ConnectedClientCount { get; set; }
    public bool HasEverBeenConnected { get; set; }
    public string? ParkSelectedName { get; set; }
    public List<string> ParkPositionNames { get; set; } = [];
    public double TargetRightAscension { get; set; }
    public double TargetDeclination { get; set; }
    public double ActualAxisX { get; set; }
    public double ActualAxisY { get; set; }
    public double AppAxisX { get; set; }
    public double AppAxisY { get; set; }
    public double[] AxisSteps { get; set; } = [];
    public GreenSwampDriveRate TrackingRate { get; set; }
    public bool IsPulseGuidingRa { get; set; }
    public bool IsPulseGuidingDec { get; set; }
    public GreenSwampSlewType SlewState { get; set; }
    public ulong LoopCounter { get; set; }
    public int TimerOverruns { get; set; }
    public DateTime LastUpdate { get; set; }
    public bool FlipOnNextGoto { get; set; }
    public double ControllerVoltage { get; set; }
    public bool LowVoltageEvent { get; set; }
    public bool VoiceActive { get; set; }
    public string VoiceName { get; set; } = string.Empty;
    public int VoiceVolume { get; set; }
    public bool IsAutoHomeRunning { get; set; }
    public int AutoHomeProgressBar { get; set; }
    public bool IsGermanPolarMode { get; set; }
    public double AutoHomeAxisX { get; set; }
    public double AutoHomeAxisY { get; set; }
    public long[] StepsPerRevolution { get; set; } = [];
    public double[] StepsWormPerRevolution { get; set; } = [];
    public long[] StepsTimeFreq { get; set; } = [];
    public GreenSwampVectorPayload TrackingOffsetRate { get; set; } = new();
    public bool CanPPec { get; set; }
    public bool CanHomeSensor { get; set; }
    public bool CanPolarLed { get; set; }
    public bool CanAdvancedCmdSupport { get; set; }
    public string MountName { get; set; } = string.Empty;
    public string[] MountVersion { get; set; } = [];
    public string Capabilities { get; set; } = string.Empty;
    public double SiteLatitude { get; set; }
    public GreenSwampAlignmentMode AlignmentMode { get; set; }
    public GreenSwampMountType MountType { get; set; }
    public double SiteLongitude { get; set; }
    public double SiteElevation { get; set; }
}

/// <summary>
/// Wire DTO for GreenSwamp.Alpaca.Shared.Vector's {X,Y} JSON shape (§11.3), used only for
/// TrackingOffsetRate. Mirrors into GreenSwampTelescopeState.TrackingOffsetRate's (double X, double Y)
/// tuple via GreenSwampTelescopeStatePayloadMapper.
/// </summary>
internal sealed class GreenSwampVectorPayload
{
    public double X { get; set; }
    public double Y { get; set; }
}
