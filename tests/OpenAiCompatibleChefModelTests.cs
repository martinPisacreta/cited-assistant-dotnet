using System.Net;
using System.Text;
using System.Text.Json;
using AskChefSample;
using Xunit;

namespace AskChefSample.Tests;

public sealed class OpenAiCompatibleChefModelTests : IDisposable
{
    private readonly string keyVariable =
        "ASKCHEF_TEST_" + Guid.NewGuid().ToString("N");
    private static readonly ChefModelInput Input = new(
        "How do I use pasta water?",
        new LessonPassage("pasta-water", "Pasta water",
            "content/pasta-water.md", "Add pasta water to loosen the sauce."),
        new[] { new ChefTurn("PRIVATE HISTORY", "DO NOT SEND", null,
            DateTimeOffset.UnixEpoch) });

    public OpenAiCompatibleChefModelTests()
    {
        // Disposable placeholder only; no real provider key is used.
        Environment.SetEnvironmentVariable(keyVariable, "synthetic-test-only");
    }

    [Fact]
    public async Task ValidCitationReturnsGeneratedAnswerAndOnlySelectedEvidence()
    {
        const string answer = "Loosen the sauce with pasta water. [source:pasta-water]";
        using var handler = new FakeHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://chef.invalid/v1/chat/completions",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var body = await request.Content!.ReadAsStringAsync(token);
            using var payload = JsonDocument.Parse(body);
            Assert.Equal("test-model", payload.RootElement
                .GetProperty("model").GetString());
            var materialText = payload.RootElement.GetProperty("messages")[1]
                .GetProperty("content").GetString()!;
            using var material = JsonDocument.Parse(materialText);
            var passage = Assert.Single(material.RootElement
                .GetProperty("passages").EnumerateArray());
            Assert.Equal(Input.Evidence.Id, passage.GetProperty("id").GetString());
            Assert.Equal(Input.Evidence.Text, passage.GetProperty("text").GetString());
            Assert.DoesNotContain("PRIVATE HISTORY", body);
            Assert.DoesNotContain("DO NOT SEND", body);
            return Completion(answer);
        });
        using var client = new HttpClient(handler);
        Assert.Equal(answer, await Model(client).RespondAsync(Input));
    }

    [Fact]
    public async Task MissingCitationReturnsEmptyAnswer()
    {
        Assert.Equal(string.Empty, await Respond(
            (_, _) => Task.FromResult(Completion("Use pasta water."))));
    }

    [Fact]
    public async Task InventedCitationReturnsEmptyAnswer()
    {
        Assert.Equal(string.Empty, await Respond((_, _) => Task.FromResult(
            Completion("Use water. [source:pasta-water] [source:invented]"))));
    }

    [Fact]
    public async Task ServerErrorReturnsEmptyAnswer()
    {
        Assert.Equal(string.Empty, await Respond((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError))));
    }

    [Fact]
    public async Task EndpointTimeoutReturnsEmptyAnswer()
    {
        Assert.Equal(string.Empty, await Respond(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Completion("Unreachable [source:pasta-water]");
        }, TimeSpan.FromMilliseconds(50)));
    }

    private OpenAiCompatibleChefModel Model(HttpClient client,
        TimeSpan? timeout = null) => new(client,
            new Uri("https://chef.invalid/v1/"), "test-model",
            keyVariable, timeout);

    private async Task<string> Respond(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        TimeSpan? timeout = null)
    {
        using var handler = new FakeHandler(send);
        using var client = new HttpClient(handler);
        return await Model(client, timeout).RespondAsync(Input);
    }

    private static HttpResponseMessage Completion(string answer) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = answer } } }
        }), Encoding.UTF8, "application/json")
    };

    public void Dispose() => Environment.SetEnvironmentVariable(keyVariable, null);

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
