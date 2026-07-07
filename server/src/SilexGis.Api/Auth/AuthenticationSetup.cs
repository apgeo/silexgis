// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Validation.AspNetCore;
using SilexGis.Domain;
using SilexGis.Infrastructure.Email;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Auth;

/// <summary>
/// ASP.NET Identity + embedded OpenIddict OIDC server (ADR-006, 05-auth-permissions.md §1).
/// Cookie session for the interactive authorize flow; JWT bearer (locally validated) for
/// the API. Authorization Code + PKCE only — no client secrets, no password grant.
/// </summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddSilexGisAuth(this IServiceCollection services)
    {
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName);
        services.AddOptions<AdminBootstrapOptions>().BindConfiguration(AdminBootstrapOptions.SectionName);

        services.AddIdentity<SilexGisUser, SilexGisRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // Length-first policy (NIST-style); complexity classes add little entropy.
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireDigit = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                // One subject claim everywhere (cookies and tokens).
                options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
            })
            .AddEntityFrameworkStores<SilexGisDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "silexgis.session";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.HttpOnly = true;
            // API semantics: unauthenticated → 401/403, never HTML redirects. The authorize
            // endpoint does its own redirect-to-SPA-login (ConnectEndpoints).
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<SilexGisDbContext>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetUserInfoEndpointUris("connect/userinfo");

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                options.RegisterScopes(Scopes.Profile, Scopes.Email, Scopes.Roles);

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));

                // Development certificates are auto-created and persisted per machine.
                // Production installations must mount stable certificates — tracked for the
                // deployment work (06-deployment.md); tokens are invalidated on key rotation.
                options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();

                // Standard JWT access tokens (05-auth-permissions.md §1).
                options.DisableAccessTokenEncryption();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    // TLS terminates at the reverse proxy (01-architecture.md §1) — the API
                    // itself serves plain HTTP inside the network, so OpenIddict must not
                    // reject non-HTTPS requests. Public exposure without TLS is an operator
                    // error documented in 06-deployment.md.
                    .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // AddIdentity made the cookie the default scheme; the API default is bearer.
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        // Replaces the AnonymousCurrentUser registered by AddSilexGisPersistence (last wins).
        services.AddSingleton<ICurrentUser, Common.HttpContextCurrentUser>();
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
