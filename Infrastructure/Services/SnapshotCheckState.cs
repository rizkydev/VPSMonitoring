using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Services;

/// <summary>
/// Implementasi <see cref="ISnapshotCheckState"/> yang hold CancellationToken
/// di level service (singleton). Dipakai supaya "Cek Sekarang" gak ke-cancel
/// waktu user navigate ke page lain.
/// </summary>
public sealed class SnapshotCheckState : ISnapshotCheckState, IDisposable
{
    private readonly IVpsMonitorService _monitor;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _isChecking;

    public SnapshotCheckState(IVpsMonitorService monitor)
    {
        _monitor = monitor;
    }

    public SystemSnapshot? Current { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }
    public string? LastError { get; private set; }
    public bool IsChecking => _isChecking;
    public event Action? StateChanged;

    public async Task CheckAsync(SshConnectionConfig config)
    {
        // Anti double-click: kalau sudah ada proses, skip
        if (_isChecking) return;

        // Cancel proses sebelumnya (kalau ada) — defensive
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }

        _isChecking = true;
        LastError = null;
        RaiseStateChanged();

        CancellationToken token;
        lock (_lock) { token = _cts!.Token; }

        try
        {
            var snapshot = await _monitor.GetSnapshotAsync(config, token);
            Current = snapshot;
            LastCheckedAt = DateTime.UtcNow;
            if (!snapshot.IsOnline || snapshot.ErrorMessage is not null)
            {
                LastError = snapshot.ErrorMessage ?? "Server tidak merespons.";
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — bukan error, jangan set LastError
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            _isChecking = false;
            RaiseStateChanged();
        }
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); } catch { /* ignore subscriber errors */ }
    }

    public void Dispose()
    {
        // Service di-dispose cuma saat app shutdown. Cancel ongoing operation.
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
