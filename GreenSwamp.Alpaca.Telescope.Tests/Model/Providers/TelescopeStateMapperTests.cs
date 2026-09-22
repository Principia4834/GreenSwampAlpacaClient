using System;
using ASCOM.Common.DeviceInterfaces;
using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;
using AscomTelescopeState = ASCOM.Common.DeviceStateClasses.TelescopeState;

namespace GreenSwamp.Alpaca.Telescope.Tests.Model.Providers;

/// <summary>
/// Level 1 (test strategy §5.1): pure mapping logic, zero I/O, no AlpacaTelescope involved.
/// </summary>
public class TelescopeStateMapperTests
{
    [Fact]
    public void ToTelescopeState_MapsAllDeviceStateBundledFields()
    {
        var timeStamp = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var utcDate = new DateTime(2025, 6, 1, 11, 59, 0, DateTimeKind.Utc);

        var deviceState = new AscomTelescopeState
        {
            Altitude = 45.5,
            Azimuth = 180.25,
            Declination = -12.3,
            RightAscension = 6.75,
            SideOfPier = PointingState.ThroughThePole,
            Slewing = true,
            Tracking = true,
            AtHome = false,
            AtPark = false,
            IsPulseGuiding = true,
            UTCDate = utcDate
        };

        var result = TelescopeStateMapper.ToTelescopeState(deviceState, timeStamp);

        result.Altitude.Should().Be(45.5);
        result.Azimuth.Should().Be(180.25);
        result.Declination.Should().Be(-12.3);
        result.RightAscension.Should().Be(6.75);
        result.SideOfPier.Should().Be(TelescopePierSide.ThroughThePole);
        result.Slewing.Should().BeTrue();
        result.Tracking.Should().BeTrue();
        result.AtHome.Should().BeFalse();
        result.AtPark.Should().BeFalse();
        result.IsPulseGuiding.Should().BeTrue();
        result.UtcDate.Should().Be(utcDate);
        result.TimeStamp.Should().Be(timeStamp);
        result.GreenSwamp.Should().BeNull("GreenSwampTelescopeState population is deferred to a later slice");
    }

    [Fact]
    public void ToTelescopeState_NullNumericAndBooleanFields_MapToDefaults()
    {
        var deviceState = new AscomTelescopeState();

        var result = TelescopeStateMapper.ToTelescopeState(deviceState, DateTimeOffset.UnixEpoch);

        result.Altitude.Should().Be(0);
        result.Azimuth.Should().Be(0);
        result.Declination.Should().Be(0);
        result.RightAscension.Should().Be(0);
        result.Slewing.Should().BeFalse();
        result.Tracking.Should().BeFalse();
        result.AtHome.Should().BeFalse();
        result.AtPark.Should().BeFalse();
        result.IsPulseGuiding.Should().BeFalse();
        result.UtcDate.Should().Be(default);
    }

    [Theory]
    [InlineData(PointingState.Normal, TelescopePierSide.Normal)]
    [InlineData(PointingState.ThroughThePole, TelescopePierSide.ThroughThePole)]
    [InlineData(PointingState.Unknown, TelescopePierSide.Unknown)]
    [InlineData(null, TelescopePierSide.Unknown)]
    public void ToTelescopePierSide_MapsEveryPointingStateValue(PointingState? pointingState, TelescopePierSide expected)
    {
        TelescopeStateMapper.ToTelescopePierSide(pointingState).Should().Be(expected);
    }
}
