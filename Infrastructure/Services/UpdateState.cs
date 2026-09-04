using System.Text;
using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Services;

/// <summary>
/// Implementasi <see cref="IUpdateState"/>. Hold CancellationToken di level service
/// supaya long-running process (update, reboot) gak ke-cancel waktu navigate.
/// </summary>
public sealed class UpdateState : IUpdateState, IDisposable
{
    private readonly IUpdateService _update;
    private readonly IServerControlService _server;
    private readonly IUpdateLogService _logService;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public UpdateState(IUpdateService update, IServerControlService server, IUpdateLogService logService)
    {
        _update = update;
        _server = server;
        _logService = logService;
    }

    public UpdateSummary? Summary { get; private set; }
    public UpdateResult? Result { get; private set; }
    public string ProgressLog { get; private set; } = string.Empty;
    public string? RebootStatus { get; private set; }
    public string RebootStatusIcon { get; private set; } = string.Empty;
    public string RebootStatusClass { get; private set; } = string.Empty;

    public bool IsApplying { get; private set; }
    public bool IsRebooting { get; private set; }
    public bool IsRebootPolling { get; private set; }
    // CheckAsync pakai flag yang sama dengan apply, jadi IsChecking proxy ke IsApplying.
    public bool IsChecking => IsApplying;
    public bool IsBusy => IsApplying || IsRebooting || IsRebootPolling;

    public event Action? StateChanged;

