// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using Microsoft.AspNetCore.DataProtection;
using SilexGis.Api.Features.About;
using SilexGis.Api.Features.Admin;
using SilexGis.Api.Features.Attachments;
using SilexGis.Api.Features.Audit;
using SilexGis.Api.Features.Caves;
using SilexGis.Api.Features.Dashboard;
using SilexGis.Api.Features.Export;
using SilexGis.Api.Features.Files;
using SilexGis.Api.Features.Geofiles;
using SilexGis.Api.Features.GeoreferencedMaps;
using SilexGis.Api.Features.History;
using SilexGis.Api.Features.Jobs;
using SilexGis.Api.Features.Map;
using SilexGis.Api.Features.MapViews;
using SilexGis.Api.Features.FeatureSets;
using SilexGis.Api.Features.Permissions;
using SilexGis.Api.Features.MapLayers;
using SilexGis.Api.Features.Me;
using SilexGis.Api.Features.Notifications;
using SilexGis.Api.Features.Features;
using SilexGis.Api.Features.FeatureShares;
using SilexGis.Api.Features.Search;
using SilexGis.Api.Features.Tags;
using SilexGis.Api.Features.Taxonomies;
using SilexGis.Api.Features.Cavers;
using SilexGis.Api.Features.CavingGroups;
using SilexGis.Api.Features.TripLogs;
using SilexGis.Api.Features.Users;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // SILEXGIS__{Section}__{Key} environment variables override appsettings.
    builder.Configuration.AddEnvironmentVariables("SILEXGIS__");

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Contract hygiene: enums as strings; strict numbers — the web
    // default (AllowReadingFromString) would advertise every numeric as "number | string"
    // in the OpenAPI document and poison the generated TS client.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();
    builder.Services.AddSilexGisPersistence(builder.Configuration);
    builder.Services.AddSilexGisGeodata(builder.Configuration);
    builder.Services.AddSilexGisAuth(builder.Configuration);
    builder.Services.AddSilexGisNotifications();

    // Data-protection keys persist to disk so file-access tokens (and cookies) survive
    // restarts and container recreation; deployments mount a volume at Keys:Path.
    var keysPath = Path.GetFullPath(
        builder.Configuration.GetValue<string>("Keys:Path") ?? Path.Combine("data", "keys"),
        AppContext.BaseDirectory);
    Directory.CreateDirectory(keysPath);
    builder.Services.AddDataProtection()
        .SetApplicationName("silexgis")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    builder.Services.AddSingleton<IFileAccessTokenService, FileAccessTokenService>();
    builder.Services.AddSingleton<IUnsubscribeTokens, UnsubscribeTokenService>();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<SilexGisDbContext>("database");
    builder.Services.AddOptions<AboutOptions>()
        .BindConfiguration(AboutOptions.SectionName);
    builder.Services.AddOptions<AccessOptions>()
        .BindConfiguration(AccessOptions.SectionName);
    builder.Services.AddOptions<MapOptions>()
        .BindConfiguration(MapOptions.SectionName);
    builder.Services.AddScoped<IUserContextAccessor, UserContextAccessor>();
    builder.Services.AddScoped<IAccessContextAccessor, AccessContextAccessor>();
    // Credential-guessing protection: per-IP fixed window on the auth surface.
    // Limit is configurable for installations behind shared NATs.
    var authPermitLimit = builder.Configuration.GetValue("Auth:RateLimitPerMinute", 60);
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("auth", context =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = authPermitLimit,
                    QueueLimit = 0,
                }));
    });

    // AccessService / IAccessService / FeatureProtection / FullAdminGuard are registered
    // by AddSilexGisPersistence — they live in Infrastructure since the supertype cutover.
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    var app = builder.Build();

    // The API always sits behind a reverse proxy (nginx `web` service / Vite dev proxy);
    // honor its scheme/host so OIDC issuer and redirects are right.
    // The proxy is only reachable on the internal network, so no known-proxy allow-list.
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapOpenApi(); // /openapi/v1.json — the contract the TS client is generated from.

    // Liveness: process is up (no dependency checks). Readiness: all registered checks.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready");

    app.MapConnectEndpoints();

    // Default-deny: everything under /api/v1 requires a bearer token unless an endpoint is
    // explicitly on the anonymous allow-list.
    var api = app.MapGroup("/api/v1").RequireAuthorization();
    api.MapAboutEndpoints();
    api.MapAuthEndpoints();
    api.MapTwoFactorChallengeEndpoints();
    api.MapExternalAuthEndpoints();
    api.MapMeEndpoints();
    api.MapMeAddressEndpoints();
    api.MapMeEmailEndpoints();
    api.MapMePhoneEndpoints();
    api.MapMeCredentialEndpoints();
    api.MapMeNotificationEndpoints();
    api.MapMePreferenceEndpoints();
    api.MapMeDataExportEndpoints();
    api.MapMeCapabilityEndpoints();
    api.MapUnsubscribeEndpoints();
    api.MapMfaEndpoints();
    api.MapTaxonomyEndpoints();
    api.MapMapLayerEndpoints();
    api.MapCaveEndpoints();
    api.MapEntranceEndpoints();
    api.MapSurveyModelEndpoints();
    api.MapCenterlineEndpoints();
    api.MapFeatureEndpoints();
    api.MapFeatureHierarchyEndpoints();
    api.MapFeatureLinkEndpoints();
    api.MapFeatureShareEndpoints();
    api.MapMapDataEndpoints();
    api.MapSearchEndpoints();
    api.MapDashboardEndpoints();
    api.MapGeofileEndpoints();
    api.MapJobEndpoints();
    api.MapExportEndpoints();
    api.MapFileEndpoints();
    api.MapAttachmentEndpoints();
    api.MapGeoreferencedMapEndpoints();
    api.MapTripLogEndpoints();
    api.MapTagEndpoints();
    api.MapAuditEndpoints();
    api.MapHistoryEndpoints();
    api.MapObjectAccessEndpoints();
    api.MapPermissionGroupEndpoints();
    api.MapFeatureSetEndpoints();
    api.MapCavingGroupEndpoints();
    api.MapCaverEndpoints();
    api.MapUserEndpoints();
    api.MapMapViewEndpoints();
    api.MapAdminSettingsEndpoints();
    api.MapAdminTemplateEndpoints();

    if (app.Configuration.GetValue("Db:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.MigrateAsync();
        await TaxonomySeeder.SeedAsync(db);
        await MapLayerSeeder.SeedAsync(db);
        // Permission groups must exist before the bootstrap admin joins Full Administrators.
        await PermissionGroupSeeder.SeedAsync(db);
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
    }

    // `dotnet run -- seed-demo`: load the demo dataset and exit.
    if (args.Contains("seed-demo"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var admins = await userManager.GetUsersInRoleAsync(GlobalRoles.Admin);
        if (admins.Count == 0)
        {
            Log.Error("seed-demo requires a bootstrap admin (set SILEXGIS__Admin__Email/Password)");
            return;
        }

        await DemoSeeder.SeedAsync(db, admins[0].Id);
        Log.Information("Demo data seeded (owner: {Email})", admins[0].Email);
        return;
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SilexGIS API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposes the implicit entry-point class to WebApplicationFactory-based tests.</summary>
public partial class Program;
