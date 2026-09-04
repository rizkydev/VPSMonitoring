using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Singleton state holder untuk proses "Cek Sekarang" di dashboard.
/// CancellationToken hidup di service (bukan di component), supaya proses
/// TIDAK ke-cancel saat user navigate ke page lain (Logs, Settings, dll).
/// Component subscribe <see cref="StateChanged"/> untuk re-render.
/// </summary>
public interface ISnapshotCheckState
{
    /// <summary>Snapshot terakhir yang berhasil di-fetch. Null kalau belum pernah / gagal.</summary>
    SystemSnapshot? Current { get; }

    /// <summary>True saat proses fetch sedang berjalan.</summary>
    bool IsChecking { get; }

    /// <summary>Timestamp UTC dari fetch terakhir (sukses maupun gagal).</summary>
    DateTime? LastCheckedAt { get; }

    /// <summary>Error message dari fetch terakhir (kalau ada).</summary>
    string? LastError { get; }

    /// <summary>Triggered setiap kali state berubah. Subscribe dari component untuk re-render.</summary>
    event Action? StateChanged;

    /// <summary>Mulai fetch snapshot. Kalau sudah ada proses berjalan, di-skip (no-op).</summary>
    Task CheckAsync(SshConnectionConfig config);
}
