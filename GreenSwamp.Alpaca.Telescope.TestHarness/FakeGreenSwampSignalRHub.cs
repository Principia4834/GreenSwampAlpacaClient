using System.Collections.Concurrent;
using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Telescope.TestHarness;

/// <summary>
/// In-memory fake SignalR hub for GreenSwamp telescope state tests.
/// Supports group join/leave, broadcast fan-out, and simple connection lifecycle assertions.
/// </summary>
public sealed class FakeGreenSwampSignalRHub
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, FakeGreenSwampSignalRClient>> _groups = new();

    public int ConnectCallCount { get; private set; }
    public int DisconnectCallCount { get; private set; }
    public int JoinCallCount { get; private set; }
    public int LeaveCallCount { get; private set; }

    public FakeGreenSwampSignalRClient Connect(int deviceNumber)
    {
        ConnectCallCount++;
        return new FakeGreenSwampSignalRClient(this, deviceNumber);
    }

    public FakeGreenSwampSignalRProviderConnection CreateProviderConnection(int deviceNumber)
    {
        ConnectCallCount++;
        return new FakeGreenSwampSignalRProviderConnection(this, deviceNumber);
    }

    internal void Disconnect(FakeGreenSwampSignalRClient client)
    {
        DisconnectCallCount++;
        LeaveGroup(client);
    }

    internal void JoinGroup(FakeGreenSwampSignalRClient client)
    {
        JoinCallCount++;
        var group = _groups.GetOrAdd(client.DeviceNumber, _ => new ConcurrentDictionary<Guid, FakeGreenSwampSignalRClient>());
        group[client.ClientId] = client;
    }

    internal void LeaveGroup(FakeGreenSwampSignalRClient client)
    {
        LeaveCallCount++;
        if (_groups.TryGetValue(client.DeviceNumber, out var group))
        {
            group.TryRemove(client.ClientId, out _);
        }
    }

    public void Broadcast(int deviceNumber, TelescopeState state)
    {
        if (!_groups.TryGetValue(deviceNumber, out var group))
        {
            return;
        }

        foreach (var client in group.Values)
        {
            client.Receive(state);
        }
    }
}

/// <summary>
/// Lightweight fake client used by the fake hub to model per-connection group membership.
/// </summary>
public sealed class FakeGreenSwampSignalRClient
{
    private readonly FakeGreenSwampSignalRHub _hub;
    private bool _connected;

    internal FakeGreenSwampSignalRClient(FakeGreenSwampSignalRHub hub, int deviceNumber)
    {
        _hub = hub;
        DeviceNumber = deviceNumber;
        ClientId = Guid.NewGuid();
    }

    public Guid ClientId { get; }
    public int DeviceNumber { get; }
    public int ReceivedCount { get; private set; }
    public TelescopeState? LastState { get; private set; }

    public void Start()
    {
        _connected = true;
        _hub.JoinGroup(this);
    }

    public void Stop()
    {
        if (!_connected)
        {
            return;
        }

        _connected = false;
        _hub.Disconnect(this);
    }

    public void JoinGroup() => _hub.JoinGroup(this);

    public void LeaveGroup() => _hub.LeaveGroup(this);

    public void Receive(TelescopeState state)
    {
        ReceivedCount++;
        LastState = state;
    }
}

/// <summary>
/// Provider-style wrapper used by lifecycle tests to model join/rejoin/leave/disconnect behavior.
/// </summary>
public sealed class FakeGreenSwampSignalRProviderConnection
{
    private readonly FakeGreenSwampSignalRClient _client;

    internal FakeGreenSwampSignalRProviderConnection(FakeGreenSwampSignalRHub hub, int deviceNumber)
    {
        _client = new FakeGreenSwampSignalRClient(hub, deviceNumber);
    }

    public int DeviceNumber => _client.DeviceNumber;
    public Guid ClientId => _client.ClientId;
    public int ReceivedCount => _client.ReceivedCount;
    public TelescopeState? LastState => _client.LastState;

    public void Connect() => _client.Start();
    public void Rejoin() => _client.JoinGroup();
    public void Leave() => _client.LeaveGroup();
    public void Disconnect() => _client.Stop();
    public void Receive(TelescopeState state) => _client.Receive(state);
}
