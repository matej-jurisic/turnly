using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>Pure selection of the next current assignee when a recurring chore advances to a new
/// occurrence. Counts come from the database (assignment history + completions); the
/// <see cref="Random"/> is injected so tests can seed it.</summary>
public static class AssignmentPicker
{
    /// <param name="orderedAssignees">Eligible assignees in a stable order (e.g. by created-at);
    /// must be non-empty.</param>
    /// <param name="current">The current assignee, if any.</param>
    /// <param name="assignedCounts">Times each user has been assigned this chore.</param>
    /// <param name="completedCounts">Times each user has completed this chore.</param>
    public static Guid Pick(
        AssignmentStrategy strategy,
        IReadOnlyList<Guid> orderedAssignees,
        Guid? current,
        IReadOnlyDictionary<Guid, int> assignedCounts,
        IReadOnlyDictionary<Guid, int> completedCounts,
        Random rng)
    {
        if (orderedAssignees.Count == 0)
            throw new ArgumentException("At least one assignee is required.", nameof(orderedAssignees));
        if (orderedAssignees.Count == 1)
            return orderedAssignees[0];

        return strategy switch
        {
            AssignmentStrategy.Random => orderedAssignees[rng.Next(orderedAssignees.Count)],
            AssignmentStrategy.RandomExceptLastAssigned => RandomExcept(orderedAssignees, current, rng),
            AssignmentStrategy.LeastAssigned => MinBy(orderedAssignees, u => assignedCounts.GetValueOrDefault(u)),
            AssignmentStrategy.LeastCompleted => MinBy(orderedAssignees, u => completedCounts.GetValueOrDefault(u)),
            AssignmentStrategy.RoundRobin => NextInOrder(orderedAssignees, current),
            AssignmentStrategy.KeepLastAssigned => current is { } c && orderedAssignees.Contains(c) ? c : orderedAssignees[0],
            _ => orderedAssignees[0],
        };
    }

    private static Guid RandomExcept(IReadOnlyList<Guid> assignees, Guid? current, Random rng)
    {
        var pool = current is { } c ? assignees.Where(a => a != c).ToList() : assignees.ToList();
        if (pool.Count == 0) pool = assignees.ToList(); // current was the only one — fall back
        return pool[rng.Next(pool.Count)];
    }

    /// <summary>Lowest count wins; ties broken by the stable assignee order (first such wins).</summary>
    private static Guid MinBy(IReadOnlyList<Guid> assignees, Func<Guid, int> count)
    {
        var best = assignees[0];
        var bestCount = count(best);
        foreach (var a in assignees.Skip(1))
        {
            var c = count(a);
            if (c < bestCount) { best = a; bestCount = c; }
        }
        return best;
    }

    private static Guid NextInOrder(IReadOnlyList<Guid> assignees, Guid? current)
    {
        var idx = current is { } c ? assignees.IndexOf(c) : -1;
        return assignees[(idx + 1) % assignees.Count];
    }

    private static int IndexOf(this IReadOnlyList<Guid> list, Guid value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }
}
