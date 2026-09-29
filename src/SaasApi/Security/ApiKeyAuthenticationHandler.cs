using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SaasApi.Security;

/// <summary>
/// Authenticates machine-to-machine callers by API key (header "X-Api-Key" or "Authorization: ApiKey ...").
/// The key decides the tenant; nothing the client sends in the URL or body can change it.
/// For interactive users, add Microsoft Entra ID (JWT bearer) as a second scheme that maps the
/// token's tenant claim to <see cref="SaasClaims.TenantId"/> (see README).
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";

    private readonly IApiKeyStore _keys;

    public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IApiKeyStore keys) : base(options, logger, encoder) => _keys = keys;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? presented = ReadKey();
        if (presented is null) return Task.FromResult(AuthenticateResult.NoResult());

        if (!ApiKey.TryParse(presented, out string keyId, out string secret))
            return Task.FromResult(AuthenticateResult.Fail("Malformed API key."));

        ApiKeyRecord? record = _keys.FindByKeyId(keyId);
        if (record is null || record.Revoked || !ApiKey.Verify(secret, record))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));   // same message for every case

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, record.Name),
            new Claim(ClaimTypes.Role, record.Role),
            new Claim(SaasClaims.TenantId, record.TenantId),
            new Claim(SaasClaims.KeyId, record.KeyId)
        }, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{SchemeName} header=\"{HeaderName}\"";
        return base.HandleChallengeAsync(properties);
    }

    private string? ReadKey()
    {
        if (Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header))
            return header.ToString().Trim();

        string? authorization = Request.Headers.Authorization;
        const string prefix = SchemeName + " ";
        return authorization is not null && authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
    }
}
