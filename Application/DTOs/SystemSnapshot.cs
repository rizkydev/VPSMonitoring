namespace VPS_Monitor_Desktop_App.Application.DTOs;

/// <summary>
/// Snapshot kondisi VPS pada satu waktu. Dikumpulkan sekali jalan per "Cek Sekarang".
/// Field null/empty = data tidak berhasil dikumpulkan (bukan berarti 0).
/// </summary>
public sealed class SystemSnapshot
{
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public bool IsOnline { get; init; }
    public OsFamily OsFamily { get; init; } = OsFamily.Unknown;
    public CpuMetrics? Cpu { get; init; }
    public MemoryMetrics? Memory { get; init; }
    public IReadOnlyList<StorageMetrics> Storage { get; init; } = Array.Empty<StorageMetrics>();
    public UptimeMetrics? Uptime { get; init; }
    public IReadOnlyList<InstalledPackage> Software { get; init; } = Array.Empty<InstalledPackage>();
    public IReadOnlyList<HostedWebsite> Websites { get; init; } = Array.Empty<HostedWebsite>();
    public IReadOnlyList<ServiceStatus> Services { get; init; } = Array.Empty<ServiceStatus>();
    public IReadOnlyList<DockerContainer> DockerContainers { get; init; } = Array.Empty<DockerContainer>();
    public FirewallStatus? Firewall { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum OsFamily
{
    Unknown,
    Debian,   // Debian, Ubuntu, etc.
    Rhel,     // RHEL, CentOS, Fedora, Rocky, Alma
    Arch,     // Arch, Manjaro
}

public sealed class CpuMetrics
{
    /// <summary>0-100</summary>
    public double UsagePercent { get; init; }
    public int CoreCount { get; init; }
}

public sealed class MemoryMetrics
{
    public double UsagePercent { get; init; }
    public double TotalGb { get; init; }
    public double UsedGb { get; init; }
    public double FreeGb { get; init; }
}

public sealed class StorageMetrics
{
    public string Filesystem { get; init; } = string.Empty;
    public string MountPoint { get; init; } = string.Empty;
    public double UsagePercent { get; init; }
    public double TotalGb { get; init; }
    public double UsedGb { get; init; }
    public double FreeGb { get; init; }
}

public sealed class UptimeMetrics
{
    public string HumanReadable { get; init; } = string.Empty;
    public double LoadAverage1Min { get; init; }
    public double LoadAverage5Min { get; init; }
    public double LoadAverage15Min { get; init; }
}

public enum PackageCategory
{
    Other,
    System,
    WebServer,
    Database,
    Runtime,
    Container,
    Security,
    Network,
}

public sealed class InstalledPackage
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public PackageCategory Category { get; init; } = PackageCategory.Other;
}

public sealed class HostedWebsite
{
    public string Domain { get; init; } = string.Empty;
    public string ServerType { get; init; } = string.Empty;  // "nginx", "apache", "docker"
    public string? ConfigPath { get; init; }
    public bool HasSsl { get; init; }
    public int HttpPort { get; init; } = 80;
    public int? HttpsPort { get; init; } = 443;
}

public sealed class ServiceStatus
{
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string State { get; init; } = string.Empty;  // "active", "inactive", "failed", "unknown"
}

public sealed class DockerContainer
{
    public string Name { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<int> ExposedPorts { get; init; } = Array.Empty<int>();
}

public sealed class FirewallStatus
{
    public bool IsActive { get; init; }
    public string Raw { get; init; } = string.Empty;
    public IReadOnlyList<FirewallRule> OpenPorts { get; init; } = Array.Empty<FirewallRule>();
}

public sealed class FirewallRule
{
    public string To { get; init; } = string.Empty;  // "22", "80", "443/tcp", etc.
    public string Action { get; init; } = string.Empty;  // "ALLOW", "DENY"
    public string From { get; init; } = string.Empty;  // "Anywhere", "192.168.0.0/24", etc.
}
