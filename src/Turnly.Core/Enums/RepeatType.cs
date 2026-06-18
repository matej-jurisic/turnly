namespace Turnly.Core.Enums;

/// <summary>
/// Basic recurrence types (Phase 2). Custom recurrence (interval / frequency /
/// days-of-month) is a Phase 3 extension and intentionally absent here.
/// </summary>
public enum RepeatType
{
    OneTime = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4
}
