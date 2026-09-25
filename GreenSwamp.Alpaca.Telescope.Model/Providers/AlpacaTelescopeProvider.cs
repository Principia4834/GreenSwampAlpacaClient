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
/// GreenSwamp-class detection (SignalR-transport-implementation-plan-final.md §2): recognizes the
/// connected device's DriverInfo string alone (name+version check) - see GreenSwampClassDetector.
/// </summary>
internal interface IAlpacaTelescopeProvider : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task FindHome(CancellationToken ct);
    Task ParkAsync(CancellationToken ct);
    Task AbortSlewAsync(CancellationToken ct);
    Abstractions.TelescopeState GetState(TimeProvider timeProvider);
    TelescopeCapabilities GetCapabilities();
}

/// <summary>
/// Wraps ASCOM.Alpaca.Clients.AlpacaTelescope (ITelescopeV4) - the always-present Alpaca REST
/// </summary>
internal sealed class AlpacaTelescopeProvider : IAlpacaTelescopeProvider
{
    private static readonly TimeSpan SlowSupplementalPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TargetSupplementalPollInterval = TimeSpan.FromSeconds(1);

    private readonly IAlpacaTelescopeClient _client;
    private readonly object _supplementalLock = new();
    private AlpacaSupplementalState _supplementalState = AlpacaSupplementalState.Empty;
    private DateTimeOffset? _lastSlowSupplementalPoll;
    private DateTimeOffset? _lastTargetSupplementalPoll;
    private bool _lastSlewingState;
    private bool _hasSlewingState;

    public AlpacaTelescopeProvider(TelescopeConnectionDescriptor descriptor)
        : this(new AlpacaTelescopeClientAdapter(descriptor))
    {
    }

    internal AlpacaTelescopeProvider(IAlpacaTelescopeClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Connects via ASCOM.Common.ClientExtensions.ConnectAsync (implementation design §6.1):
    /// handles both Platform 6 and Platform 7 connect semantics, polling until fully connected.
    /// </summary>
    public Task ConnectAsync(CancellationToken ct) => _client.ConnectAsync(ct);

    public Task DisconnectAsync(CancellationToken ct) => _client.DisconnectAsync(ct);

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
        var now = timeProvider.GetUtcNow();
        var deviceState = _client.GetDeviceState();
        var currentSlewing = deviceState.Slewing ?? false;

        lock (_supplementalLock)
        {
            PollSupplementalStateIfDue(now, currentSlewing);
            return TelescopeStateMapper.ToTelescopeState(deviceState, now, _supplementalState);
        }
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
            IsGreenSwampClass = GreenSwampClassDetector.IsGreenSwampClass(_client.DriverInfo)
        };
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Polls the supplemental state (site location, alignment mode, tracking rate, target RA/Dec) if
    /// due based on the polling intervals and current slewing state.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="currentSlewing">Indicates whether the telescope is currently slewing.</param>
    private void PollSupplementalStateIfDue(DateTimeOffset now, bool currentSlewing)
    {
        if (ShouldPollSlowSupplemental(now))
        {
            _supplementalState = _supplementalState with
            {
                SiteLatitude = ReadOptionalSupplemental(() => _client.SiteLatitude, _supplementalState.SiteLatitude),
                SiteLongitude = ReadOptionalSupplemental(() => _client.SiteLongitude, _supplementalState.SiteLongitude),
                SiteElevation = ReadOptionalSupplemental(() => _client.SiteElevation, _supplementalState.SiteElevation),
                AlignmentMode = ReadOptionalSupplemental(
                    () => MapAlignmentMode(_client.AlignmentModeValue),
                    _supplementalState.AlignmentMode),
                TrackingRate = ReadOptionalSupplemental(
                    () => MapDriveRate(_client.TrackingRateValue),
                    _supplementalState.TrackingRate)
            };
            _lastSlowSupplementalPoll = now;
        }

        if (ShouldPollTargetSupplemental(now, currentSlewing))
        {
            _supplementalState = _supplementalState with
            {
                TargetRightAscension = ReadOptionalSupplemental(
                    () => _client.TargetRightAscension,
                    _supplementalState.TargetRightAscension),
                TargetDeclination = ReadOptionalSupplemental(
                    () => _client.TargetDeclination,
                    _supplementalState.TargetDeclination)
            };
            _lastTargetSupplementalPoll = now;
        }

        _lastSlewingState = currentSlewing;
        _hasSlewingState = true;
    }

    private bool ShouldPollSlowSupplemental(DateTimeOffset now)
    {
        if (!_lastSlowSupplementalPoll.HasValue)
        {
            return true;
        }

        return now - _lastSlowSupplementalPoll.Value >= SlowSupplementalPollInterval;
    }

