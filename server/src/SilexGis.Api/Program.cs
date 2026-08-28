// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using Microsoft.AspNetCore.DataProtection;
using SilexGis.Api.Features.About;
using SilexGis.Api.Features.Admin;
using SilexGis.Api.Features.Attachments;
using SilexGis.Api.Features.AccessHistory;
using SilexGis.Api.Features.Audit;
using SilexGis.Api.Features.Cabinets;
using SilexGis.Api.Features.Crs;
using SilexGis.Api.Features.Caves;
using SilexGis.Api.Features.Dashboard;
using SilexGis.Api.Features.Documents;
using SilexGis.Api.Features.Export;
using SilexGis.Api.Features.Files;
using SilexGis.Api.Features.Geofiles;
using SilexGis.Api.Features.GeoreferencedMaps;
using SilexGis.Api.Features.History;
using SilexGis.Api.Features.Import;
using SilexGis.Api.Features.Jobs;
using SilexGis.Api.Features.Map;
using SilexGis.Api.Features.MapViews;
using SilexGis.Api.Features.FeatureSets;
using SilexGis.Api.Features.Permissions;
using SilexGis.Api.Features.Photos;
using SilexGis.Api.Features.MapLayers;
using SilexGis.Api.Features.Me;
using SilexGis.Api.Features.Notifications;
using SilexGis.Api.Features.Features;
using SilexGis.Api.Features.Filters;
using SilexGis.Api.Features.ResLinks;
using SilexGis.Api.Features.FeatureShares;
using SilexGis.Api.Features.Search;
using SilexGis.Api.Features.Statistics;
using SilexGis.Api.Features.Sync;
using SilexGis.Api.Features.Tags;
using SilexGis.Api.Features.Taxonomies;
using SilexGis.Api.Features.Terrain;
using SilexGis.Api.Features.Cavers;
using SilexGis.Api.Features.CavingGroups;
using SilexGis.Api.Features.TripLogs;
using SilexGis.Api.Features.Uploads;
using SilexGis.Api.Features.Users;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure;
using SilexGis.Infrastructure.Documents;
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

    // A large upload passes three independent ceilings before a handler sees it: the request
    // body limit, the multipart form limit, and the reverse proxy's own cap. The first two
    // are set here from the same configured maximum the upload endpoints enforce, because a
    // body cut off by either never reaches the code that could explain why. The proxy's cap
    // lives in the deployment configuration and has to be at least as large. The request
    // body limit stays per-endpoint — raising it globally would let every JSON route accept
    // half a gigabyte — while the multipart limit is safe to set once, since a body has
    // already been bounded by the time the form reader runs.
    var filesOptions = builder.Configuration.GetSection(SilexGis.Infrastructure.Files.FilesOptions.SectionName)
        .Get<SilexGis.Infrastructure.Files.FilesOptions>() ?? new SilexGis.Infrastructure.Files.FilesOptions();
    // The multipart ceiling is process-wide, so it has to clear the largest upload any route
    // accepts — not only the configurable one. Georeferenced rasters accept a fixed 512 MB,
    // and lowering the configurable limit must not silently cut those off at a size no
    // message anywhere names. Whichever is larger wins; raise this if a route ever accepts
    // more than 512 MB on its own.
    const long largestFixedUploadBytes = 512L * 1024 * 1024;
    var multipartBodyLengthLimit = Math.Max(
        filesOptions.MaxRequestBodyBytes,
        largestFixedUploadBytes + SilexGis.Infrastructure.Files.FilesOptions.MultipartEnvelopeBytes);
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(
        options => options.MultipartBodyLengthLimit = multipartBodyLengthLimit);

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer<SilexGis.Api.Common.BearerSecurityDocumentTransformer>();
        options.AddOperationTransformer<SilexGis.Api.Common.AnonymousRouteSecurityOperationTransformer>();
    });
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
    builder.Services.AddOptions<TerrainOptions>()
        .BindConfiguration(TerrainOptions.SectionName);
    builder.Services.AddOptions<SyncOptions>()
        .BindConfiguration(SyncOptions.SectionName);
    builder.Services.AddScoped<IUserContextAccessor, UserContextAccessor>();
    builder.Services.AddScoped<IAccessContextAccessor, AccessContextAccessor>();
    // One resolver per resource-link target world; the directory is what the link
    // surface fans out through for display, the picker feed and the authoring floor.
    builder.Services.AddScoped<IResLinkTargetResolver, FeatureTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, DocumentTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, TripLogTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, CaverTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, CavingGroupTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, MapViewTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, CabinetTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, SurveyModelTargetResolver>();
    builder.Services.AddScoped<IResLinkTargetResolver, GeofileTargetResolver>();
    builder.Services.AddScoped<ResLinkTargetDirectory>();
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
    api.MapUiDefaultsEndpoints();
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
    api.MapCrsEndpoints();
    api.MapFeatureEndpoints();
    api.MapFilterEndpoints();
    api.MapFeatureHierarchyEndpoints();
    api.MapFeatureLinkEndpoints();
    api.MapFeatureShareEndpoints();
    api.MapMapDataEndpoints();
    api.MapSearchEndpoints();
    api.MapDashboardEndpoints();
    api.MapGeofileEndpoints();
    api.MapTermRuleEndpoints();
    api.MapStagedImportEndpoints();
    api.MapPhotoImportEndpoints();
    api.MapImportBatchEndpoints();
    api.MapJobEndpoints();
    api.MapExportEndpoints();
    api.MapFileEndpoints();
    api.MapUploadBatchEndpoints();
    api.MapDocumentEndpoints();
    api.MapDocumentTypeEndpoints();
    api.MapDocumentCommentEndpoints();
    api.MapCabinetEndpoints();
    api.MapAttachmentEndpoints();
    api.MapPhotoEndpoints();
    api.MapAlbumEndpoints();
    api.MapPublicPhotoEndpoints();
    api.MapResLinkEndpoints();
    api.MapResLinkRelationTypeEndpoints();
    api.MapGeoreferencedMapEndpoints();
    api.MapTripLogEndpoints();
    api.MapTripReportTemplateEndpoints();
    api.MapTripTypeEndpoints();
    api.MapTripParticipantRoleEndpoints();
    api.MapTripStatisticsEndpoints();
    api.MapTagEndpoints();
    api.MapAuditEndpoints();
    api.MapAccessHistoryEndpoints();
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
    api.MapTerrainBuildEndpoints();
    api.MapSyncEndpoints();

    if (app.Configuration.GetValue("Db:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.MigrateAsync();
        await TaxonomySeeder.SeedAsync(db);
        await MapLayerSeeder.SeedAsync(db);
        await TermRuleSeeder.SeedAsync(db);
        // Permission groups must exist before the bootstrap admin joins Full Administrators.
        await PermissionGroupSeeder.SeedAsync(db);
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);

        // Registration joins new accounts to these groups by slug; a slug naming no
        // group would silently do nothing per signup, so it is called out once here.
        var configuredDefaults = scope.ServiceProvider
            .GetRequiredService<IOptions<AuthOptions>>().Value.DefaultPermissionGroupSlugs;
        if (configuredDefaults.Count > 0)
        {
            var known = await db.PermissionGroups
                .Where(g => configuredDefaults.Contains(g.Slug))
                .Select(g => g.Slug)
                .ToListAsync();
            foreach (var unknown in configuredDefaults.Except(known))
            {
                Log.Warning(
                    "Auth:DefaultPermissionGroups names no existing permission group: {Slug}", unknown);
            }
        }
    }

    // An elevation model is described by the operator and read by nobody else, so a description
    // that contradicts itself has no symptom until somebody notices every cave sitting off its
    // hillside. Said once, at startup, rather than left to be discovered.
    foreach (var warning in app.Services.GetRequiredService<IOptions<TerrainOptions>>()
                 .Value.ConfigurationWarnings())
    {
        Log.Warning("Terrain configuration: {Warning}", warning);
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

        await DemoSeeder.SeedAsync(
            db,
            admins[0].Id,
            scope.ServiceProvider.GetRequiredService<DocumentWriteService>(),
            scope.ServiceProvider.GetRequiredService<IFileStore>());
        Log.Information("Demo data seeded (owner: {Email})", admins[0].Email);
        return;
    }

    // `dotnet run -- seed-speleoloc-dev`: add the second party the demo dataset lacks — a caving
    // group, a plain account in it that owns nothing, and the administrator alongside — then exit.
    // Without it every object on the installation belongs to the one administrator account, so
    // nothing here can show what location protection actually does.
    if (args.Contains("seed-speleoloc-dev"))
    {
        using var scope = app.Services.CreateScope();
        switch (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
        {
            case SpeleoLocDevSeedOutcome.NotPermitted:
                // It creates a login whose password is printed in the installation guide, so it
                // is refused rather than trusted to the operator having read the warning.
                Log.Error(
                    "seed-speleoloc-dev creates a development login and runs only on a "
                    + "development host (set SILEXGIS__SpeleoLocDev__Allow=true to override)");
                return;
            case SpeleoLocDevSeedOutcome.NoAdministrator:
                Log.Error(
                    "seed-speleoloc-dev requires a bootstrap admin (set SILEXGIS__Admin__Email/Password)");
                return;
            default:
                Log.Information(
                    "Development sync data seeded (group: {Group}, member: {Email})",
                    SpeleoLocDevSeeder.GroupName,
                    SpeleoLocDevSeeder.MemberEmail);
                return;
        }
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
