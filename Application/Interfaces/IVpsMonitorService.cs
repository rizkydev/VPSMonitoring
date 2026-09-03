using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Orkestrasi pengumpulan snapshot VPS via SSH. Implementasi ada di
/// <see cref="VPS_Monitor_Desktop_App.Infrastructure.Ssh.SshMonitorService"/>.
/// On-demand only — TIDAK ada polling/background loop. Trigger eksplisit dari UI.
/// </summary>
public interface IVpsMonitorService
{
    /// <summary>
    /// Mengambil snapshot lengkap (CPU/RAM/Storage/Uptime) dalam satu sesi SSH.
    /// </summary>
    Task<SystemSnapshot> GetSnapshotAsync(SshConnectionConfig config, CancellationToken ct = default);
}
