using AskChefSample;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 32_768;
});

// Explicit test configuration only. No embedded secrets or live integrations.
var memberId = RequiredEnvironment("ASKCHEF_DEMO_MEMBER_ID");
var memberToken = RequiredEnvironment("ASKCHEF_DEMO_TOKEN");
var webhookSecret = RequiredEnvironment("ASKCHEF_WEBHOOK_SECRET");
var configuredLimit = Environment.GetEnvironmentVariable(
    "ASKCHEF_MONTHLY_LIMIT") ?? "20";
if (!int.TryParse(configuredLimit, out var monthlyLimit) ||
    monthlyLimit is < 1 or > 1000)
    throw new InvalidOperationException(
        "ASKCHEF_MONTHLY_LIMIT must be between 1 and 1000.");

var clock = TimeProvider.System;
var lessons = new LessonLibrary(
    Path.Combine(AppContext.BaseDirectory, "content"));
var subscriptions = new SubscriptionStore(monthlyLimit, clock);
using var modelHttp = new HttpClient(new HttpClientHandler
{
    AllowAutoRedirect = false
});
var provider = builder.Configuration["ASKCHEF_MODEL_PROVIDER"]
    ?? "deterministic";
IChefModel model = provider switch
{
    "deterministic" => new FakeChefModel(),
    "openai-compatible" => new OpenAiCompatibleChefModel(modelHttp,
        new Uri(builder.Configuration["ASKCHEF_OPENAI_BASE_URL"]
            ?? throw new InvalidOperationException("Missing model base URL.")),
        builder.Configuration["ASKCHEF_OPENAI_MODEL"]
            ?? throw new InvalidOperationException("Missing model name.")),
    _ => throw new InvalidOperationException("Unknown model provider.")
};
var chef = new ChefService(lessons, subscriptions, model, clock);
var signatures = new WebhookSignature(webhookSecret, clock);
var authentication = new DemoMemberAuthentication(memberId, memberToken);

var app = builder.Build();
app.MapChefEndpoints(chef, subscriptions, signatures, authentication);

// No member starts active. A valid signed notice must activate access.
// All subscription, usage and conversation state lives in this process.
app.Run();

static string RequiredEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException("Missing test configuration: " + name);
    return value;
}
