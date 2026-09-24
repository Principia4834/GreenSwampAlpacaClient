using GreenSwamp.Alpaca.Telescope.Abstractions;
using GreenSwamp.Alpaca.Telescope.Model.Providers;

namespace GreenSwamp.Alpaca.Telescope.Model;

/// <summary>
/// Orchestrator implementing ITelescopeSession (architecture §6.5). Owns Alpaca REST for
/// commands and fallback state, plus the GreenSwamp SignalR provider when the connected device
/// is GreenSwamp-class.
/// </summary>
internal sealed class TelescopeSession : ITelescopeSession
{
    private static readonly TimeSpan SignalRIngestionPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RestStatePollInterval = TimeSpan.FromSeconds(1);

    private readonly IAlpacaTelescopeProvider _alpacaProvider;
    private readonly TelescopeConnectionDescriptor _descriptor;
    private readonly TimeProvider _timeProvider;
    private readonly object _statusLock = new();
    private readonly object _pollLoopLock = new();
    private readonly object _signalRStateLock = new();
    private readonly object _signalRLoopLock = new();
    private readonly Func<TelescopeConnectionDescriptor, IGreenSwampSignalRProvider> _signalRProviderFactory;
    private IGreenSwampSignalRProvider? _signalRProvider;
    private CancellationTokenSource? _restPollingCts;
    private Task? _restPollingTask;
    private CancellationTokenSource? _signalRPollingCts;
    private Task? _signalRPollingTask;
    private TelescopeState? _pendingSignalRState;

    private TelescopeConnectionStatus _status = TelescopeConnectionStatus.Initial;
    private bool _disposed;

    public TelescopeSession(TelescopeConnectionDescriptor descriptor, TimeProvider timeProvider)
        : this(
            descriptor,
            timeProvider,
            new AlpacaTelescopeProvider(descriptor),
            d => new GreenSwampSignalRProvider(d))
    {
    }

