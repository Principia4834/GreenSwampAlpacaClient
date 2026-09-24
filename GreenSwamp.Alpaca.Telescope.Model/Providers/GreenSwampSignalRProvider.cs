using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Serialization;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using AppTelescopeState = GreenSwamp.Alpaca.Telescope.Abstractions.TelescopeState;

namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Wraps Microsoft.AspNetCore.SignalR.Client.HubConnection - the GreenSwamp-class telescope
/// state transport (SignalR-transport-implementation-plan-final.md §7 Phase 1), internal to
/// GreenSwamp.Alpaca.Telescope.Model alongside AlpacaTelescopeProvider (same assembly, same
/// visibility pattern). Connects to the server's TelescopeStateHub
/// (greenswamp-telescopestate-signalr-spec.md: route "/telescopestatehub", group
/// "TelescopeState-{deviceNumber}", broadcast method "ReceiveTelescopeState") and hands each
/// received snapshot to TelescopeSession via <see cref="StateReceived"/> - this provider does not
/// decide REST/SignalR precedence itself; that is TelescopeSession's job (§5).
/// </summary>
internal interface IGreenSwampSignalRProvider : IAsyncDisposable
{
    event EventHandler<GreenSwampStateReceivedEventArgs>? StateReceived;
    event EventHandler? Reconnecting;
    event EventHandler? ConnectionLost;

    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}

internal sealed class GreenSwampSignalRProvider : IGreenSwampSignalRProvider
{
    /// <summary>WithAutomaticReconnect's built-in schedule (§5/§7 Phase 1): 2, 5, 10, 10, 10 seconds - 5 retries total.</summary>
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10)
    ];

    private readonly HubConnection _connection;
    private readonly int _deviceNumber;
    private readonly ILogger<GreenSwampSignalRProvider> _logger;
    private bool _disposed;
    private bool _fallbackEngaged;

    public GreenSwampSignalRProvider(TelescopeConnectionDescriptor descriptor)
        : this(descriptor, NullLogger<GreenSwampSignalRProvider>.Instance)
    {
    }

    internal GreenSwampSignalRProvider(TelescopeConnectionDescriptor descriptor, ILogger<GreenSwampSignalRProvider> logger)
    {
        _deviceNumber = descriptor.AlpacaDeviceNumber;
        _logger = logger;

        _logger.LogTrace("SignalR device number set to {DeviceNumber}", _deviceNumber);

        var hubUrl = $"http://{descriptor.HostName}:{descriptor.Port}/telescopestatehub";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(ReconnectDelays)
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                o.PayloadSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            })
            .Build();

        _connection.On<GreenSwampTelescopeStatePayload>("ReceiveTelescopeState", OnReceiveTelescopeState);

        // Group membership does not survive an automatic reconnect (§7 Phase 1) - must be
        // re-issued every time the connection transitions back to Connected.
        _connection.Reconnected += _ => JoinGroupAsync(CancellationToken.None);

        _connection.Reconnecting += _ =>
        {
            Reconnecting?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };

        _connection.Closed += _ =>
        {
            if (_fallbackEngaged)
            {
                return Task.CompletedTask;
            }

            // WithAutomaticReconnect only fires Closed once its own retry schedule is exhausted
            // (irrecoverable loss, §5) - signal the session to hand control to REST.
            _fallbackEngaged = true;
            ConnectionLost?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };
    }

    /// <summary>Raised for each successfully deserialized state push, already mapped to the neutral snapshot pair.</summary>
    public event EventHandler<GreenSwampStateReceivedEventArgs>? StateReceived;

    /// <summary>Raised when the connection drops and an automatic-reconnect attempt begins (§7 Phase 3 - degraded, not faulted).</summary>
    public event EventHandler? Reconnecting;

    /// <summary>Raised only once the 5-retry/2-5-10-10-10s schedule is exhausted without success (§5 - irrecoverable loss).</summary>
    public event EventHandler? ConnectionLost;

    /// <summary>
    /// Connects and joins this device's group (§7 Phase 1). No historical replay: callers must
    /// tolerate a brief "no data yet" window until the next ~250ms server tick before the first
    /// StateReceived is raised - this is expected, not a fault.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        _logger.LogTrace("Starting SignalR connection for device {DeviceNumber}", _deviceNumber);
        await _connection.StartAsync(ct).ConfigureAwait(false);
        _logger.LogTrace("SignalR connection started for device {DeviceNumber}", _deviceNumber);
        await JoinGroupAsync(ct).ConfigureAwait(false);
        _logger.LogTrace("Join ack received for device {DeviceNumber}", _deviceNumber);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (_connection.State != HubConnectionState.Disconnected)
        {
            await LeaveGroupAsync(ct).ConfigureAwait(false);
        }

        _fallbackEngaged = true;
        await _connection.StopAsync(ct).ConfigureAwait(false);
    }

    private Task JoinGroupAsync(CancellationToken ct)
    {
        _logger.LogTrace("Sending join request for device {DeviceNumber}", _deviceNumber);
        return _connection.InvokeAsync("JoinTelescopeStateGroupAsync", _deviceNumber, ct);
    }

    private Task LeaveGroupAsync(CancellationToken ct)
    {
        _logger.LogTrace("Sending leave request for device {DeviceNumber}", _deviceNumber);
        return _connection.InvokeAsync("LeaveTelescopeStateGroupAsync", _deviceNumber, ct);
    }

    private void OnReceiveTelescopeState(GreenSwampTelescopeStatePayload payload)
    {
        _logger.LogTrace("Received ReceiveTelescopeState payload for device {DeviceNumber}", _deviceNumber);
        AppTelescopeState state = GreenSwampTelescopeStatePayloadMapper.ToTelescopeState(payload, DateTimeOffset.UtcNow);
        StateReceived?.Invoke(this, new GreenSwampStateReceivedEventArgs(state));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StateReceived = null;
        Reconnecting = null;
        ConnectionLost = null;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Payload for GreenSwampSignalRProvider.StateReceived.</summary>
internal sealed class GreenSwampStateReceivedEventArgs(AppTelescopeState state) : EventArgs
{
    public AppTelescopeState State { get; } = state;
}
