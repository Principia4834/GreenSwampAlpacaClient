using ASCOM.Alpaca.Clients;
using ASCOM.Common;
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Telescope.Abstractions;
using AscomTelescopeState = ASCOM.Common.DeviceStateClasses.TelescopeState;

namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Wraps ASCOM.Alpaca.Clients.AlpacaTelescope (ITelescopeV4) - the always-present Alpaca REST
/// transport for a telescope session (architecture §6.3). Does not hand-roll HTTP/JSON; all wire
/// protocol details are owned by ASCOM.Alpaca.Components.
///
/// Known GreenSwamp-class detection mechanism for this slice (see implementation design/architecture
/// §6.8, exact mechanism confirmed TBD-then-resolved for this pass): recognizes the per-device
/// Description/DriverInfo strings reported by the connected device itself, avoiding an extra
/// server-level management-API round-trip. A future revision may add the server-level
/// management/v1/description check as a stronger/earlier signal (e.g. for discovery, §6.10) - not
/// required for this slice since Connect already retrieves Description for free.
/// </summary>
internal sealed class AlpacaTelescopeProvider : IAsyncDisposable
{
    private readonly AlpacaTelescope _client;

    public AlpacaTelescopeProvider(TelescopeConnectionDescriptor descriptor)
    {
        _client = new AlpacaTelescope(new AlpacaConfiguration
        {
            IpAddressString = descriptor.HostName,
            PortNumber = descriptor.Port,
            RemoteDeviceNumber = descriptor.AlpacaDeviceNumber
        });
    }

    /// <summary>
    /// Connects via ASCOM.Common.ClientExtensions.ConnectAsync (implementation design §6.1):
    /// handles both Platform 6 and Platform 7 connect semantics, polling until fully connected.
    /// </summary>
    public Task ConnectAsync(CancellationToken ct) =>
        _client.ConnectAsync(DeviceTypes.Telescope, _client.InterfaceVersion, ct);

    public Task DisconnectAsync(CancellationToken ct) =>
        _client.DisconnectAsync(DeviceTypes.Telescope, _client.InterfaceVersion, ct);

    /// <summary>Deliberately named without an Async suffix - see ITelescopeSession.FindHome remarks.</summary>
    public Task FindHome(CancellationToken ct) => _client.FindHomeAsync(ct);

    public Task ParkAsync(CancellationToken ct) => _client.ParkAsync(ct);

    public Task AbortSlewAsync(CancellationToken ct) => _client.AbortSlewAsync(ct);

    /// <summary>
    /// Reads the standard Alpaca DeviceState snapshot in one round-trip (implementation design
    /// §4.1) and maps it to the neutral TelescopeState (§4.2+/GreenSwampTelescopeState population
    /// deferred to a later slice).
    /// </summary>
    public Abstractions.TelescopeState GetState(TimeProvider timeProvider)
    {
        var deviceState = new AscomTelescopeState(_client.DeviceState, TL: null);
        return TelescopeStateMapper.ToTelescopeState(deviceState, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Reads standard Can* capability flags plus GreenSwamp-class detection (implementation
    /// design §5.2) once, at connect time.
    /// </summary>
    public TelescopeCapabilities GetCapabilities()
    {
        return new TelescopeCapabilities
        {
            CanFindHome = _client.CanFindHome,
            CanPark = _client.CanPark,
            CanUnpark = _client.CanUnpark,
            CanSetPark = _client.CanSetPark,
            CanPulseGuide = _client.CanPulseGuide,
            CanSetTracking = _client.CanSetTracking,
            CanSetDeclinationRate = _client.CanSetDeclinationRate,
            CanSetRightAscensionRate = _client.CanSetRightAscensionRate,
            CanSetGuideRates = _client.CanSetGuideRates,
            CanSetPierSide = _client.CanSetPierSide,
            CanSlew = _client.CanSlew,
            CanSlewAsync = _client.CanSlewAsync,
            CanSlewAltAz = _client.CanSlewAltAz,
            CanSlewAltAzAsync = _client.CanSlewAltAzAsync,
            CanSync = _client.CanSync,
            CanSyncAltAz = _client.CanSyncAltAz,
            IsGreenSwampClass = GreenSwampClassDetector.IsGreenSwampClass(_client.Description, _client.DriverInfo)
        };
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