    public async Task CheckAsync(SshConnectionConfig config)
    {
        if (IsBusy) return;
        var token = AcquireToken();
        IsApplying = true; // pakai IsApplying sebagai "in-progress" umum saat cek
        ProgressLog = string.Empty;
        RaiseStateChanged();
        try
        {
            Summary = await _update.GetAvailableUpdatesAsync(config, token);
            if (Summary.ErrorMessage is not null)
            {
                ProgressLog = "❌ " + Summary.ErrorMessage + "\n";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ProgressLog = "❌ Gagal cek update: " + ex.Message + "\n";
        }
        finally
        {
            IsApplying = false;
            RaiseStateChanged();
        }
    }

    public async Task RefreshCacheAsync(SshConnectionConfig config)
    {
        if (IsBusy) return;
        var token = AcquireToken();
        IsApplying = true;
        ProgressLog = string.Empty;
        Result = null;
        var logBuilder = new StringBuilder();
        var progress = new Progress<string>(line =>
        {
            ProgressLog += line + "\n";
            lock (logBuilder) logBuilder.AppendLine(line);
            RaiseStateChanged();
        });

        try
        {
            Result = await _update.RefreshAptCacheAsync(config, progress);
            await _logService.AddAsync(new UpdateLogEntry
            {
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow,
                Action = "check",
                Success = Result.Success,
                Summary = "Refresh apt cache (apt clean + apt update).",
                FullLog = logBuilder.ToString(),
                ErrorMessage = Result.ErrorMessage,
            });
            // Re-check setelah refresh
            Summary = await _update.GetAvailableUpdatesAsync(config, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ProgressLog += "❌ Gagal refresh cache: " + ex.Message + "\n";
        }
        finally
        {
            IsApplying = false;
            RaiseStateChanged();
        }
    }

    public async Task<UpdateResult> ApplyUpgradeAsync(SshConnectionConfig config) =>
        await RunApplyAsync(config, (cfg, prog) => _update.ApplyUpdatesAsync(cfg, prog), "update",
            r => $"Update selesai. {r.PackagesUpgraded} package di-upgrade." + (r.RebootRequired ? " Reboot diperlukan." : ""));

    public async Task<UpdateResult> ApplyFullUpgradeAsync(SshConnectionConfig config) =>
        await RunApplyAsync(config, (cfg, prog) => _update.ApplyFullUpgradeAsync(cfg, prog), "force-upgrade",
            r => $"Force upgrade selesai. {r.PackagesUpgraded} package di-upgrade." + (r.RebootRequired ? " Reboot diperlukan." : ""));

    public async Task<UpdateResult> InstallPackagesAsync(SshConnectionConfig config, IReadOnlyList<string> packageNames)
    {
        if (IsBusy) return new UpdateResult { Success = false, ErrorMessage = "Proses lain sedang jalan." };
        return await RunApplyAsync(config,
            (cfg, prog) => _update.InstallPackagesAsync(cfg, packageNames, prog),
            "install-by-name",
            r => $"Install by name selesai: {string.Join(", ", packageNames)}. {r.PackagesUpgraded} package di-upgrade." + (r.RebootRequired ? " Reboot diperlukan." : ""));
    }

    private async Task<UpdateResult> RunApplyAsync(
        SshConnectionConfig config,
        Func<SshConnectionConfig, IProgress<string>, Task<UpdateResult>> runner,
        string action,
        Func<UpdateResult, string> summaryOk)
    {
        if (IsBusy) return new UpdateResult { Success = false, ErrorMessage = "Proses lain sedang jalan." };
        var token = AcquireToken();
        IsApplying = true;
        ProgressLog = string.Empty;
        Result = null;
        RaiseStateChanged();
        var startedAt = DateTime.UtcNow;
        var logBuilder = new StringBuilder();
        var progress = new Progress<string>(line =>
        {
            ProgressLog += line + "\n";
            lock (logBuilder) logBuilder.AppendLine(line);
            RaiseStateChanged();
        });

        try
        {
            Result = await runner(config, progress);
            await _logService.AddAsync(new UpdateLogEntry
            {
                StartedAt = startedAt,
                FinishedAt = DateTime.UtcNow,
                Action = action,
                Success = Result.Success,
                PackagesUpgraded = Result.PackagesUpgraded,
                RebootRequired = Result.RebootRequired,
                Summary = Result.Success ? summaryOk(Result) : $"{action} gagal.",
                FullLog = logBuilder.ToString(),
                ErrorMessage = Result.ErrorMessage,
            });
            // Re-check summary setelah apply
            try { Summary = await _update.GetAvailableUpdatesAsync(config, token); }
            catch { /* summary refresh best-effort */ }
            return Result;
        }
        catch (OperationCanceledException)
        {
            return new UpdateResult { Success = false, ErrorMessage = "Update dibatalkan." };
        }
        catch (Exception ex)
        {
            ProgressLog += "❌ Gagal: " + ex.Message + "\n";
            return new UpdateResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            IsApplying = false;
            RaiseStateChanged();
        }
    }

    public async Task RebootAsync(SshConnectionConfig config)
    {
        if (IsBusy) return;
        var token = AcquireToken();
        IsRebooting = true;
        SetRebootStatus("🔌 Mengirim perintah shutdown...", "🔌", "alert-info");
        var startedAt = DateTime.UtcNow;
        var rebootSuccess = false;
        var rebootError = (string?)null;

        try
        {
            await _server.RebootAsync(config, delayMinutes: 1);
            rebootSuccess = true;
        }
        catch (Exception ex) { rebootError = ex.Message; }

        await _logService.AddAsync(new UpdateLogEntry
        {
            StartedAt = startedAt,
            FinishedAt = DateTime.UtcNow,
            Action = "reboot",
            Success = rebootSuccess,
            RebootPerformed = true,
            Summary = rebootSuccess ? "Perintah reboot dikirim (delay 1 menit)." : $"Gagal kirim reboot: {rebootError}",
            ErrorMessage = rebootError,
        });

        SetRebootStatus("⏳ Menunggu SSH drop (~30-60 detik)...", "⏳", "alert-info");
        try { await Task.Delay(TimeSpan.FromSeconds(30), token); } catch { }

        // Poll server
        var pollingResult = await PollServerOnline(config);
        await _logService.AddAsync(new UpdateLogEntry
        {
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            FinishedAt = DateTime.UtcNow,
            Action = "check",
            Success = pollingResult.online,
            RebootRequired = pollingResult.stillRebootRequired,
            Summary = pollingResult.online ? "Server kembali online setelah reboot." : (pollingResult.error ?? "Server masih offline."),
            ErrorMessage = pollingResult.online ? null : pollingResult.error,
        });

        // Reboot selesai → bersihkan state yang trigger "Reboot diperlukan" warning
        // dan modal progress dari Apply step. Server baru saja reboot, jadi flag
        // reboot-required dari summary sebelumnya sudah tidak relevan.
        if (pollingResult.online)
        {
            Result = null;
            ProgressLog = string.Empty;
            if (Summary is not null)
            {
                Summary.RebootRequired = false;
            }
            SetRebootStatus("✅ Reboot selesai. Server siap digunakan.", "✅", "alert-success");
        }
    }

    private async Task<(bool online, bool stillRebootRequired, string? error)> PollServerOnline(SshConnectionConfig config)
    {
        IsRebootPolling = true;
        SetRebootStatus("🔍 Polling apakah server sudah online kembali...", "🔍", "alert-info");
        var maxAttempts = 60;
        for (int i = 0; i < maxAttempts; i++)
        {
            if (await _server.PingAsync(config))
            {
                SetRebootStatus("✅ Server kembali online! Auto-refresh data...", "✅", "alert-success");
                IsRebootPolling = false;
                IsRebooting = false;
                try { Summary = await _update.GetAvailableUpdatesAsync(config); }
                catch { }
                RaiseStateChanged();
                return (true, Summary?.RebootRequired ?? false, null);
            }
            SetRebootStatus($"🔍 Menunggu server online... (percobaan {i + 1}/{maxAttempts}, ~{(i + 1) * 5}s)", "🔍", "alert-info");
            try { await Task.Delay(TimeSpan.FromSeconds(5)); } catch { }
        }
        var errMsg = "Server masih offline setelah 5 menit. Cek manual via console provider VPS Anda.";
        SetRebootStatus($"❌ {errMsg}", "❌", "alert-danger");
        IsRebootPolling = false;
        IsRebooting = false;
        return (false, false, errMsg);
    }

    public void ClearResult()
    {
        Result = null;
        ProgressLog = string.Empty;
        RaiseStateChanged();
    }

    private CancellationToken AcquireToken()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    private void SetRebootStatus(string message, string icon, string cssClass)
    {
        RebootStatus = message;
        RebootStatusIcon = icon;
        RebootStatusClass = cssClass;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); } catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
