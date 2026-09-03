using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Penyimpanan kredensial SSH lokal. Implementasi MAUI SecureStorage
/// ada di Infrastructure/Storage. Setiap method harus thread-safe.
/// </summary>
public interface ICredentialStore
{
    Task SaveAsync(SshConnectionConfig config);
    Task<SshConnectionConfig?> LoadAsync(CancellationToken ct = default);
    Task ClearAsync();
    Task<bool> HasCredentialsAsync();
}
