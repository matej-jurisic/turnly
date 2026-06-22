using Turnly.Core.Entities;

namespace Turnly.Core.Recurrence;

/// <summary>Pure computation of a chore's current "on-time" streak: how many of the most recent
/// occurrences were completed on or before their due date, walking backward from the latest. A late
/// completion, a skipped occurrence, or an auto-expired (missed) occurrence resets the count to 0.
/// Pass the completion rows for one schedule — a whole chore (rotating) or one assignee's track
/// (<see cref="Enums.AssignmentStrategy.Independent"/>).</summary>
public static class StreakCalculator
{
    public static int CurrentStreak(IEnumerable<ChoreCompletion> completions)
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
            // On time only when the closing completion landed on/before the due date — so a
            // multi-completion occurrence counts only once fully done in time.
            if (occ.Max(c => c.CompletedAt) <= occ.Key) streak++;
            else break;
        }
        return streak;
    }
}
