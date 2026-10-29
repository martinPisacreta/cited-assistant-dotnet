using System.Security.Cryptography;
using System.Text;

namespace AskChefSample;

// One explicitly configured demo identity, not a production identity provider.
// Never accept a member identifier from the chat request body.
public sealed class DemoMemberAuthentication
{
    private readonly string memberId;
    private readonly byte[] tokenHash;

    public DemoMemberAuthentication(string memberId, string token)
    {
        if (!SubscriptionStore.ValidIdentifier(memberId))
            throw new ArgumentException("Invalid demo member identifier.");
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length < 32 || token.Length > 256 ||
            token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Use a demo token of 32-256 characters.");
        this.memberId = memberId;
        tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    // Hashes have equal lengths for constant-time comparison.
    // Configure a random test token; never log Authorization headers.
    public bool TryAuthenticate(string? authorization, out string identity)
    {
        identity = string.Empty;
        const string prefix = "Bearer ";
        if (authorization is null ||
            !authorization.StartsWith(prefix, StringComparison.Ordinal) ||
            authorization.Length < prefix.Length + 32 ||
            authorization.Length > prefix.Length + 256) return false;

        var supplied = authorization[prefix.Length..];
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        if (!CryptographicOperations.FixedTimeEquals(tokenHash, suppliedHash))
            return false;

        identity = memberId;
        return true;
    }
}
