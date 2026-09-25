using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GreenSwamp.Alpaca.Telescope.Abstractions;

namespace GreenSwamp.Alpaca.Client.ViewModels;

/// <summary>
/// One open telescope tab (architecture §8.1/§8.2). Depends only on <see cref="ITelescopeSession"/>
/// and <see cref="IUiDispatcher"/> - no ASCOM/SignalR types, per the layering rule in architecture
/// §4. Exposes every field of <see cref="TelescopeState"/> (implementation design's instruction to
/// expose the full DeviceState-bundled property surface) plus the four in-scope commands
/// (Connect, FindHome, Park, Abort - implementation design §6).
/// </summary>
public sealed partial class TelescopeTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ITelescopeSession _session;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty]
    private double _altitude;

    [ObservableProperty]
    private double _azimuth;

    [ObservableProperty]
    private double _declination;

    [ObservableProperty]
    private double _rightAscension;

    [ObservableProperty]
    private TelescopePierSide _sideOfPier;

    [ObservableProperty]
    private bool _isSlewing;

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private bool _atHome;

    [ObservableProperty]
    private bool _atPark;

    [ObservableProperty]
    private bool _isPulseGuiding;

    [ObservableProperty]
    private DateTime _utcDate;

    [ObservableProperty]
    private DateTimeOffset? _stateTimeStamp;

    [ObservableProperty]
    private double _siteLatitude;

    [ObservableProperty]
    private double _siteLongitude;

    [ObservableProperty]
    private double _siteElevation;

    [ObservableProperty]
    private GreenSwampAlignmentMode _alignmentMode;

    [ObservableProperty]
    private GreenSwampDriveRate _trackingRate;

    [ObservableProperty]
    private double _targetRightAscension;

    [ObservableProperty]
    private double _targetDeclination;

    [ObservableProperty]
    private GreenSwampTelescopeState? _greenSwamp;

    [ObservableProperty]
    private TelescopeConnectionState _connectionState = TelescopeConnectionState.Disconnected;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindHomeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParkCommand))]
    private TelescopeCapabilities _capabilities = new();

    [ObservableProperty]
    private string? _statusMessage;

    public TelescopeTabViewModel(ITelescopeSession session, IUiDispatcher dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;

        Capabilities = session.Capabilities;
        ApplyConnectionStatus(session.Status);

        _session.StateUpdated += OnStateUpdated;
        _session.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    [RelayCommand]
    private Task ConnectAsync(CancellationToken ct) => _session.ConnectAsync(ct);

    [RelayCommand]
    private Task DisconnectAsync(CancellationToken ct) => _session.DisconnectAsync(ct);

    [RelayCommand(CanExecute = nameof(CanFindHome))]
    private Task FindHomeAsync(CancellationToken ct) => _session.FindHome(ct);

    private bool CanFindHome() => Capabilities.CanFindHome;

    [RelayCommand(CanExecute = nameof(CanPark))]
    private Task ParkAsync(CancellationToken ct) => _session.ParkAsync(ct);

    private bool CanPark() => Capabilities.CanPark;

    /// <summary>Always enabled while connected - AbortSlew has no Can* gate (implementation design §6.4).</summary>
    [RelayCommand]
    private Task AbortAsync(CancellationToken ct) => _session.AbortSlewAsync(ct);

    private void OnStateUpdated(object? sender, TelescopeStateUpdatedEventArgs e) =>
        _dispatcher.Post(() =>
        {
            var state = e.State;
            Altitude = state.Altitude;
            Azimuth = state.Azimuth;
            Declination = state.Declination;
            RightAscension = state.RightAscension;
            SideOfPier = state.SideOfPier;
            IsSlewing = state.Slewing;
            IsTracking = state.Tracking;
            AtHome = state.AtHome;
            AtPark = state.AtPark;
            IsPulseGuiding = state.IsPulseGuiding;
            UtcDate = state.UtcDate;
            StateTimeStamp = state.TimeStamp;
            SiteLatitude = state.SiteLatitude;
            SiteLongitude = state.SiteLongitude;
            SiteElevation = state.SiteElevation;
            AlignmentMode = state.AlignmentMode;
            TrackingRate = state.TrackingRate;
            TargetRightAscension = state.TargetRightAscension;
            TargetDeclination = state.TargetDeclination;
            GreenSwamp = state.GreenSwamp;
        });

    private void OnConnectionStatusChanged(object? sender, TelescopeConnectionStatusChangedEventArgs e) =>
        _dispatcher.Post(() => ApplyConnectionStatus(e.Status));

    private void ApplyConnectionStatus(TelescopeConnectionStatus status)
    {
        ConnectionState = status.State;
        IsConnected = status.State == TelescopeConnectionState.Connected;
        StatusMessage = status.ErrorMessage;

        if (status.State == TelescopeConnectionState.Connected)
        {
            // Capabilities are populated once, at connect time (TelescopeCapabilities remarks).
            Capabilities = _session.Capabilities;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _session.StateUpdated -= OnStateUpdated;
        _session.ConnectionStatusChanged -= OnConnectionStatusChanged;
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
