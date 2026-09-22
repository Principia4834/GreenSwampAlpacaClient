using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;

namespace GreenSwamp.Alpaca.Telescope.Tests.Model.Providers;

/// <summary>
/// Level 1 (test strategy §5.1): pure GreenSwamp-class detection logic (implementation design
/// §5.2), zero I/O, no AlpacaTelescope involved.
/// </summary>
public class GreenSwampClassDetectorTests
{
    [Theory]
    [InlineData("Green Swamp Alpaca Server", null)]
    [InlineData("GreenSwamp Alpaca Server", null)]
    [InlineData(null, "GreenSwampServer")]
    [InlineData("Some other text GreenSwampServer more text", null)]
    [InlineData("GREEN SWAMP ALPACA SERVER", null)]
    public void IsGreenSwampClass_RecognizesKnownIdentityMarkers(string? description, string? driverInfo)
    {
        GreenSwampClassDetector.IsGreenSwampClass(description, driverInfo).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("ASCOM Simulator Telescope", "ASCOM Telescope Simulator Driver")]
    [InlineData("Some unrelated device", "Some unrelated driver")]
    public void IsGreenSwampClass_ReturnsFalse_ForNonGreenSwampDevices(string? description, string? driverInfo)
    {
        GreenSwampClassDetector.IsGreenSwampClass(description, driverInfo).Should().BeFalse();
    }
}
