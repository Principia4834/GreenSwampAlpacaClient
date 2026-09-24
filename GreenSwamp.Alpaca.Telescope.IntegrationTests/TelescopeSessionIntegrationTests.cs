using System;
using System.Threading.Tasks;
using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model;
using Microsoft.Extensions.DependencyInjection;

namespace GreenSwamp.Alpaca.Telescope.IntegrationTests;

/// <summary>
/// Level 3 (test strategy §5.3): AlpacaTelescopeProvider/TelescopeSession against a real,
/// already-running GreenSwampAlpacaServer instance (device 0, a GEM simulator) at
/// localhost:31416. This server instance is started/stopped manually by the developer for now -
/// no Process Lifecycle Manager exists yet (test strategy §6, item 1 - deferred).
///
/// Manual test precondition (documented, not automated - ITelescopeSession does not yet expose
/// Unpark): before running this class, the device must be unparked, tracking, and slewed to a
/// non-trivial position (e.g. via the server's own Blazor UI). The simulator rejects
/// FindHome/AbortSlew/Park with "Telescope parked" while parked - a real, confirmed device
/// behavior, not a test bug. ALWAYS ASK THE USER TO CONFIRM THIS PRECONDITION IS SET BEFORE
/// RUNNING THIS TEST CLASS - do not assume the device is in the right state.
///
/// All facts run as a single ordered scenario against one shared, real, stateful server/session
/// (not independent/parallelizable facts) precisely because Park changes shared device state:
/// Park is deliberately exercised last, after every other in-scope command has already been
/// verified, so it cannot poison later assertions the way running facts in an undefined xUnit
/// order previously did (confirmed by observation this run).
///
/// Scope: exercises exactly the four in-scope commands (implementation design §6): Connect,
/// FindHome, Park, Abort. GreenSwamp-class detection/GreenSwampTelescopeState population is not
/// exercised here - not yet consumed by anything in this implementation slice.
/// </summary>
public class TelescopeSessionIntegrationTests : IAsyncLifetime
{
    private static readonly TelescopeConnectionDescriptor Descriptor = new(
        HostName: "localhost",
        Port: 31416,
        AlpacaDeviceNumber: 0);

    private ITelescopeSession _session = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddTelescopeIntegration();
        var factory = services.BuildServiceProvider().GetRequiredService<ITelescopeSessionFactory>();
        _session = factory.Create(Descriptor);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task ConnectFindHomeAbortParkDisconnect_FullScenario_AgainstRealServer()
    {
        // 1. Connect
        await _session.ConnectAsync();
        _session.Status.State.Should().Be(TelescopeConnectionState.Connected);
        _session.Status.ErrorMessage.Should().BeNull();
        _session.Status.IsAlpacaRestActive.Should().BeTrue();
        _session.Capabilities.Should().NotBeNull();

        // 2. FindHome (only if the real server reports support for it)
        if (_session.Capabilities.CanFindHome)
        {
            await _session.FindHome();
            _session.Status.ErrorMessage.Should().BeNull();
        }

        // 3. AbortSlew - no Can* gate in the standard interface; always passed through
        TelescopeState? published = null;
        _session.StateUpdated += (_, e) => published = e.State;

        await _session.AbortSlewAsync();
        _session.Status.ErrorMessage.Should().BeNull("AbortSlew has no Can* gate and the device is unparked per the documented test precondition");
        published.Should().NotBeNull("a successful command publishes a fresh state snapshot");

        // 4. Park - deliberately last: this changes shared device state for any test run after this one
        if (_session.Capabilities.CanPark)
        {
            await _session.ParkAsync();
            _session.Status.ErrorMessage.Should().BeNull();
        }

        // 5. Disconnect
        await _session.DisconnectAsync();
        _session.Status.State.Should().Be(TelescopeConnectionState.Disconnected);
    }

    [Fact]
    public async Task GreenSwampSignalR_ConnectsAndRaisesSignalRActive_Status()
    {
        var connectedTcs = new TaskCompletionSource<TelescopeConnectionStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<TelescopeConnectionStatusChangedEventArgs> handler = (_, e) =>
        {
            if (e.Status.State == TelescopeConnectionState.Connected && e.Status.IsSignalRActive)
            {
                connectedTcs.TrySetResult(e.Status);
            }
        };

        _session.ConnectionStatusChanged += handler;
        await _session.ConnectAsync();

        var connectedStatus = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        _session.ConnectionStatusChanged -= handler;

        connectedStatus.IsAlpacaRestActive.Should().BeTrue();
        connectedStatus.IsSignalRActive.Should().BeTrue();
        connectedStatus.IsSignalRDegraded.Should().BeFalse();
        _session.Status.Should().Be(connectedStatus);
    }

    [Fact]
    public async Task GreenSwampSignalR_FullyPopulatesSnapshotFields_FromLiveServer()
    {
        await _session.ConnectAsync();

        _session.Status.IsAlpacaRestActive.Should().BeTrue();
        _session.Status.IsSignalRActive.Should().BeTrue();
        _session.Status.IsSignalRDegraded.Should().BeFalse();

        var snapshotTcs = new TaskCompletionSource<TelescopeState>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<TelescopeStateUpdatedEventArgs> handler = (_, e) =>
        {
            if (e.State.GreenSwamp is not null)
            {
                snapshotTcs.TrySetResult(e.State);
            }
        };

        _session.StateUpdated += handler;

        var snapshot = await snapshotTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

        _session.StateUpdated -= handler;

        snapshot.GreenSwamp.Should().NotBeNull("GreenSwamp-class devices should populate the GreenSwamp extension snapshot via SignalR");
        snapshot.SiteLatitude.Should().NotBe(0);
        snapshot.AlignmentMode.Should().NotBe(default);
        // TrackingRate is not asserted against "not default" - GreenSwampDriveRate.Sidereal (0) is
        // both the enum's default value and a legitimate real-world tracking rate, so that
        // assertion can never hold for a truthful payload while the mount tracks sidereally.
        // Assert only that the field deserialized to a defined enum member.
        Enum.IsDefined(snapshot.TrackingRate).Should().BeTrue();
        snapshot.TargetRightAscension.Should().NotBe(0);
        snapshot.TargetDeclination.Should().NotBe(0);
        snapshot.TimeStamp.Should().NotBe(default);
        snapshot.GreenSwamp!.MountName.Should().NotBeNullOrWhiteSpace();
        // MountType is not asserted against "not default" - GreenSwampMountType.Simulator (0) is
        // both the enum's default value and this test's real device (device 0 is documented as a
        // GEM simulator, see class remarks), so that assertion can never hold truthfully here.
        // Assert only that the field deserialized to a defined enum member.
        Enum.IsDefined(snapshot.GreenSwamp.MountType).Should().BeTrue();
    }
}
