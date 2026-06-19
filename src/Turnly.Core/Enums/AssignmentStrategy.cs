namespace Turnly.Core.Enums;

/// <summary>How the next current assignee is chosen when a recurring chore advances to a new
/// occurrence on completion.</summary>
public enum AssignmentStrategy
{
    /// <summary>Pick any assignee at random.</summary>
    Random = 0,
    /// <summary>Pick the assignee who has been assigned this chore fewest times.</summary>
    LeastAssigned = 1,
    /// <summary>Pick the assignee who has completed this chore fewest times.</summary>
    LeastCompleted = 2,
    /// <summary>Keep the same person as the previous occurrence (Phase 2 behaviour).</summary>
    KeepLastAssigned = 3,
    /// <summary>Random, but exclude whoever was assigned last.</summary>
    RandomExceptLastAssigned = 4,
    /// <summary>Cycle through the assignees in a stable order.</summary>
    RoundRobin = 5
}
