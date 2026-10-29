using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AskChefSample;

// HMAC-SHA256(secret, UTF8(timestamp + ".") || originalBodyBytes).
public sealed class WebhookSignature
{
    public const int MaximumBodyBytes = 16_384;
    private readonly byte[] key;
    private readonly TimeProvider clock;

    public WebhookSignature(string secret, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        key = Encoding.UTF8.GetBytes(secret);
        if (key.Length < 32)
            throw new ArgumentException("Use a test secret of at least 32 UTF-8 bytes.");
        this.clock = clock ?? TimeProvider.System;
    }

    // Timestamp is Unix seconds; signature is exactly 64 hexadecimal digits.
    // Verify before parsing JSON or changing subscription state.
    public bool Verify(
        byte[] body, string? timestamp, string? signature)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length > MaximumBodyBytes ||
            timestamp is not { Length: >= 1 and <= 20 } ||
            signature is not { Length: 64 }) return false;
        if (!long.TryParse(timestamp, NumberStyles.None,
            CultureInfo.InvariantCulture, out var seconds)) return false;

        DateTimeOffset sentAt;
        try { sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return false; }

        var age = clock.GetUtcNow() - sentAt;
        if (age < TimeSpan.FromMinutes(-5) ||
            age > TimeSpan.FromMinutes(5)) return false;

        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }

        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message, prefix.Length);
        var expected = HMACSHA256.HashData(key, message);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
