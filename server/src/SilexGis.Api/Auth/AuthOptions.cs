// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain;

namespace SilexGis.Api.Auth;

/// <summary>Authentication behavior options.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>When false (default), accounts are created by admins only.</summary>
    public bool OpenRegistration { get; set; }

    /// <summary>Global role granted to self-registered users.</summary>
    public string DefaultRole { get; set; } = GlobalRoles.Viewer;

    /// <summary>Extra OAuth redirect URIs (e.g. the Vite dev server callback).</summary>
    public string[] AdditionalRedirectUris { get; set; } = [];
}

/// <summary>First-run administrator bootstrap (SILEXGIS__Admin__Email / __Password).</summary>
public sealed class AdminBootstrapOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }
}
