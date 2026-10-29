using AskChefSample;
using Xunit;

namespace AskChefTests;

public sealed class SubscriptionStoreTests
{
    private static SubscriptionNotice Notice(
        string eventId, bool active = true, long version = 1) =>
        new(eventId, "member-a", active, version);

    [Fact]
    public void Duplicate_event_is_idempotent()
    {
        var store = new SubscriptionStore(2);
        var notice = Notice("activate-a");
        Assert.Equal("applied", store.ApplyVerifiedNotice(notice));
        Assert.Equal("duplicate", store.ApplyVerifiedNotice(notice));
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
    }

    [Fact]
    public void Reusing_event_id_with_different_data_is_rejected()
    {
        var store = new SubscriptionStore(2);
        store.ApplyVerifiedNotice(Notice("event-a"));
        Assert.Throws<ArgumentException>(() =>
            store.ApplyVerifiedNotice(Notice("event-a", false, 2)));
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
    }

    [Fact]
    public void Older_activation_cannot_reverse_cancellation()
    {
        var store = new SubscriptionStore(2);
        store.ApplyVerifiedNotice(Notice("cancel-a", false, 3));
        Assert.Equal("stale",
            store.ApplyVerifiedNotice(Notice("old-activation", true, 2)));
        Assert.Equal("inactive_member",
            store.TryReserveQuestion("member-a").Reason);
    }

    [Fact]
    public void Same_version_with_another_event_is_rejected()
    {
        var store = new SubscriptionStore(2);
        store.ApplyVerifiedNotice(Notice("activate-a"));
        Assert.Throws<ArgumentException>(() =>
            store.ApplyVerifiedNotice(Notice("cancel-a", false)));
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
    }

    [Fact]
    public void Reactivation_does_not_reset_monthly_usage()
    {
        var store = new SubscriptionStore(1);
        store.ApplyVerifiedNotice(Notice("activate-a"));
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
        store.ApplyVerifiedNotice(Notice("cancel-a", false, 2));
        store.ApplyVerifiedNotice(Notice("reactivate-a", true, 3));
        Assert.Equal("monthly_limit",
            store.TryReserveQuestion("member-a").Reason);
    }

    [Fact]
    public void New_utc_month_restores_allowance()
    {
        var clock = new MutableClock();
        var store = new SubscriptionStore(1, clock);
        store.ApplyVerifiedNotice(Notice("activate-a"));
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
        Assert.False(store.TryReserveQuestion("member-a").Allowed);
        clock.Now = clock.Now.AddSeconds(1);
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
        Assert.False(store.TryReserveQuestion("member-a").Allowed);
    }

    [Fact]
    public void Monthly_allowances_are_separate_by_member()
    {
        var store = new SubscriptionStore(1);
        store.ApplyVerifiedNotice(Notice("activate-a"));
        store.ApplyVerifiedNotice(new("activate-b", "member-b", true, 1));
        Assert.True(store.TryReserveQuestion("member-a").Allowed);
        Assert.False(store.TryReserveQuestion("member-a").Allowed);
        Assert.True(store.TryReserveQuestion("member-b").Allowed);
    }

    [Fact]
    public void Concurrent_reservations_cannot_exceed_limit()
    {
        var store = new SubscriptionStore(5);
        store.ApplyVerifiedNotice(Notice("activate-a"));
        var admitted = 0;
        Parallel.For(0, 100, _ =>
        {
            if (store.TryReserveQuestion("member-a").Allowed)
                Interlocked.Increment(ref admitted);
        });
        Assert.Equal(5, admitted);
        Assert.Equal("monthly_limit",
            store.TryReserveQuestion("member-a").Reason);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            new(2026, 10, 31, 23, 59, 59, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
