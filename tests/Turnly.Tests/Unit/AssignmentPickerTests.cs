using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

public class AssignmentPickerTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid C = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid[] All = [A, B, C];

    private static Guid Pick(
        AssignmentStrategy strategy,
        Guid? current,
        IReadOnlyDictionary<Guid, int>? assigned = null,
        IReadOnlyDictionary<Guid, int>? completed = null,
        int seed = 0) =>
        AssignmentPicker.Pick(strategy, All, current,
            assigned ?? new Dictionary<Guid, int>(),
            completed ?? new Dictionary<Guid, int>(),
            new Random(seed));

    [Fact]
    public void Single_assignee_is_always_picked()
    {
        var pick = AssignmentPicker.Pick(AssignmentStrategy.Random, [A], A,
            new Dictionary<Guid, int>(), new Dictionary<Guid, int>(), new Random(1));
        Assert.Equal(A, pick);
    }

    [Fact]
    public void KeepLastAssigned_keeps_current()
    {
        Assert.Equal(B, Pick(AssignmentStrategy.KeepLastAssigned, B));
    }

    [Fact]
    public void KeepLastAssigned_falls_back_to_first_when_current_ineligible()
    {
        Assert.Equal(A, Pick(AssignmentStrategy.KeepLastAssigned, Guid.NewGuid()));
    }

    [Fact]
    public void RoundRobin_advances_in_order_and_wraps()
    {
        Assert.Equal(B, Pick(AssignmentStrategy.RoundRobin, A));
        Assert.Equal(C, Pick(AssignmentStrategy.RoundRobin, B));
        Assert.Equal(A, Pick(AssignmentStrategy.RoundRobin, C));
    }

    [Fact]
    public void RandomExceptLastAssigned_never_returns_current()
    {
        for (var seed = 0; seed < 20; seed++)
            Assert.NotEqual(B, Pick(AssignmentStrategy.RandomExceptLastAssigned, B, seed: seed));
    }

    [Fact]
    public void LeastAssigned_picks_lowest_count()
    {
        var counts = new Dictionary<Guid, int> { [A] = 5, [B] = 1, [C] = 3 };
        Assert.Equal(B, Pick(AssignmentStrategy.LeastAssigned, A, assigned: counts));
    }

    [Fact]
    public void LeastCompleted_picks_lowest_count_with_stable_tie_break()
    {
        // A and C tie at 0; the stable order [A,B,C] makes A win.
        var counts = new Dictionary<Guid, int> { [B] = 4 };
        Assert.Equal(A, Pick(AssignmentStrategy.LeastCompleted, B, completed: counts));
    }

    [Fact]
    public void Random_stays_within_the_assignee_set()
    {
        for (var seed = 0; seed < 20; seed++)
            Assert.Contains(Pick(AssignmentStrategy.Random, A, seed: seed), All);
    }
}
