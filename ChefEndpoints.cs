using System.Text.Json;

namespace AskChefSample;

public static class ChefEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static void MapChefEndpoints(
        this WebApplication app, ChefService chef,
        SubscriptionStore subscriptions, WebhookSignature signatures,
        DemoMemberAuthentication authentication)
    {
        app.MapPost("/chat", (HttpContext context, ChatRequest request) =>
            ChatAsync(context, request));
        // Cast so Minimal APIs writes the IResult instead of treating the
        // lambda as a RequestDelegate and discarding it (ASP0016).
        app.MapPost("/subscription-notices",
            (Delegate)(Func<HttpContext, Task<IResult>>)NoticeAsync);

        async Task<IResult> ChatAsync(HttpContext context, ChatRequest request)
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            if (!authentication.TryAuthenticate(
                context.Request.Headers["Authorization"].ToString(),
                out var memberId))
                return Results.Unauthorized();

            try
            {
                var reply = await chef.ReplyAsync(
                    memberId, request, context.RequestAborted);
                return Results.Ok(reply);
            }
            catch (ChefAccessException error)
            {
                return Results.Json(new { error = error.Reason },
                    statusCode: error.Reason == "monthly_limit" ? 429 : 403);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid_chat_request" });
            }
        }

        async Task<IResult> NoticeAsync(HttpContext context)
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            var request = context.Request;
            if (request.ContentLength > WebhookSignature.MaximumBodyBytes)
                return Results.StatusCode(413);

            // Bound actual bytes too: Content-Length can be absent.
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var count = await request.Body.ReadAsync(
                    buffer.AsMemory(), context.RequestAborted);
                if (count == 0) break;
                if (body.Length + count > WebhookSignature.MaximumBodyBytes)
                    return Results.StatusCode(413);
                body.Write(buffer, 0, count);
            }

            var bytes = body.ToArray();
            if (!signatures.Verify(bytes,
                request.Headers["X-Demo-Timestamp"].ToString(),
                request.Headers["X-Demo-Signature"].ToString()))
                return Results.Unauthorized();

            // Deserialize only after authenticating the original bytes.
            // Require every field, including explicit Active=false on a cancel.
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("eventId", out var eventId) ||
                    eventId.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("memberId", out var memberId) ||
                    memberId.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("active", out var active) ||
                    (active.ValueKind != JsonValueKind.True &&
                     active.ValueKind != JsonValueKind.False) ||
                    !root.TryGetProperty("version", out var version) ||
                    !version.TryGetInt64(out _))
                    return Results.BadRequest(new { error = "invalid_notice" });

                var notice = JsonSerializer.Deserialize<SubscriptionNotice>(
                    bytes, JsonOptions);
                if (notice is null)
                    return Results.BadRequest(new { error = "invalid_notice" });
                var result = subscriptions.ApplyVerifiedNotice(notice);
                return Results.Ok(new { result });
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_notice" });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid_notice" });
            }
        }
    }
}
