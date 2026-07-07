// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SilexGis.Api.Common;
using SilexGis.Api.Features.About;
using SilexGis.Infrastructure;
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

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();
    builder.Services.AddSilexGisPersistence(builder.Configuration);
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<SilexGisDbContext>("database");
    builder.Services.AddOptions<AboutOptions>()
        .BindConfiguration(AboutOptions.SectionName);

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    app.MapOpenApi(); // /openapi/v1.json — the contract the TS client is generated from (03-api-spec.md §6).

    // Liveness: process is up (no dependency checks). Readiness: all registered checks
    // (db/storage checks arrive with Infrastructure wiring in batch 0.2).
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready");

    app.MapAboutEndpoints();

    if (app.Configuration.GetValue("Db:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.MigrateAsync();
        await TaxonomySeeder.SeedAsync(db);
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
