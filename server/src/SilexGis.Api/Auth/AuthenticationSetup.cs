// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        services.AddOptions<TestLoginOptions>().BindConfiguration(TestLoginOptions.SectionName);

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

        // Revalidate the security stamp on every cookie request rather than on the framework's
        // default half-hour timer. Changing or resetting a password rotates the stamp and clears
        // the token stores, but a cookie issued before that keeps working until the next
        // revalidation — and the authorize endpoint accepts exactly that cookie and auto-consents,
        // so a stale cookie could mint a fresh code and trade it for a refresh token that lasts
        // weeks. A thirty-minute residual would therefore become a six-week one, which is the
        // opposite of what changing a password is for. The per-request account lookup this costs
        // is paid only by the cookie scheme, which serves the login and authorize routes alone —
        // every API route authenticates a bearer token and is untouched.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);

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
                    .SetUserInfoEndpointUris("connect/userinfo")
                    // Token revocation, so that signing out can cancel the renewal key the page
                    // held rather than leave it valid for the rest of its window. The protocol's
                    // own endpoint, answered by the sign-in server itself: it needs no access token
                    // (which may already have expired when somebody signs out), it is idempotent
                    // (an unknown or already revoked token is answered as success), and the only
                    // thing it can be made to do is cancel a token the caller already holds — so it
                    // is not a cross-site risk and offers nothing to an anonymous caller.
                    .SetRevocationEndpointUris("connect/revoke");

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                options.RegisterScopes(Scopes.Profile, Scopes.Email, Scopes.Roles);

                // Revoking a refresh token ends the sign-in it belongs to, not only that token.
                options.AddEventHandler(SignInRevocation.Descriptor);

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));

                // The signing and encryption certificates come from TokenCertificateSetup: the
                // operator's files when configured, otherwise certificates made once and kept in the
                // keys directory, so that recreating the container signs nobody out and a second
                // instance validates the first one's tokens.

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
        services.AddSingleton<IConfigureOptions<OpenIddict.Server.OpenIddictServerOptions>, TokenCertificateSetup>();
        services.AddHostedService<TokenStorePruneWorker>();

        // A bearer value is either a sign-in access token or a personal access key, told apart by
        // the key's fixed marker. The policy scheme only routes: a key goes to the key handler,
        // everything else — a sign-in token, garbage, or no header at all — to the token
        // validation exactly as before keys existed. Challenge and forbid always go to the token
        // validation, so every refusal of a bad credential reads the same whichever kind it was.
        services.AddAuthentication()
            .AddPolicyScheme(AccessKeySchemes.Bearer, displayName: null, options =>
            {
                options.ForwardDefaultSelector = AccessKeySchemes.Select;
                options.ForwardChallenge = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.ForwardForbid = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, AccessKeyAuthenticationHandler>(
                AccessKeySchemes.AccessKey, displayName: null, _ => { });

        // AddIdentity made the cookie the default scheme; the API default is bearer.
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = AccessKeySchemes.Bearer;
            options.DefaultAuthenticateScheme = AccessKeySchemes.Bearer;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        // Who the caller is, and where the request came from, for every audit row written while a
        // request is served. Replace rather than add: an added second registration would win only
        // by being registered later, and if that order ever changed every audit row would record
        // no user while every test still passed. Replace removes the persistence layer's
        // no-request defaults whichever call ran first (they are TryAdd for the same reason).
        services.Replace(ServiceDescriptor.Singleton<ICurrentUser, Common.HttpContextCurrentUser>());
        services.Replace(ServiceDescriptor.Singleton<IRequestOrigin, Common.HttpContextRequestOrigin>());
        // Mail and SMS delivery, the message templates, and the settings behind all three.
        services.AddSilexGisMessaging();

        return services;
    }
}