    private bool ShouldPollTargetSupplemental(DateTimeOffset now, bool currentSlewing)
    {
        if (!_lastTargetSupplementalPoll.HasValue)
        {
            return true;
        }

        if (!_hasSlewingState)
        {
            return true;
        }

        if (!_lastSlewingState && currentSlewing)
        {
            return true;
        }

        return now - _lastTargetSupplementalPoll.Value >= TargetSupplementalPollInterval;
    }

    private static GreenSwampAlignmentMode MapAlignmentMode(int value) =>
        Enum.IsDefined(typeof(GreenSwampAlignmentMode), value)
            ? (GreenSwampAlignmentMode)value
            : default;

    private static GreenSwampDriveRate MapDriveRate(int value) =>
        Enum.IsDefined(typeof(GreenSwampDriveRate), value)
            ? (GreenSwampDriveRate)value
            : default;

    private static T ReadOptionalSupplemental<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is 
                    ASCOM.PropertyNotImplementedException or 
                    ASCOM.MethodNotImplementedException or 
                    ASCOM.NotImplementedException or 
                    ASCOM.ValueNotSetException)
        {
            return fallback;
        }
    }
}

internal interface IAlpacaTelescopeClient : IDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task FindHomeAsync(CancellationToken ct);
    Task ParkAsync(CancellationToken ct);
    Task AbortSlewAsync(CancellationToken ct);
    AscomTelescopeState GetDeviceState();

    bool CanFindHome { get; }
    bool CanPark { get; }
    bool CanUnpark { get; }
    bool CanSetPark { get; }
    bool CanPulseGuide { get; }
    bool CanSetTracking { get; }
    bool CanSetDeclinationRate { get; }
    bool CanSetRightAscensionRate { get; }
    bool CanSetGuideRates { get; }
    bool CanSetPierSide { get; }
    bool CanSlew { get; }
    bool CanSlewAsync { get; }
    bool CanSlewAltAz { get; }
    bool CanSlewAltAzAsync { get; }
    bool CanSync { get; }
    bool CanSyncAltAz { get; }
    string DriverInfo { get; }

    double SiteLatitude { get; }
    double SiteLongitude { get; }
    double SiteElevation { get; }
    int AlignmentModeValue { get; }
    int TrackingRateValue { get; }
    double TargetRightAscension { get; }
    double TargetDeclination { get; }
}

internal sealed class AlpacaTelescopeClientAdapter : IAlpacaTelescopeClient
{
    private readonly AlpacaTelescope _client;

    public AlpacaTelescopeClientAdapter(TelescopeConnectionDescriptor descriptor)
    {
        _client = new AlpacaTelescope(new AlpacaConfiguration
        {
            IpAddressString = descriptor.HostName,
            PortNumber = descriptor.Port,
            RemoteDeviceNumber = descriptor.AlpacaDeviceNumber
        });
    }

    public Task ConnectAsync(CancellationToken ct) =>
        _client.ConnectAsync(DeviceTypes.Telescope, _client.InterfaceVersion, ct);

    public Task DisconnectAsync(CancellationToken ct) =>
        _client.DisconnectAsync(DeviceTypes.Telescope, _client.InterfaceVersion, ct);

    public Task FindHomeAsync(CancellationToken ct) => _client.FindHomeAsync(ct);

    public Task ParkAsync(CancellationToken ct) => _client.ParkAsync(ct);

    public Task AbortSlewAsync(CancellationToken ct) => _client.AbortSlewAsync(ct);

    public AscomTelescopeState GetDeviceState() => new(_client.DeviceState, TL: null);

    public bool CanFindHome => _client.CanFindHome;
    public bool CanPark => _client.CanPark;
    public bool CanUnpark => _client.CanUnpark;
    public bool CanSetPark => _client.CanSetPark;
    public bool CanPulseGuide => _client.CanPulseGuide;
    public bool CanSetTracking => _client.CanSetTracking;
    public bool CanSetDeclinationRate => _client.CanSetDeclinationRate;
    public bool CanSetRightAscensionRate => _client.CanSetRightAscensionRate;
    public bool CanSetGuideRates => _client.CanSetGuideRates;
    public bool CanSetPierSide => _client.CanSetPierSide;
    public bool CanSlew => _client.CanSlew;
    public bool CanSlewAsync => _client.CanSlewAsync;
    public bool CanSlewAltAz => _client.CanSlewAltAz;
    public bool CanSlewAltAzAsync => _client.CanSlewAltAzAsync;
    public bool CanSync => _client.CanSync;
    public bool CanSyncAltAz => _client.CanSyncAltAz;
    public string DriverInfo => _client.DriverInfo;
    public double SiteLatitude => _client.SiteLatitude;
    public double SiteLongitude => _client.SiteLongitude;
    public double SiteElevation => _client.SiteElevation;
    public int AlignmentModeValue => (int)_client.AlignmentMode;
    public int TrackingRateValue => (int)_client.TrackingRate;
    public double TargetRightAscension => _client.TargetRightAscension;
    public double TargetDeclination => _client.TargetDeclination;

    public void Dispose() => _client.Dispose();
}
