namespace VPS_Monitor_Desktop_App.Application.DTOs;

/// <summary>
/// Hasil dari test koneksi SSH. Success=true berarti server bisa dihubungi dan kredensial valid.
/// </summary>
public sealed record ConnectionTestResult(
    bool Success,
    string? ErrorMessage,
    string? ServerInfo = null,
    long? LatencyMs = null);
