using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>Pure selection of the next current assignee when a recurring chore advances to a new
/// occurrence. Counts come from the database (assignment history + completions); the
/// <see cref="Random"/> is injected so tests can seed it. Generic over what takes a turn: a user id,
/// or whatever the caller rotates as one unit (a fixed pair).</summary>
public static class AssignmentPicker
{
    /// <param name="orderedAssignees">Eligible assignees in a stable order (e.g. by created-at);
    /// must be non-empty.</param>
    /// <param name="current">The current assignee, if any.</param>
    /// <param name="assignedCounts">Times each user has been assigned this chore.</param>
    /// <param name="completedCounts">Times each user has completed this chore.</param>
    /// <param name="lastAssignedAt">When each user was most recently assigned this chore (tie-break
    /// for <see cref="AssignmentStrategy.LeastAssigned"/>); missing = never assigned.</param>
    /// <param name="lastCompletedAt">When each user most recently completed this chore (tie-break
    /// for <see cref="AssignmentStrategy.LeastCompleted"/>); missing = never completed.</param>
    public static T Pick<T>(
        AssignmentStrategy strategy,
        IReadOnlyList<T> orderedAssignees,
        T? current,
        IReadOnlyDictionary<T, int> assignedCounts,
        IReadOnlyDictionary<T, int> completedCounts,
        IReadOnlyDictionary<T, DateTimeOffset> lastAssignedAt,
        IReadOnlyDictionary<T, DateTimeOffset> lastCompletedAt,
        Random rng) where T : struct
    {
        if (orderedAssignees.Count == 0)
            throw new ArgumentException("At least one assignee is required.", nameof(orderedAssignees));
        if (orderedAssignees.Count == 1)
            return orderedAssignees[0];

        return strategy switch
        {
            AssignmentStrategy.Random => orderedAssignees[rng.Next(orderedAssignees.Count)],
            AssignmentStrategy.RandomExceptLastAssigned => RandomExcept(orderedAssignees, current, rng),
            AssignmentStrategy.LeastAssigned => MinBy(orderedAssignees,
                u => assignedCounts.GetValueOrDefault(u),
                u => lastAssignedAt.GetValueOrDefault(u, DateTimeOffset.MinValue)),
            AssignmentStrategy.LeastCompleted => MinBy(orderedAssignees,
                u => completedCounts.GetValueOrDefault(u),
                u => lastCompletedAt.GetValueOrDefault(u, DateTimeOffset.MinValue)),
            AssignmentStrategy.RoundRobin => NextInOrder(orderedAssignees, current),
            AssignmentStrategy.KeepLastAssigned => current is { } c && orderedAssignees.Contains(c) ? c : orderedAssignees[0],
            _ => orderedAssignees[0],
        };
    }

    private static T RandomExcept<T>(IReadOnlyList<T> assignees, T? current, Random rng) where T : struct
    {
        var pool = current is { } c ? assignees.Where(a => !a.Equals(c)).ToList() : assignees.ToList();
        if (pool.Count == 0) pool = assignees.ToList(); // current was the only one — fall back
        return pool[rng.Next(pool.Count)];
    }

    /// <summary>Lowest count wins; ties broken by least-recently (oldest <paramref name="lastAt"/>,
    /// so never-assigned/completed users — who map to <see cref="DateTimeOffset.MinValue"/> — win);
    /// any remaining tie falls back to the stable assignee order (first such wins).</summary>
    private static T MinBy<T>(IReadOnlyList<T> assignees, Func<T, int> count, Func<T, DateTimeOffset> lastAt)
    {
        var best = assignees[0];
        var bestCount = count(best);
        var bestAt = lastAt(best);
        foreach (var a in assignees.Skip(1))
        {
            var c = count(a);
            var t = lastAt(a);
            if (c < bestCount || (c == bestCount && t < bestAt)) { best = a; bestCount = c; bestAt = t; }
        }
        return best;
    }

    private static T NextInOrder<T>(IReadOnlyList<T> assignees, T? current) where T : struct
    {
        var idx = current is { } c ? assignees.IndexOf(c) : -1;
        return assignees[(idx + 1) % assignees.Count];
    }

    private static int IndexOf<T>(this IReadOnlyList<T> list, T value) where T : struct
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].Equals(value)) return i;
        return -1;
    }
}
