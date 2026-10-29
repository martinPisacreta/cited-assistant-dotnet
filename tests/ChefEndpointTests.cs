using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AskChefSample;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace AskChefTests;

public sealed class ChefEndpointTests
{
    // Public synthetic credentials, used only by the in-memory test host.
    private const string Member = "member-http";
    private const string Token = "synthetic-http-member-token-0123456789";
    private const string Secret = "synthetic-http-webhook-secret-0123456789";

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var library = new LessonLibrary(
            Path.Combine(AppContext.BaseDirectory, "content"));
        var store = new SubscriptionStore(20);
        app.MapChefEndpoints(
            new ChefService(library, store, new FakeChefModel()),
            store, new WebhookSignature(Secret),
            new DemoMemberAuthentication(Member, Token));
        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static HttpRequestMessage Notice(bool validSignature)
    {
        var body = Encoding.UTF8.GetBytes(
            "{\"eventId\":\"activate-http\",\"memberId\":\"member-http\"," +
            "\"active\":true,\"version\":1}");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message, prefix.Length);
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret), message);
        if (!validSignature) signature[0] ^= 1;
        var request = new HttpRequestMessage(
            HttpMethod.Post, "/subscription-notices")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Demo-Timestamp", timestamp);
        request.Headers.Add("X-Demo-Signature",
            Convert.ToHexString(signature));
        return request;
    }

    private static HttpRequestMessage Chat()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/chat")
        {
            Content = JsonContent.Create(new ChatRequest(
                "http-session", "How long should I roast vegetables?"))
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", Token);
        return request;
    }

    [Fact]
    public async Task Invalid_signature_returns_401_and_does_not_activate()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var notice = Notice(validSignature: false);
        using var response = await client.SendAsync(notice);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var chat = Chat();
        using var denied = await client.SendAsync(chat);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Signed_activation_returns_200_and_enables_chat()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var notice = Notice(validSignature: true);
        using var response = await client.SendAsync(notice);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.True(result.RootElement.TryGetProperty("result", out _));
        using var chat = Chat();
        using var allowed = await client.SendAsync(chat);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var reply = System.Text.Json.JsonDocument.Parse(
            await allowed.Content.ReadAsStringAsync());
        Assert.False(reply.RootElement.GetProperty("refused").GetBoolean());
        Assert.Equal("content/roasted-vegetables.md",
            reply.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Authenticated_chat_without_activation_returns_403()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Chat();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var error = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal("inactive_member",
            error.RootElement.GetProperty("error").GetString());
    }
}
