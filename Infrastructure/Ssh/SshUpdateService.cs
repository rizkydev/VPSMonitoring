using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Ssh;

/// <summary>
/// Implementasi <see cref="IUpdateService"/> pakai SSH.NET. Hanya untuk keluarga Debian/Ubuntu
/// (pakai apt). Untuk RHEL/CentOS perlu yum/dnf — belum di-support di iterasi ini.
/// </summary>
public sealed class SshUpdateService : IUpdateService
{
    private const int CommandTimeoutMinutes = 10;

    public async Task<UpdateSummary> GetAvailableUpdatesAsync(
        SshConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(config);
            await Task.Run(() => client.Connect(), ct);
            if (!client.IsConnected)
            {
                return new UpdateSummary
                {
                    ErrorMessage = "Gagal terhubung ke server.",
                };
            }

            // 1) Refresh package index
            var updateOutput = await Task.Run(() =>
            {
                using var cmd = client.CreateCommand("apt-get update -y 2>&1");
                cmd.CommandTimeout = TimeSpan.FromMinutes(2);
                return cmd.Execute() + cmd.Result;
            }, ct);

            // 2) List upgradable
            var listOutput = await Task.Run(() =>
            {
                using var cmd = client.CreateCommand("apt list --upgradable 2>/dev/null");
                cmd.CommandTimeout = TimeSpan.FromSeconds(30);
                return cmd.Execute() + cmd.Result;
            }, ct);

            // 3) Reboot required?
            var rebootOutput = await Task.Run(() =>
            {
                using var cmd = client.CreateCommand("if [ -f /var/run/reboot-required ]; then echo YES; cat /var/run/reboot-required.pkgs 2>/dev/null; else echo NO; fi");
                cmd.CommandTimeout = TimeSpan.FromSeconds(5);
                return cmd.Execute() + cmd.Result;
            }, ct);

            // 4) Security packages (heuristic: package dengan prefix -security atau ada di security repo)
            //    apt-get changelog atau apt-check bisa kasih info lebih akurat, tapi sederhana pakai parse repository.
            var packages = ParseUpgradableList(listOutput);
            var rebootPkgs = ParseRebootPackages(rebootOutput);
            var needsReboot = rebootOutput.TrimStart().StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            var securityCount = packages.Count(p => p.IsSecurity);

            try { client.Disconnect(); } catch { /* ignore */ }

            return new UpdateSummary
            {
                TotalUpdates = packages.Count,
                SecurityUpdates = securityCount,
                RebootRequired = needsReboot,
                RebootRequiredPackages = rebootPkgs,
                Packages = packages,
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateSummary { ErrorMessage = "Pengecekan update dibatalkan." };
        }
        catch (Exception ex)
        {
            return new UpdateSummary { ErrorMessage = $"Gagal cek update: {ex.Message}" };
        }
    }

    public async Task<UpdateResult> ApplyUpdatesAsync(
        SshConnectionConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await RunUpgradeInternalAsync(
            config,
            "DEBIAN_FRONTEND=noninteractive apt-get upgrade -y 2>&1",
            "apt-get upgrade -y",
            progress,
            ct);
    }

    public async Task<UpdateResult> ApplyFullUpgradeAsync(
        SshConnectionConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // `apt full-upgrade` lebih agresif: bisa remove package yang dianggap obsolete
        // untuk resolve dependency. Pakai ini hanya kalau `apt-get upgrade` tidak apply
        // (mis. Ubuntu phased updates, atau held-back package).
        return await RunUpgradeInternalAsync(
            config,
            "DEBIAN_FRONTEND=noninteractive apt full-upgrade -y 2>&1",
            "apt full-upgrade -y",
            progress,
            ct);
    }

    public async Task<UpdateResult> InstallPackagesAsync(
        SshConnectionConfig config,
        IReadOnlyList<string> packageNames,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (packageNames is null || packageNames.Count == 0)
            return new UpdateResult { Success = false, ErrorMessage = "Daftar package kosong." };

        // Sanitize input: hanya alphanumeric, dot, dash, plus
        var safe = packageNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => Regex.Replace(n.Trim(), @"[^a-zA-Z0-9\.+\-]", ""))
            .Where(n => n.Length > 0)
            .ToList();

        if (safe.Count == 0)
            return new UpdateResult { Success = false, ErrorMessage = "Nama package tidak valid." };

