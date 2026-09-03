using System.Diagnostics;
using Renci.SshNet;
using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Ssh;

/// <summary>
/// Implementasi <see cref="ISshService"/> pakai SSH.NET (Renci.SshNet).
/// Timeout koneksi 15 detik. Password dan private key tidak di-log.
/// </summary>
public sealed class SshNetService : ISshService
{
    private const int ConnectTimeoutSeconds = 15;

    public async Task<ConnectionTestResult> TestConnectionAsync(
        SshConnectionConfig config, CancellationToken ct = default)
    {
        var validation = ValidateConfig(config);
        if (validation is not null)
            return validation;

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = CreateClient(config);
            await Task.Run(() => client.Connect(), ct);
            sw.Stop();

            if (!client.IsConnected)
                return new ConnectionTestResult(false, "Gagal terhubung ke server (tidak ada koneksi).");

            string? serverInfo = null;
            try
            {
                using var cmd = client.RunCommand("uname -a");
                if (!string.IsNullOrWhiteSpace(cmd.Result))
                    serverInfo = cmd.Result.Trim();
            }
            catch
            {
                // Info tidak wajib; koneksi tetap dianggap berhasil.
            }

            client.Disconnect();
            return new ConnectionTestResult(true, null, serverInfo, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, "Koneksi dibatalkan atau timeout.");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Gagal terhubung: {ex.Message}");
        }
    }

    public async Task<string> ExecuteCommandAsync(
        SshConnectionConfig config, string command, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        using var client = CreateClient(config);
        await Task.Run(() => client.Connect(), ct);
        try
        {
            using var cmd = client.RunCommand(command);
            return cmd.Result ?? string.Empty;
        }
        finally
        {
            if (client.IsConnected) client.Disconnect();
        }
    }

    private static ConnectionTestResult? ValidateConfig(SshConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
            return new ConnectionTestResult(false, "Host tidak boleh kosong.");

        if (config.Port <= 0 || config.Port > 65535)
            return new ConnectionTestResult(false, "Port harus antara 1-65535.");

        if (string.IsNullOrWhiteSpace(config.Username))
            return new ConnectionTestResult(false, "Username tidak boleh kosong.");

        return config.AuthMethod switch
        {
            SshAuthMethod.Password when string.IsNullOrEmpty(config.Password)
                => new ConnectionTestResult(false, "Password tidak boleh kosong untuk metode Password."),
            SshAuthMethod.PrivateKey when string.IsNullOrWhiteSpace(config.PrivateKey)
                => new ConnectionTestResult(false, "Private key tidak boleh kosong untuk metode Private Key."),
            _ => null
        };
    }

    private static SshClient CreateClient(SshConnectionConfig config)
    {
        var authMethod = config.AuthMethod switch
        {
            SshAuthMethod.Password => (AuthenticationMethod)
                new PasswordAuthenticationMethod(config.Username, config.Password ?? string.Empty),
            SshAuthMethod.PrivateKey => new PrivateKeyAuthenticationMethod(
                config.Username,
                new PrivateKeyFile(ResolveKeyStream(config.PrivateKey!))),
            _ => throw new InvalidOperationException($"Metode autentikasi tidak dikenal: {config.AuthMethod}")
        };

        var connectionInfo = new ConnectionInfo(config.Host, config.Port, config.Username, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds)
        };

        return new SshClient(connectionInfo);
    }

    private static Stream ResolveKeyStream(string privateKey)
    {
        // Boleh raw key string ATAU path ke file .pem di filesystem.
        if (File.Exists(privateKey))
            return new FileStream(privateKey, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey));
    }
}
