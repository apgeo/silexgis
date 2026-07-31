// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Validation.AspNetCore;
using SilexGis.Domain;
using SilexGis.Infrastructure;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Auth;

/// <summary>
/// ASP.NET Identity + embedded OpenIddict OIDC server.
/// Cookie session for the interactive authorize flow; JWT bearer (locally validated) for
/// the API. Authorization Code + PKCE only — no client secrets, no password grant.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>
    /// Lifetime of an emailed confirmation or password-reset link. Public because the message
    /// templates state it to the recipient — the number in the mail is this one, not a guess.
    /// </summary>
    public const int EmailLinkLifetimeHours = 24;

    public static IServiceCollection AddSilexGisAuth(this IServiceCollection services, IConfiguration configuration)
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

        // Replace Identity's two delivered-code providers with the single-use ones. The link
        // tokens (confirmation, reset) keep the data-protector provider they already used.
        services.AddScoped<EmailCodeTokenProvider>();
        services.AddScoped<PhoneCodeTokenProvider>();
        services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.ProviderMap[TokenOptions.DefaultEmailProvider] =
                new TokenProviderDescriptor(typeof(EmailCodeTokenProvider));
            options.Tokens.ProviderMap[TokenOptions.DefaultPhoneProvider] =
                new TokenProviderDescriptor(typeof(PhoneCodeTokenProvider));
        });

        // How long a confirmation or reset link stays good. A day is the framework default and
        // what the shipped message wording states; the two must be changed together.
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(EmailLinkLifetimeHours));

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
                // deployment work; tokens are invalidated on key rotation.
                options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();

                // Standard JWT access tokens.
                options.DisableAccessTokenEncryption();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    // TLS terminates at the reverse proxy — the API
                    // itself serves plain HTTP inside the network, so OpenIddict must not
                    // reject non-HTTPS requests. Public exposure without TLS is an
                    // operator error (the install guide requires TLS at the proxy).
                    .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // External login providers (Google/GitHub/generic OIDC) — none unless configured.
        // Each federates into a local account; the local OIDC server still issues app tokens.
        var externalProviders = configuration.GetSection(AuthOptions.SectionName)
            .GetSection(nameof(AuthOptions.ExternalProviders))
            .Get<ExternalProviderOptions[]>() ?? [];
        if (externalProviders.Length > 0)
        {
            services.AddAuthentication().AddExternalProviders(externalProviders);
        }

        services.AddScoped<ExternalAuthService>();

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
        // Mail and SMS delivery, the message templates, and the settings behind all three.
        services.AddSilexGisMessaging();

        return services;
    }
}
