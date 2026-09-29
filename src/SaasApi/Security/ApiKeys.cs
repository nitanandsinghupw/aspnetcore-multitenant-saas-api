using System.Security.Cryptography;
using System.Text;

namespace SaasApi.Security;

/// <summary>
/// A stored API key. Only a SHA-256 hash of the secret is kept, so a leaked database doesn't leak
/// working keys. Keys look like "sk_{keyId}_{secret}": the key id finds the record, the secret proves it.
/// </summary>
public sealed record ApiKeyRecord(string KeyId, string SecretSha256, string TenantId, string Role, string Name, bool Revoked = false);

public interface IApiKeyStore
{
    ApiKeyRecord? FindByKeyId(string keyId);
}

public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly Dictionary<string, ApiKeyRecord> _keys;

    public InMemoryApiKeyStore(IEnumerable<ApiKeyRecord> keys) =>
        _keys = keys.ToDictionary(k => k.KeyId, StringComparer.Ordinal);

    public ApiKeyRecord? FindByKeyId(string keyId) => _keys.TryGetValue(keyId, out ApiKeyRecord? k) ? k : null;
}

public static class ApiKey
{
    public const string Prefix = "sk";

    /// <summary>Creates a new random key. Show the plaintext to the user once; store only the record.</summary>
    public static (string Plaintext, ApiKeyRecord Record) Generate(string tenantId, string role, string name)
    {
        string keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        string secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        return ($"{Prefix}_{keyId}_{secret}", new ApiKeyRecord(keyId, Hash(secret), tenantId, role, name));
    }

    public static bool TryParse(string presented, out string keyId, out string secret)
    {
        keyId = secret = string.Empty;
        string[] parts = presented.Split('_', 3);
        if (parts.Length != 3 || parts[0] != Prefix || parts[1].Length == 0 || parts[2].Length < 16) return false;
        keyId = parts[1];
        secret = parts[2];
        return true;
    }

    /// <summary>Constant-time comparison, so response timing doesn't reveal how much of a guess was right.</summary>
    public static bool Verify(string secret, ApiKeyRecord record)
    {
        byte[] expected = Convert.FromHexString(record.SecretSha256);
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