    internal TelescopeSession(
        TelescopeConnectionDescriptor descriptor,
        TimeProvider timeProvider,
        IAlpacaTelescopeProvider alpacaProvider,
        Func<TelescopeConnectionDescriptor, IGreenSwampSignalRProvider> signalRProviderFactory)
    {
        InstanceId = Guid.NewGuid();
        _descriptor = descriptor;
        _timeProvider = timeProvider;
        _alpacaProvider = alpacaProvider;
        _signalRProviderFactory = signalRProviderFactory;
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
        await StopRestPollingLoopAsync().ConfigureAwait(false);

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

            if (Capabilities.IsGreenSwampClass)
            {
                _signalRProvider = _signalRProviderFactory(_descriptor);
            }

            if (_signalRProvider is not null)
            {
                _signalRProvider.StateReceived += OnSignalRStateReceived;
                _signalRProvider.Reconnecting += OnSignalRReconnecting;
                _signalRProvider.ConnectionLost += OnSignalRConnectionLost;
                await _signalRProvider.ConnectAsync(ct).ConfigureAwait(false);
                StartSignalRIngestionLoopIfNeeded();
            }

            SetStatus(new TelescopeConnectionStatus(
                TelescopeConnectionState.Connected,
                IsAlpacaRestActive: true,
                IsSignalRActive: _signalRProvider is not null,
                IsSignalRDegraded: false,
                ErrorMessage: null));

            PublishStateFromRest();

            if (_signalRProvider is null)
            {
                StartRestPollingLoopIfNeeded();
            }
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
            await StopRestPollingLoopAsync().ConfigureAwait(false);
            await StopSignalRIngestionLoopAsync().ConfigureAwait(false);

            if (_signalRProvider is not null)
            {
                await _signalRProvider.DisconnectAsync(ct).ConfigureAwait(false);
            }

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
            PublishStateFromRest();
        }
        catch (Exception ex)
        {
            SetStatus(Status with { ErrorMessage = ex.Message });
        }
    }

    /// <summary>Reads the current DeviceState snapshot and raises StateUpdated (architecture §6.5/§6.6).</summary>
    private void PublishStateFromRest()
    {
        try
        {
            var state = _alpacaProvider.GetState(_timeProvider);
            SetStatus(Status with { IsAlpacaRestActive = true, ErrorMessage = null });
            StateUpdated?.Invoke(this, new TelescopeStateUpdatedEventArgs(state));
        }
        catch (Exception ex)
        {
            SetStatus(Status with { IsAlpacaRestActive = false, ErrorMessage = ex.Message });
        }
    }

    private void OnSignalRStateReceived(object? sender, GreenSwampStateReceivedEventArgs e)
    {
        lock (_signalRStateLock)
        {
            _pendingSignalRState = e.State;
        }
    }

    private void OnSignalRReconnecting(object? sender, EventArgs e)
    {
        SetStatus(Status with { IsSignalRActive = false, IsSignalRDegraded = true });
    }

    private void OnSignalRConnectionLost(object? sender, EventArgs e)
    {
        SetStatus(Status with { IsSignalRActive = false, IsSignalRDegraded = true });
        _ = StopSignalRIngestionLoopAsync();
        StartRestPollingLoopIfNeeded();
    }

    private void StartSignalRIngestionLoopIfNeeded()
    {
        lock (_signalRLoopLock)
        {
            if (_signalRPollingTask is { IsCompleted: false })
            {
                return;
            }

            _signalRPollingCts = new CancellationTokenSource();
            _signalRPollingTask = RunSignalRIngestionLoopAsync(_signalRPollingCts.Token);
        }
    }

    private async Task StopSignalRIngestionLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? pollTask;

        lock (_signalRLoopLock)
        {
            cts = _signalRPollingCts;
            pollTask = _signalRPollingTask;
            _signalRPollingCts = null;
            _signalRPollingTask = null;
        }

        if (cts is null || pollTask is null)
        {
            return;
        }

        cts.Cancel();

        try
        {
            await pollTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void StartRestPollingLoopIfNeeded()
    {
        lock (_pollLoopLock)
        {
            if (_restPollingTask is { IsCompleted: false })
            {
                return;
            }

            _restPollingCts = new CancellationTokenSource();
            _restPollingTask = RunRestPollingLoopAsync(_restPollingCts.Token);
        }
    }

    private async Task StopRestPollingLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? pollTask;

        lock (_pollLoopLock)
        {
            cts = _restPollingCts;
            pollTask = _restPollingTask;
            _restPollingCts = null;
            _restPollingTask = null;
        }

        if (cts is null || pollTask is null)
        {
            return;
        }

        cts.Cancel();

        try
        {
            await pollTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task RunRestPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PublishStateFromRest();

            try
            {
                await Task.Delay(RestStatePollInterval, _timeProvider, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunSignalRIngestionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TelescopeState? stateToPublish = null;
            lock (_signalRStateLock)
            {
                if (_pendingSignalRState is not null)
                {
                    stateToPublish = _pendingSignalRState;
                    _pendingSignalRState = null;
                }
            }

            if (stateToPublish is not null)
            {
                StateUpdated?.Invoke(this, new TelescopeStateUpdatedEventArgs(stateToPublish));
            }

            try
            {
                await Task.Delay(SignalRIngestionPollInterval, _timeProvider, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void SetStatus(TelescopeConnectionStatus status)
    {
        lock (_statusLock)
        {
            if (_status == status)
            {
                return;
            }

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
        await StopRestPollingLoopAsync().ConfigureAwait(false);
        await StopSignalRIngestionLoopAsync().ConfigureAwait(false);

        if (_signalRProvider is not null)
        {
            _signalRProvider.StateReceived -= OnSignalRStateReceived;
            _signalRProvider.Reconnecting -= OnSignalRReconnecting;
            _signalRProvider.ConnectionLost -= OnSignalRConnectionLost;
            await _signalRProvider.DisposeAsync().ConfigureAwait(false);
        }

        StateUpdated = null;
        ConnectionStatusChanged = null;
        await _alpacaProvider.DisposeAsync().ConfigureAwait(false);
    }
}
