namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Pure GreenSwamp-class detection logic (SignalR-transport-implementation-plan-final.md §2),
/// factored out of AlpacaTelescopeProvider so it can be unit tested with no AlpacaTelescope/HTTP
/// involvement at all (test strategy §5.1).
///
/// Detection uses the ASCOM Alpaca DriverInfo string only - not Description, and not a
/// server-level management/v1/description probe. DriverInfo is an assembly-derived identity
/// string of the form "GreenSwamp.Alpaca.Server, Version=0.0.0.0, Culture=neutral,
/// PublicKeyToken=null", produced directly from the server build assemblies. Both the name check
/// and the version check must pass together for a positive match - the version check guards
/// against a coincidental name match on an unrelated/malformed DriverInfo string.
/// </summary>
internal static class GreenSwampClassDetector
{
    /// <summary>
    /// The known GreenSwampAlpacaServer assembly name, confirmed by direct inspection of the real
    /// reference server's actual DriverInfo value.
    /// </summary>
    private const string GreenSwampAssemblyName = "GreenSwamp.Alpaca.Server";

    private const string VersionFieldPrefix = "Version=";

    /// <summary>
    /// True if <paramref name="driverInfo"/> is a comma-separated ASCOM assembly-identity string
    /// whose first field (trimmed) is "GreenSwamp.Alpaca.Server" and which also contains a
    /// well-formed four-part Version=a.b.c.d field (0.0.0.0 is a valid value). Both checks must
    /// pass together.
    /// </summary>
    public static bool IsGreenSwampClass(string? driverInfo)
    {
        if (string.IsNullOrWhiteSpace(driverInfo))
        {
            return false;
        }

        var fields = driverInfo.Split(',');
        if (fields.Length == 0 || fields[0].Trim() != GreenSwampAssemblyName)
        {
            return false;
        }

        for (var i = 1; i < fields.Length; i++)
        {
            var field = fields[i].Trim();
            if (field.StartsWith(VersionFieldPrefix, StringComparison.Ordinal) &&
                IsWellFormedFourPartVersion(field[VersionFieldPrefix.Length..]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True only for a well-formed four-part version string (a.b.c.d, each part numeric) - Version.TryParse alone would also accept two/three-part strings.</summary>
    private static bool IsWellFormedFourPartVersion(string candidate) =>
        candidate.Split('.').Length == 4 && Version.TryParse(candidate, out _);
}
