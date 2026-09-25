using FluentAssertions;
using GreenSwamp.Alpaca.Client.ViewModels;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.TestHarness;

namespace GreenSwamp.Alpaca.Client.Tests.ViewModels;

/// <summary>
/// Level 2 (test strategy §5.2): TelescopeTabViewModel against a FakeTelescopeSession - no
/// transport, no real provider, no Avalonia dispatcher (ImmediateUiDispatcher instead).
/// </summary>
public class TelescopeTabViewModelTests
{
    private static (TelescopeTabViewModel ViewModel, FakeTelescopeSession Session) CreateSut(
        TelescopeCapabilities? capabilities = null)
    {
        var session = new FakeTelescopeSession();
        if (capabilities is not null)
        {
            session.Capabilities = capabilities;
        }

        var vm = new TelescopeTabViewModel(session, new ImmediateUiDispatcher());
        return (vm, session);
    }

    [Fact]
    public async Task ConnectCommand_CallsSessionConnectAsync()
    {
        var (vm, session) = CreateSut();

        await vm.ConnectCommand.ExecuteAsync(null);

        session.ConnectCallCount.Should().Be(1);
        vm.IsConnected.Should().BeTrue();
        vm.ConnectionState.Should().Be(TelescopeConnectionState.Connected);
    }

    [Fact]
    public async Task DisconnectCommand_CallsSessionDisconnectAsync()
    {
        var (vm, session) = CreateSut();

        await vm.DisconnectCommand.ExecuteAsync(null);

        session.DisconnectCallCount.Should().Be(1);
        vm.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task AbortCommand_AlwaysEnabled_CallsSessionAbortSlewAsync()
    {
        var (vm, session) = CreateSut(new TelescopeCapabilities()); // no capabilities granted at all

        vm.AbortCommand.CanExecute(null).Should().BeTrue("Abort has no Can* gate - implementation design §6.4");
        await vm.AbortCommand.ExecuteAsync(null);

        session.AbortSlewCallCount.Should().Be(1);
    }

    [Fact]
    public void FindHomeCommand_Disabled_WhenCanFindHomeIsFalse()
    {
        var (vm, _) = CreateSut(new TelescopeCapabilities { CanFindHome = false });

        vm.FindHomeCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task FindHomeCommand_Enabled_WhenCanFindHomeIsTrue_AndCallsSession()
    {
        var (vm, session) = CreateSut(new TelescopeCapabilities { CanFindHome = true });

        vm.FindHomeCommand.CanExecute(null).Should().BeTrue();
        await vm.FindHomeCommand.ExecuteAsync(null);

        session.FindHomeCallCount.Should().Be(1);
    }

    [Fact]
    public void ParkCommand_Disabled_WhenCanParkIsFalse()
    {
        var (vm, _) = CreateSut(new TelescopeCapabilities { CanPark = false });

        vm.ParkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ParkCommand_Enabled_WhenCanParkIsTrue_AndCallsSession()
    {
        var (vm, session) = CreateSut(new TelescopeCapabilities { CanPark = true });

        vm.ParkCommand.CanExecute(null).Should().BeTrue();
        await vm.ParkCommand.ExecuteAsync(null);

        session.ParkCallCount.Should().Be(1);
    }

    [Fact]
    public void OnStateUpdated_UpdatesAllBindableStateFields()
    {
        var (vm, session) = CreateSut();
        var timeStamp = DateTimeOffset.UtcNow;
        var utcDate = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var state = new TelescopeState
        {
            Altitude = 10,
            Azimuth = 20,
            Declination = 30,
            RightAscension = 5,
            SideOfPier = TelescopePierSide.ThroughThePole,
            Slewing = true,
            Tracking = true,
            AtHome = true,
            AtPark = false,
            IsPulseGuiding = true,
            UtcDate = utcDate,
            TimeStamp = timeStamp,
            SiteLatitude = 51.5,
            SiteLongitude = -0.12,
            SiteElevation = 35,
            AlignmentMode = GreenSwampAlignmentMode.GermanPolar,
            TrackingRate = GreenSwampDriveRate.Lunar,
            TargetRightAscension = 12.34,
            TargetDeclination = -22.2,
            GreenSwamp = new GreenSwampTelescopeState { MountName = "GS Sim" }
        };

        session.RaiseStateUpdated(state);

        vm.Altitude.Should().Be(10);
        vm.Azimuth.Should().Be(20);
        vm.Declination.Should().Be(30);
        vm.RightAscension.Should().Be(5);
        vm.SideOfPier.Should().Be(TelescopePierSide.ThroughThePole);
        vm.IsSlewing.Should().BeTrue();
        vm.IsTracking.Should().BeTrue();
        vm.AtHome.Should().BeTrue();
        vm.AtPark.Should().BeFalse();
        vm.IsPulseGuiding.Should().BeTrue();
        vm.UtcDate.Should().Be(utcDate);
        vm.StateTimeStamp.Should().Be(timeStamp);
        vm.SiteLatitude.Should().Be(51.5);
        vm.SiteLongitude.Should().Be(-0.12);
        vm.SiteElevation.Should().Be(35);
        vm.AlignmentMode.Should().Be(GreenSwampAlignmentMode.GermanPolar);
        vm.TrackingRate.Should().Be(GreenSwampDriveRate.Lunar);
        vm.TargetRightAscension.Should().Be(12.34);
        vm.TargetDeclination.Should().Be(-22.2);
        vm.GreenSwamp.Should().NotBeNull();
        vm.GreenSwamp!.MountName.Should().Be("GS Sim");
    }

    [Fact]
    public void OnConnectionStatusChanged_Faulted_SurfacesErrorMessage_AndDoesNotThrow()
    {
        var (vm, session) = CreateSut();

        session.RaiseConnectionStatusChanged(new TelescopeConnectionStatus(
            TelescopeConnectionState.Faulted, false, false, false, "Device unreachable"));

        vm.ConnectionState.Should().Be(TelescopeConnectionState.Faulted);
        vm.IsConnected.Should().BeFalse();
        vm.StatusMessage.Should().Be("Device unreachable");
    }

    [Fact]
    public void Capabilities_RefreshFromSession_WhenConnected()
    {
        var (vm, session) = CreateSut();
        session.Capabilities = new TelescopeCapabilities { CanFindHome = true, CanPark = true };

        session.RaiseConnectionStatusChanged(new TelescopeConnectionStatus(
            TelescopeConnectionState.Connected, true, false, false, null));

        vm.Capabilities.CanFindHome.Should().BeTrue();
        vm.Capabilities.CanPark.Should().BeTrue();
        vm.FindHomeCommand.CanExecute(null).Should().BeTrue();
        vm.ParkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesFromSessionEvents_AndDisposesSession()
    {
        var (vm, session) = CreateSut();

        await vm.DisposeAsync();

        session.IsDisposed.Should().BeTrue();

        // Raising events after disposal must not throw and must not mutate the disposed ViewModel
        // (no exception expected here since the session itself cleared its own subscriber list).
        var act = () => session.RaiseStateUpdated(new TelescopeState());
        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleViewModelInstances_BoundToDifferentSessions_DoNotCrossTalk()
    {
        var (vmA, sessionA) = CreateSut();
        var (vmB, sessionB) = CreateSut();

        sessionA.RaiseStateUpdated(new TelescopeState { Altitude = 1 });
        sessionB.RaiseStateUpdated(new TelescopeState { Altitude = 2 });

        vmA.Altitude.Should().Be(1);
        vmB.Altitude.Should().Be(2);
    }
}
