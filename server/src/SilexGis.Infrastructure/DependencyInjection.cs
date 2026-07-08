// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSilexGisPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetValue<string>("Db:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. " +
                "Set Db:ConnectionString (environment: SILEXGIS__Db__ConnectionString).");
        }

        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddSingleton<TimestampInterceptor>();
        services.AddSingleton<AuditInterceptor>();

        services.AddDbContext<SilexGisDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .AddInterceptors(
                sp.GetRequiredService<TimestampInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>()));

        return services;
    }

    /// <summary>File storage, vector format IO (GDAL) and the processing-job worker.</summary>
    public static IServiceCollection AddSilexGisGeodata(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FilesOptions>(configuration.GetSection(FilesOptions.SectionName));
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<IVectorIO, GdalVectorIO>();
        services.AddScoped<IProcessingJobHandler, GeofileImportHandler>();
        services.AddHostedService<ProcessingJobWorker>();
        return services;
    }
}
