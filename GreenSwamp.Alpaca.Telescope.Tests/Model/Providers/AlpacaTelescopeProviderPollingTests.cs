using ASCOM.Common.DeviceInterfaces;
using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;
using Microsoft.Extensions.Time.Testing;
using AscomTelescopeState = ASCOM.Common.DeviceStateClasses.TelescopeState;

namespace GreenSwamp.Alpaca.Telescope.Tests.Model.Providers;

public sealed class AlpacaTelescopeProviderPollingTests
{
    [Fact]
    public void GetState_PollsSlowSupplementalFields_ImmediatelyAndEvery30Seconds()
    {
        var fakeClient = new FakeAlpacaTelescopeClient();
        var provider = new AlpacaTelescopeProvider(fakeClient);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _ = provider.GetState(timeProvider);
        _ = provider.GetState(timeProvider);

        fakeClient.SlowPollReadCount.Should().Be(5);

        timeProvider.Advance(TimeSpan.FromSeconds(29) + TimeSpan.FromMilliseconds(999));
        _ = provider.GetState(timeProvider);
        fakeClient.SlowPollReadCount.Should().Be(5);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var refreshed = provider.GetState(timeProvider);

        fakeClient.SlowPollReadCount.Should().Be(10, "30 second cadence should be exact");
        refreshed.SiteLatitude.Should().Be(fakeClient.SiteLatitude);
        refreshed.SiteLongitude.Should().Be(fakeClient.SiteLongitude);
        refreshed.SiteElevation.Should().Be(fakeClient.SiteElevation);
        refreshed.AlignmentMode.Should().Be(GreenSwampAlignmentMode.GermanPolar);
        refreshed.TrackingRate.Should().Be(GreenSwampDriveRate.Solar);
    }

    [Fact]
    public void GetState_PollsTargetCoordinates_EverySecond_AndOnSlewingTransition()
    {
        var fakeClient = new FakeAlpacaTelescopeClient();
        var provider = new AlpacaTelescopeProvider(fakeClient);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _ = provider.GetState(timeProvider);
        fakeClient.TargetPollReadCount.Should().Be(2);

        _ = provider.GetState(timeProvider);
        fakeClient.TargetPollReadCount.Should().Be(2);

        fakeClient.CurrentState = new AscomTelescopeState { Slewing = true };
        _ = provider.GetState(timeProvider);
        fakeClient.TargetPollReadCount.Should().Be(4);

        _ = provider.GetState(timeProvider);
        fakeClient.TargetPollReadCount.Should().Be(4);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var refreshed = provider.GetState(timeProvider);
        fakeClient.TargetPollReadCount.Should().Be(6);
        refreshed.TargetRightAscension.Should().Be(fakeClient.TargetRightAscension);
        refreshed.TargetDeclination.Should().Be(fakeClient.TargetDeclination);
    }

    [Fact]
    public void GetState_SequencesMultiCadenceReads_AndPreservesLatestKnownValues()
    {
        var fakeClient = new FakeAlpacaTelescopeClient();
        var provider = new AlpacaTelescopeProvider(fakeClient);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var first = provider.GetState(timeProvider);
        fakeClient.ReadSequence.Should().ContainInOrder(
            "SiteLatitude",
            "SiteLongitude",
            "SiteElevation",
            "AlignmentMode",
            "TrackingRate",
            "TargetRightAscension",
            "TargetDeclination");

        fakeClient.ReadSequence.Clear();
        fakeClient.SiteLatitudeValue = 40.1;
        fakeClient.TargetRightAscensionValue = 7.7;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var second = provider.GetState(timeProvider);

        fakeClient.ReadSequence.Should().ContainInOrder("TargetRightAscension", "TargetDeclination");
        fakeClient.ReadSequence.Should().NotContain("SiteLatitude", "30-second fields should not be read at 1-second cadence");
        second.SiteLatitude.Should().Be(first.SiteLatitude, "latest known 30-second value should be retained until its cadence is due");
        second.TargetRightAscension.Should().Be(7.7);

        fakeClient.ReadSequence.Clear();
        timeProvider.Advance(TimeSpan.FromSeconds(29));
        var third = provider.GetState(timeProvider);

        fakeClient.ReadSequence.Should().ContainInOrder(
            "SiteLatitude",
            "SiteLongitude",
            "SiteElevation",
            "AlignmentMode",
            "TrackingRate",
            "TargetRightAscension",
            "TargetDeclination");
        third.SiteLatitude.Should().Be(40.1);
    }

