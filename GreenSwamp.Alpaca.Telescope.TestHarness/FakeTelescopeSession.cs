using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Telescope.TestHarness;

/// <summary>
/// In-memory fake ITelescopeSession, used exclusively by Level 2 ViewModel unit tests (test
/// strategy §5.2/§6, item 5: "FakeTelescopeAbstraction"). No transport, no I/O, no background
/// threads - tests drive it directly by calling <see cref="RaiseStateUpdated"/>/
/// <see cref="RaiseConnectionStatusChanged"/> and by inspecting <see cref="ConnectCallCount"/> etc.
/// </summary>
public sealed class FakeTelescopeSession : ITelescopeSession
{
    public int ConnectCallCount { get; private set; }
    public int DisconnectCallCount { get; private set; }
    public int FindHomeCallCount { get; private set; }
    public int ParkCallCount { get; private set; }
    public int AbortSlewCallCount { get; private set; }
    public bool IsDisposed { get; private set; }

    /// <summary>When set, the corresponding command method throws this instead of succeeding - simulates a device-rejected command (implementation design §6, FR44-45).</summary>
    public Exception? FindHomeException { get; set; }
    public Exception? ParkException { get; set; }
    public Exception? AbortSlewException { get; set; }
    public Exception? ConnectException { get; set; }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public TelescopeCapabilities Capabilities { get; set; } = new();

    public TelescopeConnectionStatus Status { get; private set; } = TelescopeConnectionStatus.Initial;

    public event EventHandler<TelescopeStateUpdatedEventArgs>? StateUpdated;
    public event EventHandler<TelescopeConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ConnectCallCount++;
        if (ConnectException is not null)
        {
            throw ConnectException;
        }

        RaiseConnectionStatusChanged(new TelescopeConnectionStatus(TelescopeConnectionState.Connected, true, false, false, null));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        DisconnectCallCount++;
        RaiseConnectionStatusChanged(TelescopeConnectionStatus.Initial);
        return Task.CompletedTask;
    }

    public Task FindHome(CancellationToken ct = default)
    {
        FindHomeCallCount++;
        return FindHomeException is not null ? Task.FromException(FindHomeException) : Task.CompletedTask;
    }

    public Task ParkAsync(CancellationToken ct = default)
    {
        ParkCallCount++;
        return ParkException is not null ? Task.FromException(ParkException) : Task.CompletedTask;
    }

    public Task AbortSlewAsync(CancellationToken ct = default)
    {
        AbortSlewCallCount++;
        return AbortSlewException is not null ? Task.FromException(AbortSlewException) : Task.CompletedTask;
    }

    /// <summary>Test-only hook: raises StateUpdated as if a real provider had produced a new snapshot.</summary>
    public void RaiseStateUpdated(TelescopeState state) =>
        StateUpdated?.Invoke(this, new TelescopeStateUpdatedEventArgs(state));

    /// <summary>Test-only hook: raises ConnectionStatusChanged as if a real provider had transitioned state.</summary>
    public void RaiseConnectionStatusChanged(TelescopeConnectionStatus status)
    {
        Status = status;
        ConnectionStatusChanged?.Invoke(this, new TelescopeConnectionStatusChangedEventArgs(status));
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        StateUpdated = null;
        ConnectionStatusChanged = null;
        return ValueTask.CompletedTask;
    }
}
