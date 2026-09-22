namespace GreenSwamp.Alpaca.Telescope.Model.Providers;

/// <summary>
/// Pure GreenSwamp-class detection logic (implementation design §5.2, architecture §6.8),
/// factored out of AlpacaTelescopeProvider so it can be unit tested with no AlpacaTelescope/HTTP
/// involvement at all (test strategy §5.1).
///
/// For this slice, detection uses only the per-device Description/DriverInfo strings reported by
/// the connected device itself, avoiding an extra server-level management-API round-trip (no
/// client-library helper exists for a one-shot management/v1/description call against a known
/// host - confirmed by inspection of ASCOM.Alpaca.Components). A future revision may add the
/// server-level management/v1/description check as a stronger/earlier signal (e.g. for
/// discovery, architecture §6.10).
/// </summary>
internal static class GreenSwampClassDetector
{
    /// <summary>
    /// Known GreenSwampAlpacaServer identity strings, confirmed by direct inspection of
    /// GreenSwampAlpacaServer's Telescope.cs/Program.cs (implementation design §5.2, architecture §6.8).
    /// Matched case-insensitively against the connected device's Description/DriverInfo.
    /// </summary>
    private static readonly string[] GreenSwampIdentityMarkers =
    [
        "Green Swamp Alpaca Server",
        "GreenSwamp Alpaca Server",
        "GreenSwampServer"
    ];

    /// <summary>
    /// True if either <paramref name="description"/> or <paramref name="driverInfo"/> contains one
    /// of the known GreenSwamp identity markers (case-insensitive substring match).
    /// </summary>
    public static bool IsGreenSwampClass(string? description, string? driverInfo) =>
        ContainsAnyMarker(description) || ContainsAnyMarker(driverInfo);

    private static bool ContainsAnyMarker(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var marker in GreenSwampIdentityMarkers)
        {
            if (candidate.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
