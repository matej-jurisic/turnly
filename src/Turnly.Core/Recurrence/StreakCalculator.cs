using Turnly.Core.Entities;

namespace Turnly.Core.Recurrence;

/// <summary>Pure computation of a chore's current "on-time" streak: how many of the most recent
/// occurrences were completed on or before their due date, walking backward from the latest. A late
/// completion, a skipped occurrence, or an auto-expired (missed) occurrence resets the count to 0.
/// Pass the completion rows for one schedule — a whole chore (rotating) or one assignee's track
/// (<see cref="Enums.AssignmentStrategy.Independent"/>).</summary>
public static class StreakCalculator
{
    /// <param name="completions">The completion rows for one schedule (or several, when
    /// <paramref name="userId"/> attributes the streak to a person across a chore).</param>
    /// <param name="userId">When set, an occurrence only continues the streak if the person who
    /// *closed* it (the latest completion) is this user — so on a rotating chore the streak follows
    /// the individual and stops as soon as someone else takes a turn. Null counts the schedule's
    /// streak regardless of who completed each occurrence (the chore- or track-wide streak).</param>
    public static int CurrentStreak(IEnumerable<ChoreCompletion> completions, Guid? userId = null)
    {
        // An occurrence is the set of rows sharing one OccurrenceDueAt (the existing grouping key).
        var occurrences = completions
            .Where(c => c.OccurrenceDueAt.HasValue)
            .GroupBy(c => c.OccurrenceDueAt!.Value)
            .OrderByDescending(g => g.Key) // newest occurrence first
            .ToList();

        var streak = 0;
        foreach (var occ in occurrences)
        {
            // A skipped or missed occurrence in the chain stops the streak.
            if (occ.Any(c => c.IsSkip || c.IsExpired)) break;
            // The closing completion (latest) decides on-time-ness — so a multi-completion occurrence
            // counts only once fully done — and, when scoped, whose streak it is.
            var closing = occ.OrderByDescending(c => c.CompletedAt).First();
            if (closing.CompletedAt > occ.Key) break; // late
            if (userId is { } uid && closing.CompletedByUserId != uid) break; // closed by someone else
            streak++;
        }
        return streak;
    }
}
