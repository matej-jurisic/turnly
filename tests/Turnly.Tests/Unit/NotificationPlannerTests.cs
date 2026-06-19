using Turnly.Core.Entities;
using Turnly.Core.Enums;
using Turnly.Core.Notifications;

namespace Turnly.Tests.Unit;

public class NotificationPlannerTests
{
    private static readonly DateTimeOffset Due = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private static ChoreNotification Entry(NotificationTiming timing, int value, NotificationOffsetUnit unit) =>
        new() { Timing = timing, OffsetValue = value, OffsetUnit = unit };

    [Fact]
    public void AtDue_fires_at_the_due_time()
    {
        var fire = NotificationPlanner.FireTime(Entry(NotificationTiming.AtDue, 30, NotificationOffsetUnit.Minutes), Due);
        Assert.Equal(Due, fire);
    }

    [Theory]
    [InlineData(NotificationOffsetUnit.Minutes, 30)]
    [InlineData(NotificationOffsetUnit.Hours, 2)]
    [InlineData(NotificationOffsetUnit.Days, 1)]
    public void Before_subtracts_the_offset(NotificationOffsetUnit unit, int value)
    {
        var fire = NotificationPlanner.FireTime(Entry(NotificationTiming.Before, value, unit), Due);
        var expected = unit switch
        {
            NotificationOffsetUnit.Minutes => Due.AddMinutes(-value),
            NotificationOffsetUnit.Hours => Due.AddHours(-value),
            _ => Due.AddDays(-value)
        };
        Assert.Equal(expected, fire);
    }

    [Fact]
    public void After_adds_the_offset()
    {
        var fire = NotificationPlanner.FireTime(Entry(NotificationTiming.After, 3, NotificationOffsetUnit.Hours), Due);
        Assert.Equal(Due.AddHours(3), fire);
    }
}
