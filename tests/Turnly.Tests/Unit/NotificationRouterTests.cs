using Turnly.Core.Enums;
using Turnly.Core.Notifications;

namespace Turnly.Tests.Unit;

public class NotificationRouterTests
{
    private const NotificationCategory Chore = NotificationCategory.ChoreReminder;

    private static DeliveryChannel Resolve(
        DeliveryChannel preference, bool muted = false, bool quiet = false,
        NotificationCategory category = Chore) =>
        NotificationRouter.Resolve(category, preference, muted, quiet);

    [Fact]
    public void Default_preference_reaches_push()
    {
        Assert.Equal(DeliveryChannel.PushAndInbox, Resolve(DeliveryChannel.PushAndInbox));
    }

    [Fact]
    public void Off_drops_the_notification_entirely()
    {
        Assert.Equal(DeliveryChannel.Off, Resolve(DeliveryChannel.Off));
    }

    [Fact]
    public void InboxOnly_keeps_the_record_but_not_the_push()
    {
        Assert.Equal(DeliveryChannel.InboxOnly, Resolve(DeliveryChannel.InboxOnly));
    }

    [Theory]
    [InlineData(true, false)]   // muted
    [InlineData(false, true)]   // quiet hours
    [InlineData(true, true)]    // both
    public void Mute_and_quiet_hours_demote_push_to_inbox(bool muted, bool quiet)
    {
        Assert.Equal(DeliveryChannel.InboxOnly, Resolve(DeliveryChannel.PushAndInbox, muted, quiet));
    }

    [Fact]
    public void Off_still_wins_over_a_mute_or_quiet_hours()
    {
        // Suppression only ever travels one way — nothing promotes a dropped notification back.
        Assert.Equal(DeliveryChannel.Off, Resolve(DeliveryChannel.Off, muted: true, quiet: true));
    }

    [Fact]
    public void Mute_does_not_promote_an_inbox_only_preference()
    {
        Assert.Equal(DeliveryChannel.InboxOnly, Resolve(DeliveryChannel.InboxOnly, muted: true));
    }

    [Fact]
    public void System_category_ignores_every_control()
    {
        // The test push has to prove the delivery path works, so a stale "Off" can't make it lie.
        Assert.Equal(DeliveryChannel.PushAndInbox,
            Resolve(DeliveryChannel.Off, muted: true, quiet: true, category: NotificationCategory.System));
        Assert.False(NotificationRouter.IsConfigurable(NotificationCategory.System));
    }

    [Theory]
    [InlineData(NotificationCategory.ChoreReminder)]
    [InlineData(NotificationCategory.ChoreDue)]
    [InlineData(NotificationCategory.ChoreOverdue)]
    [InlineData(NotificationCategory.OfferReceived)]
    [InlineData(NotificationCategory.OfferResponse)]
    public void Every_other_category_is_configurable(NotificationCategory category)
    {
        Assert.True(NotificationRouter.IsConfigurable(category));
    }

    [Theory]
    [InlineData(NotificationType.Reminder, NotificationCategory.ChoreReminder)]
    [InlineData(NotificationType.Due, NotificationCategory.ChoreDue)]
    [InlineData(NotificationType.FollowUp, NotificationCategory.ChoreOverdue)]
    public void Scheduled_entry_types_map_to_their_category(NotificationType type, NotificationCategory expected)
    {
        Assert.Equal(expected, NotificationRouter.CategoryOf(type));
    }
}
