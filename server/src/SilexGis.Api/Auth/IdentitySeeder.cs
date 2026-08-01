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
/// Startup seeding: global roles, the bootstrap administrator,
/// and the first-party SPA OAuth client. Idempotent — safe on every start.
/// </summary>
public static class IdentitySeeder
{
    public const string SpaClientId = "silexgis-spa";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        await SeedRolesAsync(services.GetRequiredService<RoleManager<SilexGisRole>>());
        await SeedAdminAsync(
            services.GetRequiredService<UserManager<SilexGisUser>>(),
            services.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value,
            services.GetRequiredService<SilexGisDbContext>());
        await SeedSpaClientAsync(
            services.GetRequiredService<IOpenIddictApplicationManager>(),
            configuration,
            services.GetRequiredService<IOptions<AuthOptions>>().Value);
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

        if (await applications.FindByClientIdAsync(SpaClientId) is { } existing)
        {
            await applications.UpdateAsync(existing, descriptor);
        }
        else
        {
            await applications.CreateAsync(descriptor);
        }
    }
}
