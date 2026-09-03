using System.Text.Json;
using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;

namespace VPS_Monitor_Desktop_App.Infrastructure.Storage;

/// <summary>
/// Implementasi <see cref="IUpdateLogService"/> pakai JSON file di <c>FileSystem.AppDataDirectory</c>.
/// Maks 100 entry (FIFO). Atomic write via temp file + replace untuk cegah corruption kalau crash.
/// </summary>
public sealed class JsonUpdateLogService : IUpdateLogService
{
    private const int MaxEntries = 100;
    private const string FileName = "update_logs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public JsonUpdateLogService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
    }

    public async Task<IReadOnlyList<UpdateLogEntry>> GetAllAsync()
    {
        if (!File.Exists(_filePath)) return Array.Empty<UpdateLogEntry>();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var list = await JsonSerializer.DeserializeAsync<List<UpdateLogEntry>>(stream, JsonOptions);
            return list ?? new List<UpdateLogEntry>();
        }
        catch
        {
            // File corrupt — return empty, jangan crash
            return Array.Empty<UpdateLogEntry>();
        }
    }

    public async Task AddAsync(UpdateLogEntry entry)
    {
        var all = (await GetAllAsync()).ToList();
        all.Insert(0, entry); // newest first
        if (all.Count > MaxEntries)
            all = all.Take(MaxEntries).ToList();

        // Atomic write: tulis ke temp, lalu replace
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, all, JsonOptions);
        }
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public Task ClearAllAsync()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
        return Task.CompletedTask;
    }
}
