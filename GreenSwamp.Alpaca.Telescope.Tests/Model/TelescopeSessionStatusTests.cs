using FluentAssertions;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model;
using GreenSwamp.Alpaca.Telescope.Model.Providers;
using Microsoft.Extensions.Time.Testing;

namespace GreenSwamp.Alpaca.Telescope.Tests.Model;

public sealed class TelescopeSessionStatusTests
{
    private static readonly TelescopeConnectionDescriptor Descriptor = new("localhost", 11111, 0);

    [Fact]
    public async Task AlpacaClass_DeviceStateLoop_RunsAtOneSecondCadence()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var alpaca = new FakeAlpacaProvider(isGreenSwampClass: false);
        await using var session = new TelescopeSession(Descriptor, timeProvider, alpaca, _ => new FakeSignalRProvider());

        await session.ConnectAsync();
        var baselineCalls = alpaca.GetStateCallCount;

        timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Delay(10);
        alpaca.GetStateCallCount.Should().Be(baselineCalls);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForAsync(() => alpaca.GetStateCallCount == baselineCalls + 1);
    }

    [Fact]
    public async Task GreenSwampClass_Reconnecting_DoesNotEngageFallbackLoop()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var alpaca = new FakeAlpacaProvider(isGreenSwampClass: true);
        var signalR = new FakeSignalRProvider();
        await using var session = new TelescopeSession(Descriptor, timeProvider, alpaca, _ => signalR);

        await session.ConnectAsync();
        var callsBeforeReconnecting = alpaca.GetStateCallCount;

        signalR.RaiseReconnecting();
        await WaitForAsync(() => session.Status.IsSignalRDegraded);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(10);
        alpaca.GetStateCallCount.Should().Be(callsBeforeReconnecting);
        session.Status.IsSignalRActive.Should().BeFalse();
    }

    [Fact]
    public async Task GreenSwampClass_ConnectionLost_EngagesRestFallbackPollLoop()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var alpaca = new FakeAlpacaProvider(isGreenSwampClass: true);
        var signalR = new FakeSignalRProvider();
        await using var session = new TelescopeSession(Descriptor, timeProvider, alpaca, _ => signalR);

        await session.ConnectAsync();
        var callsBeforeLoss = alpaca.GetStateCallCount;

        signalR.RaiseConnectionLost();
        await WaitForAsync(() => session.Status.IsSignalRDegraded);

        var callsAfterLoss = alpaca.GetStateCallCount;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => alpaca.GetStateCallCount > callsAfterLoss);

        session.Status.IsSignalRActive.Should().BeFalse();
        session.Status.IsSignalRDegraded.Should().BeTrue();
        session.Status.IsAlpacaRestActive.Should().BeTrue();
    }

    [Fact]
    public async Task GreenSwampClass_SignalRIngestionLoop_PublishesAt250MillisecondCadence()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var alpaca = new FakeAlpacaProvider(isGreenSwampClass: true);
        var signalR = new FakeSignalRProvider();
        await using var session = new TelescopeSession(Descriptor, timeProvider, alpaca, _ => signalR);
        var published = new List<TelescopeState>();
        session.StateUpdated += (_, e) => published.Add(e.State);

        await session.ConnectAsync();
        published.Clear(); // Ignore the initial REST publish at connect.

        signalR.RaiseState(new TelescopeState { Altitude = 12, TimeStamp = timeProvider.GetUtcNow() });
        await Task.Delay(10);
        published.Should().BeEmpty();

        timeProvider.Advance(TimeSpan.FromMilliseconds(249));
        await Task.Delay(10);
        published.Should().BeEmpty();

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForAsync(() => published.Count == 1);
        published[0].Altitude.Should().Be(12);
    }

    [Fact]
    public async Task RestPolling_UpdatesIsAlpacaRestActive_WhenReachabilityChanges()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var alpaca = new FakeAlpacaProvider(isGreenSwampClass: false);
        await using var session = new TelescopeSession(Descriptor, timeProvider, alpaca, _ => new FakeSignalRProvider());

        await session.ConnectAsync();
        session.Status.IsAlpacaRestActive.Should().BeTrue();

        alpaca.GetStateException = new InvalidOperationException("REST unavailable");
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => !session.Status.IsAlpacaRestActive);

        alpaca.GetStateException = null;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => session.Status.IsAlpacaRestActive);
    }

    [Fact]
    public async Task ConnectionLost_EngagesFallbackLoop_OnlyOnce()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var alpaca = new FakeAlpacaProvider(isGreenSwampClass: true);
        var signalR = new FakeSignalRProvider();
        await using var session = new TelescopeSession(Descriptor, timeProvider, alpaca, _ => signalR);

        await session.ConnectAsync();
        signalR.RaiseConnectionLost();
        await WaitForAsync(() => session.Status.IsSignalRDegraded);

        var countAfterFirstLoss = alpaca.GetStateCallCount;
        signalR.RaiseConnectionLost();
        timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Delay(10);
        alpaca.GetStateCallCount.Should().Be(countAfterFirstLoss);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        condition().Should().BeTrue("the expected condition should be reached within timeout");
    }

    private sealed class FakeSignalRProvider : IGreenSwampSignalRProvider
    {
        public event EventHandler<GreenSwampStateReceivedEventArgs>? StateReceived;
        public event EventHandler? Reconnecting;
        public event EventHandler? ConnectionLost;

        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;

        public void RaiseReconnecting() => Reconnecting?.Invoke(this, EventArgs.Empty);
        public void RaiseConnectionLost() => ConnectionLost?.Invoke(this, EventArgs.Empty);
        public void RaiseState(TelescopeState state) => StateReceived?.Invoke(this, new GreenSwampStateReceivedEventArgs(state));

        public ValueTask DisposeAsync()
        {
            StateReceived = null;
            Reconnecting = null;
            ConnectionLost = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAlpacaProvider(bool isGreenSwampClass) : IAlpacaTelescopeProvider
    {
        public int GetStateCallCount { get; private set; }
        public Exception? GetStateException { get; set; }
        public List<DateTimeOffset> GetStateCallTimes { get; } = [];

        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task FindHome(CancellationToken ct) => Task.CompletedTask;
        public Task ParkAsync(CancellationToken ct) => Task.CompletedTask;
        public Task AbortSlewAsync(CancellationToken ct) => Task.CompletedTask;

        public TelescopeState GetState(TimeProvider timeProvider)
        {
            GetStateCallCount++;
            GetStateCallTimes.Add(timeProvider.GetUtcNow());
            if (GetStateException is not null)
            {
                throw GetStateException;
            }

            return new TelescopeState
            {
                TimeStamp = timeProvider.GetUtcNow(),
                Slewing = false
            };
        }

        public TelescopeCapabilities GetCapabilities() => new()
        {
            IsGreenSwampClass = isGreenSwampClass
        };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
