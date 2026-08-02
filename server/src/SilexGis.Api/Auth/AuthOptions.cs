// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Auth;

/// <summary>Authentication behavior options.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>When false (default), accounts are created by admins only.</summary>
    public bool OpenRegistration { get; set; }

    /// <summary>
    /// Comma-separated permission-group slugs a new account is auto-joined to — at
    /// self-registration and at auto-provisioning on a first external sign-in. Empty is
    /// a complete configuration: every account is an implicit member of All Users and
    /// holds the built-in rules, so nothing has to be configured for signups to work.
    /// </summary>
    public string DefaultPermissionGroups { get; set; } = string.Empty;

    /// <summary>The configured slugs, parsed: trimmed, lower-cased, empties dropped.</summary>
    public IReadOnlyList<string> DefaultPermissionGroupSlugs =>
        [.. DefaultPermissionGroups
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())];

    /// <summary>
    /// Hide the password login form: sign-in is only via configured external providers.
    /// Has no effect (and the form still shows) when no provider is configured, so an
    /// installation cannot lock itself out by flipping this alone.
    /// </summary>
    public bool ExternalOnly { get; set; }

    /// <summary>Extra OAuth redirect URIs (e.g. the Vite dev server callback).</summary>
    public string[] AdditionalRedirectUris { get; set; } = [];

    /// <summary>
    /// External identity providers (Google / GitHub / generic OIDC). Empty by default —
    /// the login page shows provider buttons only for entries configured here.
    /// </summary>
    public ExternalProviderOptions[] ExternalProviders { get; set; } = [];
}

/// <summary>
/// One external login provider. Federates into a local account: an external identity is
/// linked to a <c>SilexGisUser</c>; app tokens are always issued by the local OIDC server.
/// </summary>
public sealed class ExternalProviderOptions
{
    /// <summary>
    /// Stable scheme key used in URLs and stored on the login (<c>/auth/external/{name}</c>,
    /// callback <c>/api/v1/signin-{name}</c>). Lowercase, no spaces; unique per installation.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider kind: <c>google</c>, <c>github</c>, or <c>oidc</c> (generic OpenID Connect).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Button label shown on the login page.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// OIDC issuer (required for <c>oidc</c>; optional override for <c>google</c>, which
    /// defaults to <c>https://accounts.google.com</c>). Ignored for <c>github</c>.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Extra scopes beyond the defaults (openid/profile/email for OIDC).</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Allow requiring HTTPS on the OIDC metadata endpoint to be relaxed — only for
    /// self-hosted OIDC providers reachable over plain HTTP on a trusted network.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Create a local account on first sign-in when no linked or same-email account exists
    /// (requires a provider-verified email). Default false → the account must pre-exist
    /// (admin-provisioned) or the user links the provider from account settings.
    /// </summary>
    public bool AllowCreate { get; set; }
}

/// <summary>First-run administrator bootstrap (SILEXGIS__Admin__Email / __Password).</summary>
public sealed class AdminBootstrapOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }
}