    [Fact]
    public void GetState_WhenSupplementalPropertiesNotImplemented_StillReturnsDeviceStateSnapshot()
    {
        var fakeClient = new FakeAlpacaTelescopeClient
        {
            CurrentState = new AscomTelescopeState
            {
                Altitude = 10.5,
                Azimuth = 200.2,
                Declination = -33.1,
                RightAscension = 4.25,
                Slewing = true
            },
            ThrowOnSlowSupplementalRead = true,
            ThrowOnTargetSupplementalRead = true
        };

        var provider = new AlpacaTelescopeProvider(fakeClient);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var state = provider.GetState(timeProvider);

        state.Altitude.Should().Be(10.5);
        state.Azimuth.Should().Be(200.2);
        state.Declination.Should().Be(-33.1);
        state.RightAscension.Should().Be(4.25);
        state.Slewing.Should().BeTrue();
    }

    private sealed class FakeAlpacaTelescopeClient : IAlpacaTelescopeClient
    {
        public AscomTelescopeState CurrentState { get; set; } = new() { Slewing = false };

        public int SlowPollReadCount { get; private set; }
        public int TargetPollReadCount { get; private set; }
        public List<string> ReadSequence { get; } = [];
        public double SiteLatitudeValue { get; set; } = 51.5;
        public double SiteLongitudeValue { get; set; } = -0.1;
        public double SiteElevationValue { get; set; } = 123.4;
        public int AlignmentModeValueRaw { get; set; } = 2;
        public int TrackingRateValueRaw { get; set; } = 2;
        public double TargetRightAscensionValue { get; set; } = 5.5;
        public double TargetDeclinationValue { get; set; } = -22.5;
        public bool ThrowOnSlowSupplementalRead { get; set; }
        public bool ThrowOnTargetSupplementalRead { get; set; }

        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task FindHomeAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ParkAsync(CancellationToken ct) => Task.CompletedTask;
        public Task AbortSlewAsync(CancellationToken ct) => Task.CompletedTask;
        public AscomTelescopeState GetDeviceState() => CurrentState;
        public bool CanFindHome => true;
        public bool CanPark => true;
        public bool CanUnpark => true;
        public bool CanSetPark => true;
        public bool CanPulseGuide => true;
        public bool CanSetTracking => true;
        public bool CanSetDeclinationRate => true;
        public bool CanSetRightAscensionRate => true;
        public bool CanSetGuideRates => true;
        public bool CanSetPierSide => true;
        public bool CanSlew => true;
        public bool CanSlewAsync => true;
        public bool CanSlewAltAz => true;
        public bool CanSlewAltAzAsync => true;
        public bool CanSync => true;
        public bool CanSyncAltAz => true;
        public string DriverInfo => "GreenSwampAlpacaServer v4.0";

        public double SiteLatitude
        {
            get
            {
                SlowPollReadCount++;
                ReadSequence.Add(nameof(SiteLatitude));
                if (ThrowOnSlowSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return SiteLatitudeValue;
            }
        }

        public double SiteLongitude
        {
            get
            {
                SlowPollReadCount++;
                ReadSequence.Add(nameof(SiteLongitude));
                if (ThrowOnSlowSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return SiteLongitudeValue;
            }
        }

        public double SiteElevation
        {
            get
            {
                SlowPollReadCount++;
                ReadSequence.Add(nameof(SiteElevation));
                if (ThrowOnSlowSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return SiteElevationValue;
            }
        }

        public int AlignmentModeValue
        {
            get
            {
                SlowPollReadCount++;
                ReadSequence.Add("AlignmentMode");
                if (ThrowOnSlowSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return AlignmentModeValueRaw;
            }
        }

        public int TrackingRateValue
        {
            get
            {
                SlowPollReadCount++;
                ReadSequence.Add("TrackingRate");
                if (ThrowOnSlowSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return TrackingRateValueRaw;
            }
        }

        public double TargetRightAscension
        {
            get
            {
                TargetPollReadCount++;
                ReadSequence.Add(nameof(TargetRightAscension));
                if (ThrowOnTargetSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return TargetRightAscensionValue;
            }
        }

        public double TargetDeclination
        {
            get
            {
                TargetPollReadCount++;
                ReadSequence.Add(nameof(TargetDeclination));
                if (ThrowOnTargetSupplementalRead)
                {
                    throw new ASCOM.PropertyNotImplementedException();
                }
                return TargetDeclinationValue;
            }
        }

        public void Dispose()
        {
        }
    }
}
