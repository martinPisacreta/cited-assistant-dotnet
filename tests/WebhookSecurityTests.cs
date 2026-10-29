using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AskChefSample;
using Xunit;

namespace AskChefTests;

public sealed class WebhookSecurityTests
{
    private const string Secret = "synthetic-webhook-secret-for-tests-only-27";
    private static readonly DateTimeOffset Now =
        new(2026, 10, 28, 18, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        "{\"eventId\":\"event-a\",\"memberId\":\"member-a\"," +
        "\"active\":true,\"version\":1}");

    private static string Timestamp(int offset = 0) =>
        Now.AddSeconds(offset).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);

    private static string Sign(byte[] body, string timestamp,
        string secret = Secret)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var message = prefix.Concat(body).ToArray();
        return Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), message));
    }

    private static WebhookSignature Verifier() =>
        new(Secret, new FixedClock());

    [Fact]
    public void Valid_signature_accepts_original_bytes()
    {
        var timestamp = Timestamp();
        Assert.True(Verifier().Verify(Body, timestamp, Sign(Body, timestamp)));
    }

    [Fact]
    public void Changing_body_bytes_invalidates_signature()
    {
        var timestamp = Timestamp();
        var altered = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(Body).Replace("true", "false"));
        Assert.False(Verifier().Verify(
            altered, timestamp, Sign(Body, timestamp)));
    }

    [Fact]
    public void Timestamp_is_part_of_the_signed_message()
    {
        Assert.False(Verifier().Verify(
            Body, Timestamp(1), Sign(Body, Timestamp())));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void Correct_signature_outside_time_window_is_rejected(int offset)
    {
        var timestamp = Timestamp(offset);
        Assert.False(Verifier().Verify(Body, timestamp, Sign(Body, timestamp)));
    }

    [Fact]
    public void Signature_with_another_secret_is_rejected()
    {
        var timestamp = Timestamp();
        Assert.False(Verifier().Verify(Body, timestamp,
            Sign(Body, timestamp, "another-synthetic-secret-for-tests-only")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-hex")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Malformed_signature_is_rejected(string? signature)
    {
        Assert.False(Verifier().Verify(Body, Timestamp(), signature));
    }

    [Fact]
    public void Oversized_body_is_rejected_even_with_valid_signature()
    {
        var body = new byte[WebhookSignature.MaximumBodyBytes + 1];
        var timestamp = Timestamp();
        Assert.False(Verifier().Verify(body, timestamp, Sign(body, timestamp)));
    }

    [Fact]
    public void Authentication_returns_only_the_configured_member()
    {
        var token = new string('t', 40);
        var authentication = new DemoMemberAuthentication("member-a", token);
        Assert.True(authentication.TryAuthenticate("Bearer " + token, out var id));
        Assert.Equal("member-a", id);
        Assert.False(authentication.TryAuthenticate(null, out var missing));
        Assert.Equal(string.Empty, missing);
        Assert.False(authentication.TryAuthenticate(
            "Bearer " + new string('x', 40), out var invalid));
        Assert.Equal(string.Empty, invalid);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