        var pkgList = string.Join(" ", safe);
        // --only-upgrade = hanya upgrade kalau ada versi lebih baru, jangan install baru
        return await RunUpgradeInternalAsync(
            config,
            $"DEBIAN_FRONTEND=noninteractive apt install -y --only-upgrade {pkgList} 2>&1",
            $"apt install -y --only-upgrade {pkgList}",
            progress,
            ct);
    }

    public async Task<UpdateResult> RefreshAptCacheAsync(
        SshConnectionConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await RunUpgradeInternalAsync(
            config,
            "apt clean && rm -rf /var/lib/apt/lists/* && apt update 2>&1",
            "apt clean + apt update",
            progress,
            ct);
    }

    private async Task<UpdateResult> RunUpgradeInternalAsync(
        SshConnectionConfig config,
        string sshCommand,
        string commandLabel,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        var packagesUpgraded = 0;

        try
        {
            using var client = CreateClient(config);
            client.ConnectionInfo.Timeout = TimeSpan.FromMinutes(CommandTimeoutMinutes);
            await Task.Run(() => client.Connect(), ct);
            if (!client.IsConnected)
            {
                return new UpdateResult { Success = false, ErrorMessage = "Gagal terhubung ke server." };
            }

            progress?.Report($"🔄 Menjalankan {commandLabel}...\n");
            log.AppendLine($"=== {commandLabel} ===");

            using var cmd = client.CreateCommand(sshCommand);
            cmd.CommandTimeout = TimeSpan.FromMinutes(CommandTimeoutMinutes);

            // Background task: baca output stream baris per baris → progress + log
            using var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
            var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var readTask = Task.Run(async () =>
            {
                while (!streamCts.Token.IsCancellationRequested)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync(streamCts.Token); }
                    catch (OperationCanceledException) { break; }

                    if (line is null) break; // stream closed

                    lock (log) log.AppendLine(line);
                    progress?.Report(line);

                    // Hitung package yang di-upgrade
                    if (line.StartsWith("Setting up ", StringComparison.Ordinal) ||
                        line.StartsWith("Unpacking ", StringComparison.Ordinal))
                    {
                        var match = Regex.Match(line, @"^(\w+) ([^\s]+) ");
                        if (match.Success) packagesUpgraded++;
                    }
                }
            }, streamCts.Token);

            // Execute sync (blocks sampai selesai)
            await Task.Run(() => cmd.Execute(), ct);
            streamCts.Cancel();
            try { await readTask; } catch { /* ignore */ }

            var exitStatus = cmd.ExitStatus;
            try { client.Disconnect(); } catch { /* ignore */ }

            // Final check: reboot required?
            progress?.Report("\n🔍 Cek apakah reboot diperlukan...");
            using var client2 = CreateClient(config);
            await Task.Run(() => client2.Connect(), ct);
            string rebootResult = "NO";
            if (client2.IsConnected)
            {
                using var cmd2 = client2.CreateCommand("if [ -f /var/run/reboot-required ]; then echo YES; else echo NO; fi");
                cmd2.CommandTimeout = TimeSpan.FromSeconds(5);
                rebootResult = (cmd2.Execute() + cmd2.Result).Trim();
                client2.Disconnect();
            }

            var needsReboot = rebootResult.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            progress?.Report(needsReboot
                ? "⚠️ Reboot diperlukan setelah update."
                : "✅ Update selesai, reboot tidak diperlukan.");

            log.AppendLine();
            log.AppendLine("=== reboot-required: " + (needsReboot ? "YES" : "NO") + " ===");

            return new UpdateResult
            {
                Success = exitStatus == 0,
                RebootRequired = needsReboot,
                PackagesUpgraded = packagesUpgraded,
                Log = log.ToString(),
            };
        }
        catch (OperationCanceledException)
        {
            log.AppendLine("=== CANCELED ===");
            return new UpdateResult
            {
                Success = false,
                ErrorMessage = "Update dibatalkan.",
                Log = log.ToString(),
            };
        }
        catch (Exception ex)
        {
            log.AppendLine("=== ERROR: " + ex.Message + " ===");
            return new UpdateResult
            {
                Success = false,
                ErrorMessage = $"Gagal update: {ex.Message}",
                Log = log.ToString(),
            };
        }
    }

    // -------- Parsers --------

    private static IReadOnlyList<PackageUpdate> ParseUpgradableList(string output)
    {
        // Dedupe by package name. apt list --upgradable bisa nampilin package yang sama
        // multiple kali kalau ada duplicate sources (mis. sources.list + sources.list.d).
        // Keep entry dengan NewVersion paling baru.
        var byName = new Dictionary<string, PackageUpdate>(StringComparer.Ordinal);

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Listing", StringComparison.Ordinal)) continue;
            if (line.StartsWith("WARNING", StringComparison.Ordinal)) continue;

            // Pola utama: "nama/repo versi arch [upgradable from: old]"
            var match = Regex.Match(
                line,
                @"^(?<name>[^\s/]+)/(?<repo>[^\s]+)\s+(?<newVer>[^\s]+)\s+(?<arch>[^\s]+)(?:\s+\[upgradable from:\s+(?<oldVer>[^\]]+)\])?");

            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var repo = match.Groups["repo"].Value;
            var newVer = match.Groups["newVer"].Value;
            var oldVer = match.Groups["oldVer"].Success ? match.Groups["oldVer"].Value.Trim() : string.Empty;

            var isSecurity = repo.Contains("-security", StringComparison.OrdinalIgnoreCase);
            var candidate = new PackageUpdate
            {
                Name = name,
                CurrentVersion = oldVer,
                NewVersion = newVer,
                Repository = repo,
                IsSecurity = isSecurity,
            };

            // Kalau sudah ada entry dengan nama sama, keep yang NewVersion-nya "lebih besar"
            // (lexicographic — works untuk versioning Ubuntu)
            if (byName.TryGetValue(name, out var existing))
            {
                if (string.Compare(candidate.NewVersion, existing.NewVersion, StringComparison.Ordinal) > 0)
                    byName[name] = candidate;
            }
            else
            {
                byName[name] = candidate;
            }
        }
        return byName.Values.OrderBy(p => p.Name).ToList();
    }

    private static IReadOnlyList<string> ParseRebootPackages(string output)
    {
        var result = new List<string>();
        var firstLine = true;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (firstLine)
            {
                firstLine = false;
                continue; // YES/NO marker
            }
            result.Add(line);
        }
        return result;
    }

    // -------- Client helper (shared logic with other Ssh* services) --------

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
            Timeout = TimeSpan.FromMinutes(CommandTimeoutMinutes),
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
