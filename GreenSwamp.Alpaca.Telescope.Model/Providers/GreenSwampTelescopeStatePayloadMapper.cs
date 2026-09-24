using GreenSwamp.Alpaca.Telescope.Abstractions;
using AppTelescopeState = GreenSwamp.Alpaca.Telescope.Abstractions.TelescopeState;

namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Maps GreenSwampTelescopeStatePayload (the SignalR wire DTO) to the neutral TelescopeState/
/// GreenSwampTelescopeState pair, at the provider boundary, so Abstractions/ViewModels never
/// reference the payload DTO directly (architecture §6.2, §5.1 - same rationale as
/// TelescopeStateMapper for the Alpaca REST path). Field-by-field mapping matches
/// greenswamp-telescopestate-signalr-spec.md §11.4 exactly - no fields are invented.
/// </summary>
internal static class GreenSwampTelescopeStatePayloadMapper
{
    public static AppTelescopeState ToTelescopeState(GreenSwampTelescopeStatePayload payload, DateTimeOffset timeStamp)
    {
        return new AppTelescopeState
        {
            Altitude = payload.Altitude,
            Azimuth = payload.Azimuth,
            Declination = payload.Declination,
            RightAscension = payload.RightAscension,
            SideOfPier = payload.SideOfPier,
            Slewing = payload.Slewing,
            Tracking = payload.Tracking,
            AtHome = payload.AtHome,
            AtPark = payload.AtPark,
            IsPulseGuiding = payload.IsPulseGuidingRa || payload.IsPulseGuidingDec,
            UtcDate = payload.UTCDate,
            SiteLatitude = payload.SiteLatitude,
            SiteLongitude = payload.SiteLongitude,
            SiteElevation = payload.SiteElevation,
            AlignmentMode = payload.AlignmentMode,
            TrackingRate = payload.TrackingRate,
            TargetRightAscension = payload.TargetRightAscension,
            TargetDeclination = payload.TargetDeclination,
            TimeStamp = timeStamp,
            GreenSwamp = ToGreenSwampTelescopeState(payload)
        };
    }

    public static GreenSwampTelescopeState ToGreenSwampTelescopeState(GreenSwampTelescopeStatePayload payload)
    {
        return new GreenSwampTelescopeState
        {
            LimitsOn = payload.LimitsOn,
            LimitTriggered = payload.LimitTriggered,
            LimitWarningActive = payload.LimitWarningActive,
            LimitWarningMessage = payload.LimitWarningMessage,
            LimitWarningSequence = payload.LimitWarningSequence,
            IsMountRunning = payload.IsMountRunning,
            ComPort = payload.ComPort,
            ConnectedClientCount = payload.ConnectedClientCount,
            HasEverBeenConnected = payload.HasEverBeenConnected,
            ParkSelectedName = payload.ParkSelectedName,
            ParkPositionNames = payload.ParkPositionNames,
            ActualAxisX = payload.ActualAxisX,
            ActualAxisY = payload.ActualAxisY,
            AppAxisX = payload.AppAxisX,
            AppAxisY = payload.AppAxisY,
            AxisSteps = payload.AxisSteps,
            IsPulseGuidingRa = payload.IsPulseGuidingRa,
            IsPulseGuidingDec = payload.IsPulseGuidingDec,
            SlewState = payload.SlewState,
            FlipOnNextGoto = payload.FlipOnNextGoto,
            LoopCounter = payload.LoopCounter,
            TimerOverruns = payload.TimerOverruns,
            LastUpdate = payload.LastUpdate,
            LocalHourAngle = payload.LocalHourAngle,
            LocalDate = payload.LocalDate,
            ControllerVoltage = payload.ControllerVoltage,
            LowVoltageEvent = payload.LowVoltageEvent,
            VoiceActive = payload.VoiceActive,
            VoiceName = payload.VoiceName,
            VoiceVolume = payload.VoiceVolume,
            IsAutoHomeRunning = payload.IsAutoHomeRunning,
            AutoHomeProgressBar = payload.AutoHomeProgressBar,
            AutoHomeAxisX = payload.AutoHomeAxisX,
            AutoHomeAxisY = payload.AutoHomeAxisY,
            IsGermanPolarMode = payload.IsGermanPolarMode,
            StepsPerRevolution = payload.StepsPerRevolution,
            StepsWormPerRevolution = payload.StepsWormPerRevolution,
            StepsTimeFreq = payload.StepsTimeFreq,
            TrackingOffsetRate = (payload.TrackingOffsetRate.X, payload.TrackingOffsetRate.Y),
            CanPPec = payload.CanPPec,
            CanHomeSensor = payload.CanHomeSensor,
            CanPolarLed = payload.CanPolarLed,
            CanAdvancedCmdSupport = payload.CanAdvancedCmdSupport,
            MountName = payload.MountName,
            MountVersion = payload.MountVersion,
            Capabilities = payload.Capabilities,
            MountType = payload.MountType
        };
    }
}
