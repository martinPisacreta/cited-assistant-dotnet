using AskChefSample;
using Xunit;

namespace AskChefTests;

public sealed class ChefServiceTests
{
    private static (ChefService Chef, SubscriptionStore Store) Create(
        int limit = 20, IChefModel? model = null)
    {
        var library = new LessonLibrary(
            Path.Combine(AppContext.BaseDirectory, "content"));
        var store = new SubscriptionStore(limit);
        return (new ChefService(library, store, model ?? new FakeChefModel()), store);
    }

    private static void Activate(SubscriptionStore store, string member = "member-a") =>
        store.ApplyVerifiedNotice(new SubscriptionNotice(
            "activate-" + member, member, true, 1));

    private static ChatRequest Ask(string question, string conversation = "session-a") =>
        new(conversation, question);

    [Theory]
    [InlineData("How do I roast vegetables?", "roasted-vegetables")]
    [InlineData("How do I use pasta water?", "pasta-water")]
    [InlineData("What is the oil to vinegar ratio?", "vinaigrette")]
    public async Task Supported_question_returns_exact_local_passage_and_citation(
        string question, string lesson)
    {
        var (chef, store) = Create();
        Activate(store);
        var reply = await chef.ReplyAsync("member-a", Ask(question));
        var source = "content/" + lesson + ".md";
        var passage = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "content", lesson + ".md")).Trim();
        Assert.False(reply.Refused);
        Assert.Equal(source, reply.Source);
        Assert.Equal(passage + "\n\nSource: " + source, reply.Answer);
    }

    [Fact]
    public async Task Outside_material_refuses_without_calling_model()
    {
        var model = new FakeChefModel();
        var (chef, store) = Create(model: model);
        Activate(store);
        var reply = await chef.ReplyAsync("member-a", Ask("Who won the election?"));
        Assert.True(reply.Refused);
        Assert.Null(reply.Source);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Unknown_member_cannot_call_model()
    {
        var model = new FakeChefModel();
        var (chef, _) = Create(model: model);
        var error = await Assert.ThrowsAsync<ChefAccessException>(() =>
            chef.ReplyAsync("member-a", Ask("How do I roast vegetables?")));
        Assert.Equal("inactive_member", error.Reason);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Cancellation_of_subscription_blocks_the_next_question()
    {
        var (chef, store) = Create();
        Activate(store);
        await chef.ReplyAsync("member-a", Ask("How do I roast vegetables?"));
        store.ApplyVerifiedNotice(new("cancel-a", "member-a", false, 2));
        var error = await Assert.ThrowsAsync<ChefAccessException>(() =>
            chef.ReplyAsync("member-a", Ask("Can you repeat that lesson?")));
        Assert.Equal("inactive_member", error.Reason);
    }

    [Fact]
    public async Task Monthly_cap_blocks_model_before_an_additional_call()
    {
        var model = new FakeChefModel();
        var (chef, store) = Create(1, model);
        Activate(store);
        var first = await chef.ReplyAsync("member-a", Ask("How do I roast vegetables?"));
        Assert.Equal(0, first.RemainingQuestions);
        var error = await Assert.ThrowsAsync<ChefAccessException>(() =>
            chef.ReplyAsync("member-a", Ask("How do I use pasta water?")));
        Assert.Equal("monthly_limit", error.Reason);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task Follow_up_uses_previous_lesson_and_supplies_history_to_model()
    {
        var model = new RecordingModel();
        var (chef, store) = Create(model: model);
        Activate(store);
        var first = await chef.ReplyAsync("member-a", Ask("How do I use pasta water?"));
        var followUp = await chef.ReplyAsync("member-a", Ask("Can you repeat that lesson?"));
        Assert.False(followUp.Refused);
        Assert.Equal(first.Source, followUp.Source);
        Assert.Equal(first.Answer, followUp.Answer);
        Assert.Equal(first.Context[0], Assert.Single(model.LastInput!.History));
        Assert.Equal(2, followUp.Context.Count);
    }

    [Theory]
    [InlineData("member-b", "session-a")]
    [InlineData("member-a", "session-b")]
    public async Task Context_is_separate_by_member_and_conversation(
        string nextMember, string nextConversation)
    {
        var (chef, store) = Create();
        Activate(store);
        Activate(store, "member-b");
        await chef.ReplyAsync("member-a", Ask("How do I roast vegetables?"));
        var reply = await chef.ReplyAsync(nextMember,
            Ask("Can you repeat that lesson?", nextConversation));
        Assert.True(reply.Refused);
        Assert.Null(reply.Source);
        Assert.Single(reply.Context);
    }

    [Fact]
    public async Task Invented_model_output_is_rejected_without_a_citation()
    {
        var (chef, store) = Create(model: new RecordingModel { Invent = true });
        Activate(store);
        var reply = await chef.ReplyAsync("member-a", Ask("How do I roast vegetables?"));
        Assert.True(reply.Refused);
        Assert.Null(reply.Source);
        Assert.DoesNotContain("invented-secret-answer", reply.Answer);
    }

    private sealed class RecordingModel : IChefModel
    {
        public ChefModelInput? LastInput { get; private set; }
        public bool Invent { get; init; }
        public Task<string> RespondAsync(ChefModelInput input,
            CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(Invent ? "invented-secret-answer" : input.Evidence.Text);
        }
    }
}
