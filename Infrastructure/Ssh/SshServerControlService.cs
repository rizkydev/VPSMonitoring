using Renci.SshNet;
using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Ssh;

/// <summary>
/// Implementasi <see cref="IServerControlService"/> pakai SSH.NET.
/// Reboot pakai <c>shutdown -r +N</c> (delay supaya cancelable) — bukan <c>reboot</c> langsung.
/// </summary>
public sealed class SshServerControlService : IServerControlService
{
    private const int PingTimeoutSeconds = 3;
    // Dinaikkan dari 10→30 detik. Server yang baru reboot atau sibuk bisa
    // butuh waktu lebih lama untuk respond ke command 'shutdown'.
    private const int RebootCommandTimeoutSeconds = 30;

    public async Task RebootAsync(SshConnectionConfig config, int delayMinutes = 1, CancellationToken ct = default)
    {
        if (delayMinutes < 0 || delayMinutes > 60)
            throw new ArgumentOutOfRangeException(nameof(delayMinutes), "Delay harus 0-60 menit.");

        // 'shutdown -r +N' = reboot dalam N menit. Cancelable dengan 'sudo shutdown -c'.
        // 'now' = immediate (delayMinutes = 0)
        var arg = delayMinutes == 0 ? "now" : $"+{delayMinutes}";
        var command = $"shutdown -r {arg} 2>&1";

        using var client = CreateClient(config, RebootCommandTimeoutSeconds);
        await Task.Run(() => client.Connect(), ct);

        if (!client.IsConnected)
            throw new InvalidOperationException("Gagal terhubung ke server.");

        try
        {
            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(RebootCommandTimeoutSeconds);
            cmd.Execute();

            // Jika command exit code non-zero, anggap error
            if (cmd.ExitStatus != 0)
            {
                var error = cmd.Error?.Trim() ?? cmd.Result?.Trim() ?? "Unknown error";
                throw new InvalidOperationException($"Reboot gagal: {error}");
            }
        }
        finally
        {
            try { client.Disconnect(); } catch { /* ignore */ }
        }
    }

    public async Task<bool> PingAsync(SshConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(config, PingTimeoutSeconds);
            // Race connection + cancellation
            var connectTask = Task.Run(() => client.Connect(), ct);
            var completed = await Task.WhenAny(connectTask, Task.Delay(PingTimeoutSeconds * 1000, ct));
            if (completed != connectTask) return false;

            await connectTask; // propagate exception if any
            var isUp = client.IsConnected;
            try { client.Disconnect(); } catch { /* ignore */ }
            return isUp;
        }
        catch
        {
            return false;
        }
    }

    private static SshClient CreateClient(SshConnectionConfig config, int timeoutSeconds)
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
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        return new SshClient(connectionInfo);
    }

    private static Stream ResolveKeyStream(string privateKey)
    {
        if (File.Exists(privateKey))
            return new FileStream(privateKey, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey));
    }
}
