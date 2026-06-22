using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Notifications;

namespace Turnly.Core.Services;

/// <summary>Reads/writes app-wide configuration. Currently just the family timezone used to evaluate
/// quiet hours (so reminders respect "22:00–07:00" regardless of the server's host timezone).</summary>
public class AppSettingsService
{
    public const string TimeZoneKey = "TimeZone";

    private readonly TurnlyDbContext _db;

    public AppSettingsService(TurnlyDbContext db) => _db = db;

    public async Task<AppSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var tz = await GetTimeZoneIdAsync(ct);
        return new AppSettingsDto(tz, TimeZoneInfo.Local.Id);
    }

    /// <summary>The configured timezone id, or null when unset.</summary>
    public async Task<string?> GetTimeZoneIdAsync(CancellationToken ct = default)
    {
        var value = await _db.AppSettings
            .Where(s => s.Key == TimeZoneKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Sets (or, with null/empty, clears) the family timezone. Clearing falls the quiet-hours
    /// evaluation back to the server's local zone.</summary>
    public async Task<Result<AppSettingsDto>> SetTimeZoneAsync(string? timeZoneId, CancellationToken ct = default)
    {
        var trimmed = timeZoneId?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && !TimeZoneResolver.IsValid(trimmed))
            return Result.Fail<AppSettingsDto>(Error.Validation($"Unknown timezone '{trimmed}'."));

        var entity = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == TimeZoneKey, ct);
        if (entity is null)
            _db.AppSettings.Add(new AppSetting { Key = TimeZoneKey, Value = trimmed });
        else
            entity.Value = trimmed;

        await _db.SaveChangesAsync(ct);
        return Result.Success(new AppSettingsDto(trimmed.Length == 0 ? null : trimmed, TimeZoneInfo.Local.Id));
    }
}
