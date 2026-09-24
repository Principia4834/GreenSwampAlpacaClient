using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;

namespace GreenSwamp.Alpaca.Telescope.Tests.Model.Providers;

/// <summary>
/// Level 1 (test strategy §5.1): pure GreenSwamp-class detection logic
/// (SignalR-transport-implementation-plan-final.md §2), zero I/O, no AlpacaTelescope involved.
/// </summary>
public class GreenSwampClassDetectorTests
{
    /// <summary>The real reference server's actual DriverInfo value, confirmed by direct query.</summary>
    private const string RealServerDriverInfo = "GreenSwamp.Alpaca.Server, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

    [Theory]
    [InlineData(RealServerDriverInfo)]
    [InlineData("GreenSwamp.Alpaca.Server, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null")]
    [InlineData("  GreenSwamp.Alpaca.Server  , Version=0.0.0.0")]
    [InlineData("GreenSwamp.Alpaca.Server,Version=0.0.0.0")]
    public void IsGreenSwampClass_ReturnsTrue_ForNameAndWellFormedVersion(string driverInfo)
    {
        GreenSwampClassDetector.IsGreenSwampClass(driverInfo).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GreenSwamp.Alpaca.Server")]
    [InlineData("GreenSwamp.Alpaca.Server, Culture=neutral, PublicKeyToken=null")]
    [InlineData("GreenSwamp.Alpaca.Server, Version=1.2.3")]
    [InlineData("GreenSwamp.Alpaca.Server, Version=1.2.3.4.5")]
    [InlineData("GreenSwamp.Alpaca.Server, Version=a.b.c.d")]
    [InlineData("ASCOM Simulator Telescope, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [InlineData("Some other text GreenSwamp.Alpaca.Server, Version=0.0.0.0")]
    [InlineData("Some unrelated driver")]
    public void IsGreenSwampClass_ReturnsFalse_WhenNameOrVersionCheckFails(string? driverInfo)
    {
        GreenSwampClassDetector.IsGreenSwampClass(driverInfo).Should().BeFalse();
    }
}
