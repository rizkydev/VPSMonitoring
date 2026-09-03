using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Service untuk kontrol server via SSH (reboot, shutdown, dll). Berbeda dari IUpdateService
/// karena ini operasi "merusak" (server down sementara) — perlu handling khusus.
/// </summary>
public interface IServerControlService
{
    /// <summary>
    /// Reboot server. Default delay 1 menit supaya user punya waktu cancel via SSH
    /// (<c>sudo shutdown -c</c>) kalau salah klik. SSH connection akan drop dalam ~30 detik.
    /// </summary>
    Task RebootAsync(SshConnectionConfig config, int delayMinutes = 1, CancellationToken ct = default);

    /// <summary>
    /// Cek apakah server bisa dihubungi. Return true kalau koneksi SSH berhasil,
    /// false kalau timeout/refused. Untuk polling setelah reboot.
    /// </summary>
    Task<bool> PingAsync(SshConnectionConfig config, CancellationToken ct = default);
}
