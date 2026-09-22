using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using AscomTelescopeState = ASCOM.Common.DeviceStateClasses.TelescopeState;
using AppTelescopeState = GreenSwamp.Alpaca.Telescope.Abstractions.TelescopeState;

namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Maps ASCOM.Common types to the neutral Abstractions types, at the provider boundary, so
/// Abstractions/ViewModels never reference ASCOM.Common directly (architecture §6.2, §5.1).
/// </summary>
internal static class TelescopeStateMapper
{
    /// <summary>
    /// Maps the standard Alpaca DeviceState snapshot (implementation design §4.1) to TelescopeState.
    /// Fields outside the DeviceState-bundled set (§4.2 AP-only properties, GreenSwampTelescopeState
    /// §5.3) are not populated by this mapping - deferred to a later slice per stakeholder decision.
    /// </summary>
    public static AppTelescopeState ToTelescopeState(AscomTelescopeState deviceState, DateTimeOffset timeStamp)
    {
        return new AppTelescopeState
        {
            Altitude = deviceState.Altitude ?? 0,
            Azimuth = deviceState.Azimuth ?? 0,
            Declination = deviceState.Declination ?? 0,
            RightAscension = deviceState.RightAscension ?? 0,
            SideOfPier = ToTelescopePierSide(deviceState.SideOfPier),
            Slewing = deviceState.Slewing ?? false,
            Tracking = deviceState.Tracking ?? false,
            AtHome = deviceState.AtHome ?? false,
            AtPark = deviceState.AtPark ?? false,
            IsPulseGuiding = deviceState.IsPulseGuiding ?? false,
            UtcDate = deviceState.UTCDate ?? default,
            TimeStamp = timeStamp,
            GreenSwamp = null
        };
    }

    /// <summary>
    /// Maps ASCOM.Common.DeviceInterfaces.PointingState (nullable, as returned by DeviceState) to
    /// the neutral TelescopePierSide - a null value (mount cannot report pointing state) maps to
    /// Unknown, the same "don't know" value PointingState itself uses (implementation design §5.1).
    /// </summary>
    public static TelescopePierSide ToTelescopePierSide(PointingState? pointingState) => pointingState switch
    {
        PointingState.Normal => TelescopePierSide.Normal,
        PointingState.ThroughThePole => TelescopePierSide.ThroughThePole,
        _ => TelescopePierSide.Unknown
    };
}
