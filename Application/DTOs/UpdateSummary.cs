namespace VPS_Monitor_Desktop_App.Application.DTOs;

/// <summary>
/// Ringkasan update yang tersedia di server. Dikumpulkan via <c>apt list --upgradable</c>.
/// </summary>
public sealed class UpdateSummary
{
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public int TotalUpdates { get; init; }
    public int SecurityUpdates { get; init; }
    public bool RebootRequired { get; set; }
    public IReadOnlyList<string> RebootRequiredPackages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PackageUpdate> Packages { get; init; } = Array.Empty<PackageUpdate>();
    public string? ErrorMessage { get; init; }
}

public sealed class PackageUpdate
{
    public string Name { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = string.Empty;
    public string NewVersion { get; init; } = string.Empty;
    public string Repository { get; init; } = string.Empty;
    public bool IsSecurity { get; init; }
}

/// <summary>
/// Hasil dari eksekusi update. Berisi summary + log untuk UI.
/// </summary>
public sealed class UpdateResult
{
    public bool Success { get; init; }
    public bool RebootRequired { get; init; }
    public int PackagesUpgraded { get; init; }
    public string? ErrorMessage { get; init; }
    public string Log { get; init; } = string.Empty;
}
