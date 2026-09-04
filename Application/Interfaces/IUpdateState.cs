using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Singleton state holder untuk proses System Updates (Cek, Update, Force, Refresh, Reboot).
/// CancellationToken hidup di service (bukan component), supaya proses TIDAK ke-cancel
/// saat user navigate ke page lain.
/// </summary>
public interface IUpdateState
{
    /// <summary>Summary update terakhir. Null kalau belum pernah cek.</summary>
    UpdateSummary? Summary { get; }

    /// <summary>Hasil dari update terakhir (setelah Apply*). Null kalau belum apply.</summary>
    UpdateResult? Result { get; }

    /// <summary>Log streaming dari proses yang sedang jalan (atau terakhir selesai).</summary>
    string ProgressLog { get; }

    /// <summary>Status dari proses reboot (untuk banner). Null kalau gak ada reboot in-flight.</summary>
    string? RebootStatus { get; }

    string RebootStatusIcon { get; }
    string RebootStatusClass { get; }

    /// <summary>True kalau proses Cek (Cek Packages) sedang jalan.</summary>
    bool IsChecking { get; }

    /// <summary>True kalau proses Cek/Refresh/Apply masih jalan.</summary>
    bool IsBusy { get; }

    /// <summary>True kalau Apply masih jalan.</summary>
    bool IsApplying { get; }

    /// <summary>True kalau Reboot command sudah dikirim / sedang polling.</summary>
    bool IsRebooting { get; }

    /// <summary>True kalau sedang polling server untuk detect online setelah reboot.</summary>
    bool IsRebootPolling { get; }

    /// <summary>Triggered setiap kali state berubah. Subscribe dari component untuk re-render.</summary>
    event Action? StateChanged;

    /// <summary>Cek update available di server. Skip kalau IsBusy.</summary>
    Task CheckAsync(SshConnectionConfig config);

    /// <summary>Refresh apt cache (clean + update index).</summary>
    Task RefreshCacheAsync(SshConnectionConfig config);

    /// <summary>Apply standard upgrade (apt-get upgrade -y).</summary>
    Task<UpdateResult> ApplyUpgradeAsync(SshConnectionConfig config);

    /// <summary>Apply full upgrade (apt full-upgrade -y) — lebih agresif.</summary>
    Task<UpdateResult> ApplyFullUpgradeAsync(SshConnectionConfig config);

    /// <summary>Install specific packages (apt install -y --only-upgrade &lt;names&gt;).</summary>
    Task<UpdateResult> InstallPackagesAsync(SshConnectionConfig config, IReadOnlyList<string> packageNames);

    /// <summary>Reboot server. Auto-poll sampai online, lalu re-check summary.</summary>
    Task RebootAsync(SshConnectionConfig config);

    /// <summary>Clear Result dan ProgressLog (dipanggil saat close modal).</summary>
    void ClearResult();
}
