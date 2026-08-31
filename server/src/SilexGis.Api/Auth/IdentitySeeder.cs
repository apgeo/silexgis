// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Auth;

/// <summary>
/// Startup seeding: global roles, the bootstrap administrator, and the two first-party OAuth
/// clients — the web application and the SpeleoLoc mobile app. Idempotent — safe on every start.
/// </summary>
public static class IdentitySeeder
{
    public const string SpaClientId = "silexgis-spa";

    /// <summary>
    /// The mobile app's own client id. It is deliberately not the web client's: separate ids are
    /// what make a phone's tokens distinguishable rows in the token store, which is the
    /// precondition for both a lifetime of its own and for revoking one device without the others.
    /// </summary>
    public const string SpeleoLocClientId = "silexgis-speleoloc";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        await SeedRolesAsync(services.GetRequiredService<RoleManager<SilexGisRole>>());
        await SeedAdminAsync(
            services.GetRequiredService<UserManager<SilexGisUser>>(),
            services.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value,
            services.GetRequiredService<SilexGisDbContext>());

        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var authOptions = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        await SeedSpaClientAsync(applications, configuration, authOptions);
        await SeedSpeleoLocClientAsync(applications, authOptions);
    }

    private static async Task SeedRolesAsync(RoleManager<SilexGisRole> roleManager)
    {
        foreach (var role in GlobalRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                _ = await roleManager.CreateAsync(new SilexGisRole(role));
            }
        }
    }

    private static async Task SeedAdminAsync(
        UserManager<SilexGisUser> userManager, AdminBootstrapOptions options, SilexGisDbContext db)
    {
        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        if (await userManager.FindByEmailAsync(options.Email) is not null)
        {
            return;
        }

        var admin = new SilexGisUser
        {
            UserName = options.Email,
            Email = options.Email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
        };

        var result = await userManager.CreateAsync(admin, options.Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Bootstrap admin could not be created: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        _ = await userManager.AddToRoleAsync(admin, GlobalRoles.Admin);

        // The bootstrap account is what keeps the installation administrable: it joins
        // the protected Full Administrators group, whose membership is the grant.
        await PermissionGroupSeeder.EnsureRoleMembershipsAsync(db, admin.Id, GlobalRoles.Admin);

        CaverDirectory.CreateForNewAccount(db, admin.Id, admin.DisplayName, admin.UserName, admin.Email);
        await db.SaveChangesAsync();
    }

    private static async Task SeedSpaClientAsync(
        IOpenIddictApplicationManager applications, IConfiguration configuration, AuthOptions options)
    {
        var publicUrl = configuration.GetValue<string>("PublicUrl")?.TrimEnd('/') ?? "http://localhost:8080";

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = SpaClientId,
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Web,
            DisplayName = "SilexGIS Web",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        descriptor.RedirectUris.Add(new Uri($"{publicUrl}/auth/callback"));
        descriptor.PostLogoutRedirectUris.Add(new Uri(publicUrl));
        foreach (var uri in options.AdditionalRedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        await UpsertAsync(applications, descriptor);
    }

    private static async Task SeedSpeleoLocClientAsync(
        IOpenIddictApplicationManager applications, AuthOptions options)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = SpeleoLocClientId,
            ClientType = ClientTypes.Public,

            // Native, not Web, and the value is load-bearing rather than descriptive: it is what
            // lets a redirect back to a loopback address match whatever ephemeral port the app
            // happened to bind, which is how a desktop or mobile app receives an authorization
            // code from a system browser.
            ApplicationType = ApplicationTypes.Native,
            DisplayName = "SpeleoLoc",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
            },

            // No end-session endpoint: signing out of an installed app is a local act plus the
            // revocation of what it holds, not a browser round trip to the server. A public client
            // holds no secret, so proof of possession of the code is the only thing standing
            // between an intercepted redirect and a token.
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        // Both forms an installed app can be handed a code through are registered now, because
        // registering one later would mean an installation has to be re-seeded before a client
        // build that uses it can sign in at all. The loopback entry carries no port: a native
        // client binds an ephemeral one, and the port is excluded from the comparison.
        descriptor.RedirectUris.Add(new Uri("http://127.0.0.1/callback"));
        descriptor.RedirectUris.Add(new Uri("speleoloc://auth"));

        // The lifetime is set per client rather than by widening the server-wide one. The browser
        // discards its refresh token on unload, so raising its window would only extend the life of
        // stored rows no one can redeem; the phone is the client that keeps a credential across
        // weeks offline, and it is the only one that needs the longer window.
        descriptor.SetRefreshTokenLifetime(options.SpeleoLocRefreshTokenLifetime);

        await UpsertAsync(applications, descriptor);
    }

    /// <summary>
    /// Writes a client registration, reconciling one that already exists rather than only creating
    /// a missing one — otherwise a changed lifetime or a new redirect URI would reach a fresh
    /// installation and never reach one that had already started once. The update replaces the
    /// stored row wholesale from the descriptor, so anything the descriptor omits is cleared.
    /// </summary>
    private static async Task UpsertAsync(
        IOpenIddictApplicationManager applications, OpenIddictApplicationDescriptor descriptor)
    {
        if (await applications.FindByClientIdAsync(descriptor.ClientId!) is { } existing)
        {
            await applications.UpdateAsync(existing, descriptor);
        }
        else
        {
            await applications.CreateAsync(descriptor);
        }
    }
}
