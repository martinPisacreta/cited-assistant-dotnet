namespace AskChefSample;

// Member identity will come from authentication, never from the chat body.
public sealed record ChatRequest(string ConversationId, string Question);

public sealed record LessonPassage(
    string Id, string Title, string Source, string Text);

public sealed record ChefTurn(
    string Question, string Answer, string? Source, DateTimeOffset RecordedAt);

public sealed record ChefReply(
    string Answer, string? Source, bool Refused, int RemainingQuestions,
    IReadOnlyList<ChefTurn> Context);

// History supplies conversational context; only Evidence supports an answer.
public sealed record ChefModelInput(
    string Question, LessonPassage Evidence, IReadOnlyList<ChefTurn> History);

public interface IChefModel
{
    Task<string> RespondAsync(
        ChefModelInput input, CancellationToken cancellationToken = default);
}

// Local deterministic substitute. No inference, HTTP requests or charges.
// The service must validate the returned text before adding its own citation.
public sealed class FakeChefModel : IChefModel
{
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public Task<string> RespondAsync(
        ChefModelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref calls);
        return Task.FromResult(input.Evidence.Text);
    }
}
