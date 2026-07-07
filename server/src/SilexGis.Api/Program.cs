// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Api.Features.About;
using SilexGis.Api.Features.Caves;
using SilexGis.Api.Features.Map;
using SilexGis.Api.Features.MapLayers;
using SilexGis.Api.Features.Me;
using SilexGis.Api.Features.Search;
using SilexGis.Api.Features.Taxonomies;
using SilexGis.Domain;
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

    // SILEXGIS__{Section}__{Key} environment variables override appsettings (06-deployment.md §3).
    builder.Configuration.AddEnvironmentVariables("SILEXGIS__");

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Contract hygiene (03-api-spec.md §1): enums as strings; strict numbers — the web
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
    builder.Services.AddSilexGisAuth();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<SilexGisDbContext>("database");
    builder.Services.AddOptions<AboutOptions>()
        .BindConfiguration(AboutOptions.SectionName);
    builder.Services.AddOptions<AccessOptions>()
        .BindConfiguration(AccessOptions.SectionName);
    builder.Services.AddScoped<IUserContextAccessor, UserContextAccessor>();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    var app = builder.Build();

    // The API always sits behind a reverse proxy (nginx `web` service / Vite dev proxy —
    // 01-architecture.md §1); honor its scheme/host so OIDC issuer and redirects are right.
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

    app.MapOpenApi(); // /openapi/v1.json — the contract the TS client is generated from (03-api-spec.md §6).

    // Liveness: process is up (no dependency checks). Readiness: all registered checks.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready");

    app.MapConnectEndpoints();

    // Default-deny: everything under /api/v1 requires a bearer token unless an endpoint is
    // explicitly on the anonymous allow-list (03-api-spec.md §7).
    var api = app.MapGroup("/api/v1").RequireAuthorization();
    api.MapAboutEndpoints();
    api.MapAuthEndpoints();
    api.MapMeEndpoints();
    api.MapTaxonomyEndpoints();
    api.MapMapLayerEndpoints();
    api.MapCaveEndpoints();
    api.MapEntranceEndpoints();
    api.MapMapDataEndpoints();
    api.MapSearchEndpoints();

    if (app.Configuration.GetValue("Db:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.MigrateAsync();
        await TaxonomySeeder.SeedAsync(db);
        await MapLayerSeeder.SeedAsync(db);
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
    }

    // `dotnet run -- seed-demo`: load the demo dataset and exit (06-deployment.md §4).
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
