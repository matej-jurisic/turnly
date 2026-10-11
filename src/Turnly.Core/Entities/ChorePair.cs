namespace Turnly.Core.Entities;

/// <summary>Two assignees who always do a chore together. A value stored on <see cref="Chore.Pairs"/>,
/// not a table: a user sits in at most one pair per chore, so "who is X's partner" is a plain lookup.</summary>
public record ChorePair(Guid First, Guid Second)
{
    /// <summary>The other member when <paramref name="userId"/> is in this pair; null otherwise.</summary>
    public Guid? Other(Guid userId) => userId == First ? Second : userId == Second ? First : null;
}
