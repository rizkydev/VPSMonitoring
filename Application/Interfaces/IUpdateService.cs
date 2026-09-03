using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Service untuk operasi update server via SSH (apt). Hanya jalan untuk Debian/Ubuntu family.
/// Untuk OS upgrade (do-release-upgrade) tidak di-handle di sini — terlalu berisiko.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Cek daftar package yang bisa di-update. Termasuk deteksi security update &amp;
    /// apakah reboot diperlukan (cek <c>/var/run/reboot-required</c>).
    /// </summary>
    Task<UpdateSummary> GetAvailableUpdatesAsync(SshConnectionConfig config, CancellationToken ct = default);

    /// <summary>
    /// Jalankan <c>apt-get upgrade -y</c>. Output di-stream via <paramref name="progress"/>.
    /// TIDAK melakukan OS upgrade (do-release-upgrade) — itu terlalu berbahaya untuk automasi.
    /// </summary>
    Task<UpdateResult> ApplyUpdatesAsync(
        SshConnectionConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Jalankan <c>apt full-upgrade -y</c> (lebih agresif dari <see cref="ApplyUpdatesAsync"/>).
    /// Bisa bypass Ubuntu phased updates &amp; held-back package, tapi MUNGKIN remove package
    /// yang dianggap obsolete. Hanya dipakai kalau <see cref="ApplyUpdatesAsync"/> tidak apply.
    /// </summary>
    Task<UpdateResult> ApplyFullUpgradeAsync(
        SshConnectionConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Force install package tertentu dengan <c>apt install -y --only-upgrade &lt;names&gt;</c>.
    /// Paling granular — bypass phased, held, dan cache issue sekaligus.
    /// </summary>
    Task<UpdateResult> InstallPackagesAsync(
        SshConnectionConfig config,
        IReadOnlyList<string> packageNames,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Clean apt cache + re-fetch index. Fix untuk stale cache yang bikin package "stuck".
    /// </summary>
    Task<UpdateResult> RefreshAptCacheAsync(
        SshConnectionConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
