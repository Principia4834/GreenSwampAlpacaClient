using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;

namespace GreenSwamp.Alpaca.Telescope.Model;

/// <summary>
/// Orchestrator implementing ITelescopeSession (architecture §6.5). For this implementation
/// slice, owns only an AlpacaTelescopeProvider - the GreenSwamp SignalR provider is not yet
/// implemented (gated behind the future TelescopeStateHub, architecture §6.4), so reconciliation
/// between REST and SignalR sources does not yet apply; all state comes from Alpaca REST.
/// </summary>
internal sealed class TelescopeSession : ITelescopeSession
{
    private readonly AlpacaTelescopeProvider _alpacaProvider;
    private readonly TimeProvider _timeProvider;
    private readonly object _statusLock = new();

    private TelescopeConnectionStatus _status = TelescopeConnectionStatus.Initial;
    private bool _disposed;

    public TelescopeSession(TelescopeConnectionDescriptor descriptor, TimeProvider timeProvider)
    {
        InstanceId = Guid.NewGuid();
        _timeProvider = timeProvider;
        _alpacaProvider = new AlpacaTelescopeProvider(descriptor);
        Capabilities = new TelescopeCapabilities();
    }

    public Guid InstanceId { get; }

    public TelescopeCapabilities Capabilities { get; private set; }

    public TelescopeConnectionStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    public event EventHandler<TelescopeStateUpdatedEventArgs>? StateUpdated;
    public event EventHandler<TelescopeConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        SetStatus(new TelescopeConnectionStatus(
            TelescopeConnectionState.Connecting,
            IsAlpacaRestActive: false,
            IsSignalRActive: false,
            IsSignalRDegraded: false,
            ErrorMessage: null));

        try
        {
            await _alpacaProvider.ConnectAsync(ct).ConfigureAwait(false);
            Capabilities = _alpacaProvider.GetCapabilities();

            SetStatus(new TelescopeConnectionStatus(
                TelescopeConnectionState.Connected,
                IsAlpacaRestActive: true,
                IsSignalRActive: false,
                IsSignalRDegraded: false,
                ErrorMessage: null));

            PublishState();
        }
        catch (Exception ex)
        {
            // Connection failures are surfaced via ConnectionStatusChanged, not as raw exceptions
            // to the ViewModel (requirements FR44-45, implementation design §6.1).
            SetStatus(new TelescopeConnectionStatus(
                TelescopeConnectionState.Faulted,
                IsAlpacaRestActive: false,
                IsSignalRActive: false,
                IsSignalRDegraded: false,
                ErrorMessage: ex.Message));
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        SetStatus(Status with { State = TelescopeConnectionState.Disconnecting });

        try
        {
            await _alpacaProvider.DisconnectAsync(ct).ConfigureAwait(false);
            SetStatus(TelescopeConnectionStatus.Initial);
        }
        catch (Exception ex)
        {
            SetStatus(new TelescopeConnectionStatus(
                TelescopeConnectionState.Faulted,
                IsAlpacaRestActive: false,
                IsSignalRActive: false,
                IsSignalRDegraded: false,
                ErrorMessage: ex.Message));
        }
    }

    public Task FindHome(CancellationToken ct = default) =>
        ExecuteCommandAsync(() => _alpacaProvider.FindHome(ct));

    public Task ParkAsync(CancellationToken ct = default) =>
        ExecuteCommandAsync(() => _alpacaProvider.ParkAsync(ct));

    public Task AbortSlewAsync(CancellationToken ct = default) =>
        ExecuteCommandAsync(() => _alpacaProvider.AbortSlewAsync(ct));

    /// <summary>
    /// Executes a device command with no client-side precondition checking beyond the caller's
    /// own capability gate (implementation design §6, items 3/4): any exception the device raises
    /// is caught here, once, and surfaced as an application-facing error via ConnectionStatusChanged
    /// rather than propagating a raw/unhandled exception. On success, the resulting state change is
    /// published immediately rather than waiting for the next poll tick.
    /// </summary>
    private async Task ExecuteCommandAsync(Func<Task> command)
    {
        try
        {
            await command().ConfigureAwait(false);
            PublishState();
        }
        catch (Exception ex)
        {
            SetStatus(Status with { ErrorMessage = ex.Message });
        }
    }

    /// <summary>Reads the current DeviceState snapshot and raises StateUpdated (architecture §6.5/§6.6).</summary>
    private void PublishState()
    {
        var state = _alpacaProvider.GetState(_timeProvider);
        StateUpdated?.Invoke(this, new TelescopeStateUpdatedEventArgs(state));
    }

    private void SetStatus(TelescopeConnectionStatus status)
    {
        lock (_statusLock)
        {
            _status = status;
        }

        ConnectionStatusChanged?.Invoke(this, new TelescopeConnectionStatusChangedEventArgs(status));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StateUpdated = null;
        ConnectionStatusChanged = null;
        await _alpacaProvider.DisposeAsync().ConfigureAwait(false);
    }
}
