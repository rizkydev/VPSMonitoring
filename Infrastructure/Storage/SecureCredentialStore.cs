using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Storage;

/// <summary>
/// Implementasi <see cref="ICredentialStore"/> di atas MAUI <c>SecureStorage</c>.
/// Aman untuk credential sensitif (password & private key) — terenkripsi di OS keychain
/// (Windows Credential Manager / macOS Keychain / Android Keystore).
/// </summary>
public sealed class SecureCredentialStore : ICredentialStore
{
    private const string HostKey = "ssh_host";
    private const string PortKey = "ssh_port";
    private const string UsernameKey = "ssh_username";
    private const string AuthMethodKey = "ssh_auth_method";
    private const string PasswordKey = "ssh_password";
    private const string PrivateKeyKey = "ssh_private_key";

    public async Task SaveAsync(SshConnectionConfig config)
    {
        await SecureStorage.Default.SetAsync(HostKey, config.Host);
        await SecureStorage.Default.SetAsync(PortKey, config.Port.ToString());
        await SecureStorage.Default.SetAsync(UsernameKey, config.Username);
        await SecureStorage.Default.SetAsync(AuthMethodKey, config.AuthMethod.ToString());

        // Simpan hanya field yang relevan sesuai auth method, bersihkan yang lain.
        if (config.AuthMethod == SshAuthMethod.Password)
        {
            await SecureStorage.Default.SetAsync(PasswordKey, config.Password ?? string.Empty);
            SecureStorage.Default.Remove(PrivateKeyKey);
        }
        else
        {
            await SecureStorage.Default.SetAsync(PrivateKeyKey, config.PrivateKey ?? string.Empty);
            SecureStorage.Default.Remove(PasswordKey);
        }
    }

    public async Task<SshConnectionConfig?> LoadAsync(CancellationToken ct = default)
    {
        // SecureStorage di Windows MAUI Hybrid sering hang/timeout karena WinRT API init.
        // Wrap di try-catch + return null pada error apapun supaya UI tidak stuck.
        try
        {
            ct.ThrowIfCancellationRequested();

            var host = await SecureStorage.Default.GetAsync(HostKey).WaitAsync(TimeSpan.FromSeconds(2), ct);
            if (string.IsNullOrEmpty(host))
                return null;

            var portStr = await SecureStorage.Default.GetAsync(PortKey).WaitAsync(TimeSpan.FromSeconds(2), ct);
            var port = int.TryParse(portStr, out var p) ? p : 22;

            var username = await SecureStorage.Default.GetAsync(UsernameKey).WaitAsync(TimeSpan.FromSeconds(2), ct) ?? "root";
            var authStr = await SecureStorage.Default.GetAsync(AuthMethodKey).WaitAsync(TimeSpan.FromSeconds(2), ct);
            var auth = Enum.TryParse<SshAuthMethod>(authStr, out var a) ? a : SshAuthMethod.Password;

            var password = await SecureStorage.Default.GetAsync(PasswordKey).WaitAsync(TimeSpan.FromSeconds(2), ct);
            var privateKey = await SecureStorage.Default.GetAsync(PrivateKeyKey).WaitAsync(TimeSpan.FromSeconds(2), ct);

            return new SshConnectionConfig(host, port, username, auth, password, privateKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SecureStorage gagal (hang/timeout/exception) — treat as no credentials
            System.Diagnostics.Debug.WriteLine($"[SecureCredentialStore.LoadAsync] Failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> HasCredentialsAsync()
    {
        var host = await SecureStorage.Default.GetAsync(HostKey);
        return !string.IsNullOrEmpty(host);
    }

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(HostKey);
        SecureStorage.Default.Remove(PortKey);
        SecureStorage.Default.Remove(UsernameKey);
        SecureStorage.Default.Remove(AuthMethodKey);
        SecureStorage.Default.Remove(PasswordKey);
        SecureStorage.Default.Remove(PrivateKeyKey);
        return Task.CompletedTask;
    }
}
