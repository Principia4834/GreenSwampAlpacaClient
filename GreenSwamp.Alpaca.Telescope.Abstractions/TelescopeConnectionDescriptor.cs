namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// Minimal, immutable descriptor of how to reach one telescope device, passed to
/// ITelescopeSessionFactory.Create (architecture §6.1). Deliberately light: how a descriptor is
/// obtained (manual entry, a saved connection, or ITelescopeDiscoveryService) and persisted is a
/// settings-design concern, out of scope for this pass.
/// </summary>
/// <param name="HostName">Host name or IP address of the Alpaca device server.</param>
/// <param name="Port">Alpaca REST API port.</param>
/// <param name="AlpacaDeviceNumber">The telescope device number at this server (Alpaca supports multiple devices of the same type per server).</param>
/// <param name="ServerName">Server-reported name (management API), for display purposes only; not used for identification.</param>
public sealed record TelescopeConnectionDescriptor(
    string HostName,
    int Port,
    int AlpacaDeviceNumber,
    string? ServerName = null);
