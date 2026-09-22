namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// Neutral, protocol-agnostic mirror of ASCOM.Common.DeviceInterfaces.TelescopeAxis.
/// Defined here (not referenced from ASCOM.Common) so the ViewModel/Abstractions layer never
/// takes a compile-time dependency on the ASCOM client library (architecture §6.2). Values and
/// meanings are a direct 1:1 mirror - no value remapping occurs. "Primary"/"Secondary" apply to
/// both equatorial mounts (Right Ascension / Declination) and Alt-Az mounts (Azimuth / Altitude).
/// </summary>
public enum TelescopeAxis
{
    Primary,
    Secondary,
    Tertiary
}
