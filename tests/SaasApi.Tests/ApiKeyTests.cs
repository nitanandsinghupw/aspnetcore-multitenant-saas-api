using SaasApi.Security;

namespace SaasApi.Tests;

public class ApiKeyTests
{
    [Fact]
    public void A_generated_key_verifies_against_its_record()
    {
        (string plaintext, ApiKeyRecord record) = ApiKey.Generate("acme", SaasRoles.Admin, "ci");

        Assert.True(ApiKey.TryParse(plaintext, out string keyId, out string secret));
        Assert.Equal(record.KeyId, keyId);
        Assert.True(ApiKey.Verify(secret, record));
    }

    [Fact]
    public void A_wrong_secret_is_rejected()
    {
        (_, ApiKeyRecord record) = ApiKey.Generate("acme", SaasRoles.Admin, "ci");

        Assert.False(ApiKey.Verify("not-the-right-secret-at-all", record));
    }

    [Fact]
    public void Only_a_hash_of_the_secret_is_stored()
    {
        (string plaintext, ApiKeyRecord record) = ApiKey.Generate("acme", SaasRoles.Admin, "ci");
        ApiKey.TryParse(plaintext, out _, out string secret);

        Assert.DoesNotContain(secret, record.SecretSha256);
        Assert.Equal(64, record.SecretSha256.Length);
    }

    [Fact]
    public void Malformed_keys_are_rejected()
    {
        Assert.False(ApiKey.TryParse("not-a-key", out _, out _));
        Assert.False(ApiKey.TryParse("pk_abc_0123456789abcdef", out _, out _));
        Assert.False(ApiKey.TryParse("sk_abc_short", out _, out _));
    }

    [Fact]
    public void Each_generated_key_is_unique()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => ApiKey.Generate("acme", SaasRoles.Reader, "x").Plaintext).ToHashSet();

        Assert.Equal(100, keys.Count);
    }
}
