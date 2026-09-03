using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Interfaces;

/// <summary>
/// Service untuk menyimpan histori log operasi update/reboot. Persist ke local storage
/// (JSON file di <c>FileSystem.AppDataDirectory</c>) supaya tetap ada saat app ditutup.
/// </summary>
public interface IUpdateLogService
{
    Task<IReadOnlyList<UpdateLogEntry>> GetAllAsync();
    Task AddAsync(UpdateLogEntry entry);
    Task ClearAllAsync();
}
