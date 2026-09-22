namespace GreenSwamp.Alpaca.Telescope.Abstractions;

/// <summary>
/// Neutral, protocol-agnostic mirror of ASCOM.Common.DeviceInterfaces.PointingState.
/// Defined here (not referenced from ASCOM.Common) so the ViewModel/Abstractions layer never
/// takes a compile-time dependency on the ASCOM client library, consistent with the
/// TelescopeAxis insulation pattern (implementation design §5.1). Values and meanings are a
/// direct 1:1 mirror - no value remapping occurs. A provider maps a null DeviceState.SideOfPier
/// (a legitimate, standard possibility for mounts that cannot report pointing state) to Unknown.
/// </summary>
public enum TelescopePierSide
{
    /// <summary>ASCOM PointingState.Normal (legacy pierEast).</summary>
    Normal,

    /// <summary>ASCOM PointingState.ThroughThePole (legacy pierWest).</summary>
    ThroughThePole,

    /// <summary>ASCOM PointingState.Unknown (legacy pierUnknown), or an unreported/null value.</summary>
    Unknown
}
