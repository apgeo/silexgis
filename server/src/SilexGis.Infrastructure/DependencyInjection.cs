// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Email;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Messaging;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Settings;
using SilexGis.Infrastructure.Sms;

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
        services.AddSingleton<FeatureAggregateInterceptor>();
        services.AddSingleton<UserIdTransactionInterceptor>();

        services.AddDbContext<SilexGisDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .AddInterceptors(
                sp.GetRequiredService<TimestampInterceptor>(),
                // The aggregate touch must run before the audit diff so the bumped
                // feature row is part of the same audit merge.
                sp.GetRequiredService<FeatureAggregateInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<UserIdTransactionInterceptor>()));

        // One typed-property validator serves every kind-keyed schema in the system
        // (feature properties, document metadata) — the knowledge has a single home.
        services.AddSingleton<ITypedPropertiesValidator, Metadata.JsonSchemaPropertiesValidator>();
        services.AddScoped<Features.FeatureWriteService>();
        services.AddScoped<Features.FeatureIntegrityVerifier>();
        services.AddScoped<Documents.DocumentWriteService>();
        services.AddScoped<Documents.DocumentTypeWriteService>();
        services.AddScoped<Documents.CabinetWriteService>();

        services.AddScoped<Domain.Access.IAccessService, Permissions.AccessService>();
        services.AddScoped<Permissions.FeatureProtection>();
        services.AddScoped<Permissions.AssociationDisclosure>();
        services.AddScoped<Permissions.PhotoPositionDisclosure>();
        services.AddScoped<Permissions.FullAdminGuard>();
        services.AddScoped<Permissions.AccessExplainer>();

        return services;
    }

    /// <summary>
    /// Administrator-editable settings, the message templates, and the two delivery channels.
    /// </summary>
    /// <remarks>
    /// The senders are scoped because they read settings through the DbContext. Both fall back to
    /// writing messages to the log when nothing is configured, so an installation with no mail
    /// server and no SMS gateway still works end to end — every flow completes and the operator
    /// can read the links and codes out of the log.
    /// </remarks>
    public static IServiceCollection AddSilexGisMessaging(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient(Sms.HttpSmsSender.HttpClientName);

        services.AddScoped<IAppSettingsService, AppSettingsService>();
        services.AddScoped<MessageTemplateStore>();

        services.AddScoped<LoggingEmailSender>();
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<SmtpEmailSender>());
        services.AddScoped<IEmailDelivery>(sp => sp.GetRequiredService<SmtpEmailSender>());

        services.AddScoped<HttpSmsSender>();
        services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<HttpSmsSender>());
        services.AddScoped<ISmsDelivery>(sp => sp.GetRequiredService<HttpSmsSender>());

        services.AddScoped<IMessageDispatcher, MessageDispatcher>();

        return services;
    }

    /// <summary>
    /// Notification delivery: the outbox worker and the opt-out tokens it puts in each message.
    /// </summary>
    /// <remarks>
    /// Deliberately not folded into <see cref="AddSilexGisMessaging"/>, which is called from the
    /// authentication setup — hanging a background sender off the auth wiring would be surprising.
    /// Messaging is the transport; this is the thing that decides what to put on it.
    /// </remarks>
    public static IServiceCollection AddSilexGisNotifications(this IServiceCollection services)
    {
        services.AddScoped<NotificationOutboxService>();
        services.AddHostedService<NotificationOutboxWorker>();
        return services;
    }

    /// <summary>File storage, vector format IO (GDAL) and the processing-job worker.</summary>
    public static IServiceCollection AddSilexGisGeodata(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FilesOptions>(configuration.GetSection(FilesOptions.SectionName));
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<IPhotoGeotagReader, MagickPhotoGeotagReader>();
        services.AddSingleton<IContentMetadataReader, ContentMetadataReader>();
        services.AddSingleton<IVectorIO, GdalVectorIO>();
        services.AddSingleton<RasterCogService>();
        services.AddScoped<IProcessingJobHandler, GeofileImportHandler>();
        services.AddScoped<IProcessingJobHandler, RasterCogHandler>();
        services.AddScoped<IProcessingJobHandler, PhotoGeoBackfillHandler>();
        services.AddScoped<IProcessingJobHandler, AccountDataExportHandler>();
        services.AddScoped<IProcessingJobHandler, FeatureIntegrityVerifyHandler>();
        services.AddHostedService<ProcessingJobWorker>();

        services.Configure<FeatureIntegrityOptions>(
            configuration.GetSection(FeatureIntegrityOptions.SectionName));
        services.AddHostedService<FeatureIntegrityScheduler>();
        return services;
    }
}
