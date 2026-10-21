using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Notifications;

namespace Turnly.Core.Services;

/// <summary>Reads/writes app-wide configuration: the family timezone used to evaluate quiet hours (so
/// reminders respect "22:00–07:00" regardless of the server's host timezone) and the day start, which
/// decides when an all-day chore stops being "due today".</summary>
public class AppSettingsService
{
    public const string TimeZoneKey = "TimeZone";
    public const string DayStartKey = "DayStart";

    private readonly TurnlyDbContext _db;

    public AppSettingsService(TurnlyDbContext db) => _db = db;

    public async Task<AppSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var tz = await GetTimeZoneIdAsync(ct);
        return new AppSettingsDto(tz, TimeZoneInfo.Local.Id, Format(await ReadDayStartAsync(_db, ct)));
    }

    /// <summary>How far past midnight the household's day runs (zero when unset). Static so the
    /// services that judge lateness can read it off their own context without a dependency on this one.</summary>
    public static async Task<TimeSpan> ReadDayStartAsync(TurnlyDbContext db, CancellationToken ct = default)
    {
        var value = await db.AppSettings
            .Where(s => s.Key == DayStartKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return TimeOnly.TryParseExact(value, "HH:mm", out var time) ? time.ToTimeSpan() : TimeSpan.Zero;
    }

    /// <summary>The lateness grace of every all-day chore, keyed by chore id (see
    /// <see cref="Chore.LateGrace"/>). Timed chores are absent, and so is everything when the day
    /// starts at midnight, so a missing key means "no grace".</summary>
    public static async Task<Dictionary<Guid, TimeSpan>> ReadLateGraceByChoreAsync(TurnlyDbContext db, CancellationToken ct = default)
    {
        var dayStart = await ReadDayStartAsync(db, ct);
        if (dayStart == TimeSpan.Zero)
            return [];
        var chores = await db.Chores
            .Select(c => new { c.Id, c.DueTime, c.TimesOfDay })
            .ToListAsync(ct);
        return chores
            .Where(c => c.DueTime is null && c.TimesOfDay.Count == 0)
            .ToDictionary(c => c.Id, _ => dayStart);
    }

    /// <summary>Sets the day start ("HH:mm", at most noon). Null/empty or "00:00" resets it to midnight.</summary>
    public async Task<Result<AppSettingsDto>> SetDayStartAsync(string? dayStart, CancellationToken ct = default)
    {
        if (Validators.DayStart(dayStart, out var parsed) is { } error)
            return Result.Fail<AppSettingsDto>(error);

        var value = parsed == TimeOnly.MinValue ? string.Empty : parsed.ToString("HH:mm");
        var entity = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == DayStartKey, ct);
        if (entity is null)
            _db.AppSettings.Add(new AppSetting { Key = DayStartKey, Value = value });
        else
            entity.Value = value;

        await _db.SaveChangesAsync(ct);
        return Result.Success(await GetAsync(ct));
    }

    private static string Format(TimeSpan dayStart) => TimeOnly.FromTimeSpan(dayStart).ToString("HH:mm");

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
        return Result.Success(await GetAsync(ct));
    }
}
