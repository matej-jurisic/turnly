using Turnly.Core.Notifications;

namespace Turnly.Tests.Unit;

public class QuietHoursTests
{
    private static TimeOnly T(int h, int m = 0) => new(h, m);

    [Fact]
    public void Disabled_when_either_end_null()
    {
        Assert.False(QuietHours.Contains(null, T(7), T(3)));
        Assert.False(QuietHours.Contains(T(22), null, T(3)));
        Assert.False(QuietHours.Contains(null, null, T(3)));
    }

    [Fact]
    public void Zero_length_window_is_never_quiet()
    {
        Assert.False(QuietHours.Contains(T(8), T(8), T(8)));
    }

    [Theory]
    [InlineData(8, 17, 7, false)]   // before window
    [InlineData(8, 17, 8, true)]    // inclusive start
    [InlineData(8, 17, 12, true)]   // inside
    [InlineData(8, 17, 17, false)]  // exclusive end
    [InlineData(8, 17, 20, false)]  // after window
    public void Same_day_window(int start, int end, int now, bool expected)
    {
        Assert.Equal(expected, QuietHours.Contains(T(start), T(end), T(now)));
    }

    [Theory]
    [InlineData(22, 7, 23, true)]   // late evening, inside
    [InlineData(22, 7, 3, true)]    // after midnight, inside
    [InlineData(22, 7, 22, true)]   // inclusive start
    [InlineData(22, 7, 7, false)]   // exclusive end
    [InlineData(22, 7, 12, false)]  // midday, outside
    public void Overnight_window_spans_midnight(int start, int end, int now, bool expected)
    {
        Assert.Equal(expected, QuietHours.Contains(T(start), T(end), T(now)));
    }
}
