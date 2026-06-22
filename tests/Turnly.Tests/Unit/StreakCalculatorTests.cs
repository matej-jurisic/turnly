using Turnly.Core.Entities;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

public class StreakCalculatorTests
{
    private static readonly DateTimeOffset Base = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A completion of the occurrence due on <paramref name="dayOffset"/>, finished
    /// <paramref name="lateHours"/> after the due time (negative = early/on time).</summary>
    private static ChoreCompletion Done(int dayOffset, double lateHours = 0, bool isSkip = false, bool isExpired = false)
    {
        var due = Base.AddDays(dayOffset);
        return new ChoreCompletion
        {
            OccurrenceDueAt = due,
            CompletedAt = due.AddHours(lateHours),
            IsSkip = isSkip,
            IsExpired = isExpired,
        };
    }

    [Fact]
    public void Empty_is_zero()
    {
        Assert.Equal(0, StreakCalculator.CurrentStreak([]));
    }

    [Fact]
    public void All_on_time_counts_every_occurrence()
    {
        var completions = new[] { Done(0, -1), Done(1, -2), Done(2, 0) };
        Assert.Equal(3, StreakCalculator.CurrentStreak(completions));
    }

    [Fact]
    public void Late_most_recent_resets_to_zero()
    {
        // Newest occurrence (day 2) was finished an hour late.
        var completions = new[] { Done(0, -1), Done(1, -1), Done(2, 1) };
        Assert.Equal(0, StreakCalculator.CurrentStreak(completions));
    }

    [Fact]
    public void Late_in_the_middle_stops_the_count()
    {
        // Newest two on time, then a late one further back: streak is the recent run only.
        var completions = new[] { Done(2, -1), Done(3, 0), Done(1, 5), Done(0, -1) };
        Assert.Equal(2, StreakCalculator.CurrentStreak(completions));
    }

    [Fact]
    public void Skip_resets_the_streak()
    {
        var completions = new[] { Done(2, -1), Done(1, isSkip: true), Done(0, -1) };
        Assert.Equal(1, StreakCalculator.CurrentStreak(completions));
    }

    [Fact]
    public void Expired_resets_the_streak()
    {
        var completions = new[] { Done(2, -1), Done(1, isExpired: true), Done(0, -1) };
        Assert.Equal(1, StreakCalculator.CurrentStreak(completions));
    }

    [Fact]
    public void Multi_completion_occurrence_counts_only_when_last_is_on_time()
    {
        // One occurrence (day 1) with two completions: first early, the closing one late → not on time.
        var late = new[]
        {
            Done(1, -2), Done(1, 3), // occurrence closed late
            Done(0, -1),
        };
        Assert.Equal(0, StreakCalculator.CurrentStreak(late));

        // Same occurrence but the closing completion lands on time → counts.
        var onTime = new[]
        {
            Done(1, -3), Done(1, -1),
            Done(0, -1),
        };
        Assert.Equal(2, StreakCalculator.CurrentStreak(onTime));
    }

    [Fact]
    public void Rows_without_an_occurrence_due_are_ignored()
    {
        var completions = new[]
        {
            Done(0, -1),
            new ChoreCompletion { OccurrenceDueAt = null, CompletedAt = Base },
        };
        Assert.Equal(1, StreakCalculator.CurrentStreak(completions));
    }
}
