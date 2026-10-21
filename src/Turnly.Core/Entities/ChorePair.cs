namespace Turnly.Core.Entities;

/// <summary>Two assignees who do a chore together. A value stored on <see cref="Chore.Pairs"/>, not a
/// table. One person may sit in several pairs, so which pair holds the turn is stored on the chore
/// (<see cref="Chore.CurrentPartnerId"/>) rather than looked up here.</summary>
public record ChorePair(Guid First, Guid Second)
{
    /// <summary>The other member when <paramref name="userId"/> is in this pair; null otherwise.</summary>
    public Guid? Other(Guid userId) => userId == First ? Second : userId == Second ? First : null;

    public bool Has(Guid userId) => userId == First || userId == Second;

    /// <summary>Whether this is the pair of these two people, in either order.</summary>
    public bool Is(Guid a, Guid b) => a != b && Has(a) && Has(b);
}
