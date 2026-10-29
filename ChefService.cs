namespace AskChefSample;

public sealed class ChefAccessException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

public sealed class ChefService(
    LessonLibrary library, SubscriptionStore subscriptions, IChefModel model,
    TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<(string Member, string Conversation),
        List<ChefTurn>> conversations = new();
    private const int HistoryLimit = 20;
    private const string OutsideMaterial =
        "I'd be happy to help with the sample lessons, but I don't have " +
        "a passage that answers that question. Please ask about roasting " +
        "vegetables, pasta water, or vinaigrette.";
    private const string UnverifiedAnswer =
        "I couldn't verify an answer against the sample lesson. " +
        "Please try again or review the lesson directly.";

    public async Task<ChefReply> ReplyAsync(
        string memberId, ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!SubscriptionStore.ValidIdentifier(memberId))
            throw new ArgumentException("Invalid authenticated member.");
        if (!SubscriptionStore.ValidIdentifier(request.ConversationId))
            throw new ArgumentException("Invalid conversation identifier.");
        if (string.IsNullOrWhiteSpace(request.Question) ||
            request.Question.Length > 2000)
            throw new ArgumentException("Question must contain 1-2000 characters.");

        // Serializes context updates and admission within this process.
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var access = subscriptions.TryReserveQuestion(memberId);
            if (!access.Allowed) throw new ChefAccessException(access.Reason);

            var key = (memberId, request.ConversationId);
            if (!conversations.TryGetValue(key, out var history))
            {
                history = [];
                conversations.Add(key, history);
            }
            var question = request.Question.Trim();
            var evidence = library.Find(question, history);
            var answer = OutsideMaterial;
            string? source = null;
            var refused = true;

            if (evidence is not null)
            {
                string? candidate = null;
                var input = new ChefModelInput(question, evidence,
                    Array.AsReadOnly(history.ToArray()));
                try
                {
                    candidate = await model.RespondAsync(
                        input, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    // Fail closed without disclosing dependency error details.
                    // The question reservation remains consumed.
                }

                var verified = model is ICitedChefModel cited
                    ? cited.IsValidAnswer(input, candidate ?? string.Empty)
                    : string.Equals(candidate, evidence.Text,
                        StringComparison.Ordinal);
                if (verified)
                {
                    source = evidence.Source;
                    answer = candidate + "\n\nSource: " + source;
                    refused = false;
                }
                else answer = UnverifiedAnswer;
            }

            cancellationToken.ThrowIfCancellationRequested();
            history.Add(new ChefTurn(question, answer, source, time.GetUtcNow()));
            if (history.Count > HistoryLimit)
                history.RemoveRange(0, history.Count - HistoryLimit);
            return new ChefReply(answer, source, refused,
                access.RemainingQuestions, Array.AsReadOnly(history.ToArray()));
        }
        finally { gate.Release(); }
    }
}
