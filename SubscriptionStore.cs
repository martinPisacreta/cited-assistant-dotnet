namespace AskChefSample;

public sealed record SubscriptionNotice(
    string EventId, string MemberId, bool Active, long Version);

public sealed record QuestionAccess(
    bool Allowed, string Reason, int RemainingQuestions);

public sealed class SubscriptionStore
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly int monthlyLimit;
    private readonly Dictionary<string, SubscriptionNotice> events =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubscriptionNotice> members =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Month, int Count)> usage =
        new(StringComparer.Ordinal);

    public SubscriptionStore(int monthlyLimit, TimeProvider? clock = null)
    {
        if (monthlyLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(monthlyLimit));
        this.monthlyLimit = monthlyLimit;
        this.clock = clock ?? TimeProvider.System;
    }

    public static bool ValidIdentifier(string? value) =>
        value is { Length: >= 1 and <= 100 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Call only after verifying the signature of the original request bytes.
    public string ApplyVerifiedNotice(SubscriptionNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (!ValidIdentifier(notice.EventId) ||
            !ValidIdentifier(notice.MemberId) || notice.Version < 1)
            throw new ArgumentException("Invalid subscription notice.");

        lock (gate)
        {
            if (events.TryGetValue(notice.EventId, out var previous))
            {
                if (previous != notice)
                    throw new ArgumentException("Event ID conflict.");
                return "duplicate";
            }
            if (members.TryGetValue(notice.MemberId, out var current))
            {
                if (notice.Version == current.Version)
                    throw new ArgumentException("Subscription version conflict.");
                if (notice.Version < current.Version)
                {
                    events.Add(notice.EventId, notice);
                    return "stale";
                }
            }
            events.Add(notice.EventId, notice);
            members[notice.MemberId] = notice;
            // Cancellation and reactivation never reset monthly usage.
            return "applied";
        }
    }

    // Identity must come from authentication, never a caller-supplied body ID.
    // Reserve before retrieval/model work; failed requests retain their slot.
    public QuestionAccess TryReserveQuestion(string memberId)
    {
        if (!ValidIdentifier(memberId))
            throw new ArgumentException("Invalid member identifier.");
        lock (gate)
        {
            if (!members.TryGetValue(memberId, out var member) || !member.Active)
                return new(false, "inactive_member", 0);

            var now = clock.GetUtcNow();
            var month = now.Year * 100 + now.Month;
            var count = usage.TryGetValue(memberId, out var current) &&
                current.Month == month ? current.Count : 0;
            if (count >= monthlyLimit)
                return new(false, "monthly_limit", 0);

            usage[memberId] = (month, count + 1);
            return new(true, "reserved", monthlyLimit - count - 1);
        }
    }
}
