using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Abstraksi untuk koneksi SSH ke VPS. Implementasi ada di Infrastructure/Ssh.
/// </summary>
public interface ISshService
{
    /// <summary>
    /// Menguji koneksi SSH ke VPS berdasarkan <paramref name="config"/>.
    /// Tidak menyimpan kredensial, hanya untuk verifikasi.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(SshConnectionConfig config, CancellationToken ct = default);

    /// <summary>
    /// Menjalankan satu command di remote dan mengembalikan stdout.
    /// </summary>
    Task<string> ExecuteCommandAsync(SshConnectionConfig config, string command, CancellationToken ct = default);
}
