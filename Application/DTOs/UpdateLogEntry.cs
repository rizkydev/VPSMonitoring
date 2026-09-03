namespace VPS_Monitor_Desktop_App.Application.DTOs;

/// <summary>
/// Satu entry log untuk operasi update/reboot. Disimpan di local storage (JSON file)
/// supaya persist antar session aplikasi.
/// </summary>
public sealed class UpdateLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Action { get; set; } = "";      // "check" | "update" | "reboot"
    public bool Success { get; set; }
    public string Summary { get; set; } = "";     // ringkasan singkat untuk card
    public string FullLog { get; set; } = "";     // output lengkap
    public int PackagesUpgraded { get; set; }
    public int TotalUpdates { get; set; }
    public int SecurityUpdates { get; set; }
    public bool RebootRequired { get; set; }
    public bool RebootPerformed { get; set; }
    public string? ErrorMessage { get; set; }
}
