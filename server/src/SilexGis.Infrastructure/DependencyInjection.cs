// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // One typed-property validator serves every kind-keyed schema in the system (feature
        // properties, document metadata, the three sections of a trip report) — the knowledge
        // has a single home.
        services.AddSingleton<ITypedPropertiesValidator, Metadata.JsonSchemaPropertiesValidator>();
        services.AddSingleton<Geodata.ICrsRegistry, Geodata.ProjCrsRegistry>();
        services.AddSingleton<Geodata.ICoordinateProjector, Geodata.ProjCoordinateProjector>();
        services.AddSingleton<Surveys.SurveyMeshConverter>();
        services.AddScoped<Features.FeatureWriteService>();
        services.AddScoped<Features.FeatureIntegrityVerifier>();
        services.AddScoped<Import.TermRuleSetStore>();
        services.AddScoped<Import.VisibleProximitySearch>();
        services.AddScoped<Import.ImportCandidateService>();
        services.AddScoped<Import.ImportCommitService>();
        services.AddScoped<Import.PhotoCandidateService>();
        services.AddScoped<Import.PhotoCommitService>();
        services.AddScoped<Trips.TripTypeWriteService>();
        services.AddScoped<Trips.TripSectionWriter>();
        services.AddScoped<Documents.DocumentWriteService>();
        services.AddScoped<Documents.DocumentTypeWriteService>();
        services.AddScoped<Documents.CabinetWriteService>();
        services.AddScoped<Documents.AlbumWriteService>();
        services.AddScoped<Documents.UploadAllowanceService>();
        services.AddScoped<Documents.UploadIngestService>();
        services.AddScoped<Documents.UploadBatchService>();
        services.AddScoped<Documents.BulkIngestRunner>();
        services.AddScoped<Documents.ServerDirectorySource>();

        services.AddScoped<Domain.Access.IAccessService, Permissions.AccessService>();
        services.AddScoped<Permissions.FeatureProtection>();
        services.AddScoped<Permissions.AssociationDisclosure>();
        services.AddScoped<Permissions.PhotoPositionDisclosure>();
        services.AddScoped<Permissions.FullAdminGuard>();
        services.AddScoped<Permissions.AccessExplainer>();

        // The filterable worlds, and the one registry everything asks. Registration order is the
        // order results are shown in, so it is the order a person reads them in rather than an
        // implementation detail. A world added here without a fixture fails the conformance suite.
        services.AddScoped<Filters.IFilterWorld, Filters.FeatureFilterWorld>();
        services.AddScoped<Filters.IFilterWorld, Filters.TripLogFilterWorld>();
        services.AddScoped<Filters.IFilterWorld, Filters.DocumentFilterWorld>();
        services.AddScoped<Filters.IFilterWorld, Filters.MapViewFilterWorld>();
        services.AddScoped<Filters.FilterWorldRegistry>();

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
    /// Notification routing and delivery: the worker, its clock, and the opt-out tokens it
    /// puts in each message.
    /// </summary>
    /// <remarks>
    /// Deliberately not folded into <see cref="AddSilexGisMessaging"/>, which is called from the
    /// authentication setup — hanging a background sender off the auth wiring would be surprising.
    /// Messaging is the transport; this is the thing that decides what to put on it.
    /// </remarks>
    public static IServiceCollection AddSilexGisNotifications(this IServiceCollection services)
    {
        // The one clock this pipeline reads. Introduced narrowly rather than swept through the
        // solution: what it buys is a test able to assert that a failed send set the next attempt
        // to exactly the backoff ladder's value. It cannot move a claim — whether a row is due is
        // decided by the database's own now(), not by this.
        services.TryAddSingleton(TimeProvider.System);

        // The ways a notification can leave the system. One registration per transport, and the
        // router asks all of them — so a second channel is this list growing by a line, not a
        // branch appearing in the routing pass. In-app is deliberately not here: the notification
        // row's own existence is its in-app presence and nothing about it can fail.
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<INotificationChannel, SmsNotificationChannel>();
        services.AddScoped<NotificationChannels>();

        services.AddScoped<NotificationOptOut>();
        services.AddScoped<NotificationDeliveryService>();

        // Reading a notification needs the reader's own account row for the language to
        // fall back to, which a feature slice may not touch — so the wording is written out
        // here, on its behalf.
        services.AddScoped<NotificationInboxRenderer>();
        services.AddHostedService<NotificationWorker>();
        return services;
    }

    /// <summary>
    /// File storage, vector format IO (GDAL), the processing-job worker, and the schedules that
    /// queue work for it.
    /// </summary>
    public static IServiceCollection AddSilexGisGeodata(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FilesOptions>(configuration.GetSection(FilesOptions.SectionName));

        // Laying an office document out needs an office suite, which is a service of its own
        // and far more machinery than a small installation should have to run. It is optional,
        // off unless the operator deploys it, and the converter reports that plainly instead of
        // being absent from the container — so every caller can say "nothing here can do this"
        // rather than crash or stay silent.
        services.Configure<Documents.Conversion.ConversionOptions>(
            configuration.GetSection(Documents.Conversion.ConversionOptions.SectionName));
        services.AddHttpClient(Documents.Conversion.HttpDocumentConverter.HttpClientName);
        services.AddSingleton<Domain.Documents.IDocumentConverter,
            Documents.Conversion.HttpDocumentConverter>();

        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<PageRenderService>();
        services.AddSingleton<IPhotoGeotagReader, MagickPhotoGeotagReader>();
        services.AddSingleton<IContentMetadataReader, ContentMetadataReader>();
        services.AddScoped<ContentIntake>();
        services.AddSingleton<IVectorIO, GdalVectorIO>();
        services.AddSingleton<RasterCogService>();
        services.AddSingleton<Documents.ISpreadsheetWriter, Documents.XlsxSpreadsheetWriter>();
        services.AddSingleton<Documents.IDocumentWriter, Documents.DocxDocumentWriter>();

        // Readers of stored files' text layers. Stateless, so one of each serves everything;
        // the selector is what turns a stored format into the reader that understands it.
        // First, so that the format this application writes is claimed by the reader that
        // understands it whatever any later reader's media-type test grows to accept.
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.AnnotatedTextExtractor>();
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.PlainTextExtractor>();
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.OpenDocumentTextExtractor>();
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.RichTextExtractor>();
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.PdfTextExtractor>();
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.OfficeOpenXmlTextExtractor>();
        services.AddSingleton<Documents.Extraction.ITextExtractor,
            Documents.Extraction.LegacyOfficeTextExtractor>();
        services.AddSingleton<Documents.Extraction.TextExtractorSelector>();

        services.AddScoped<Documents.AnnotatedTextService>();

        services.AddScoped<IProcessingJobHandler, GeofileImportHandler>();
        services.AddScoped<IProcessingJobHandler, RasterCogHandler>();
        services.AddScoped<IProcessingJobHandler, SurveyMeshHandler>();
        services.AddScoped<IProcessingJobHandler, PhotoGeoBackfillHandler>();
        services.AddScoped<IProcessingJobHandler, AccountDataExportHandler>();
        services.AddScoped<IProcessingJobHandler, FeatureIntegrityVerifyHandler>();
        services.AddScoped<IProcessingJobHandler, TextExtractionHandler>();
        services.AddScoped<IProcessingJobHandler, TextExtractionBackfillHandler>();
        services.AddScoped<IProcessingJobHandler, AccessHistoryPruneHandler>();
        services.AddScoped<IProcessingJobHandler, DocumentConversionHandler>();
        services.AddScoped<IProcessingJobHandler, DocumentConversionBackfillHandler>();
        services.AddScoped<IProcessingJobHandler, ArchiveExpansionHandler>();
        services.AddScoped<IProcessingJobHandler, DirectoryImportHandler>();
        services.AddScoped<IProcessingJobHandler, UploadSessionSweepHandler>();
        services.AddScoped<IProcessingJobHandler, DocumentPurgeHandler>();
        services.AddScoped<IProcessingJobHandler, CavingGroupAnnouncementHandler>();

        // The terrain chain: the handler that walks a build through the steps, and the directories
        // it works in. The steps themselves are registered as each is built — the walk runs the
        // ones that are there, in order, and stops at the first one nothing implements yet.
        services.Configure<Terrain.TerrainBuildOptions>(
            configuration.GetSection(Terrain.TerrainBuildOptions.SectionName));
        services.AddSingleton<Terrain.TerrainWorkspace>();
        services.AddSingleton<Terrain.TerrainUploads>();
        services.AddScoped<IProcessingJobHandler, TerrainBuildHandler>();

        // Obtaining the rasters: a client for the open elevation dataset, and the step that puts
        // everything a build was given — downloaded, uploaded or read from a directory the operator
        // listed — into the one directory the rest of the chain reads.
        services.AddHttpClient(Terrain.CopernicusFetcher.HttpClientName);
        services.AddScoped<Terrain.CopernicusFetcher>();
        services.AddScoped<Terrain.ITerrainPhase, Terrain.TerrainFetchPhase>();

        // Turning those rasters into the one form everything after them reads. Stateless and holding
        // nothing between calls, so one instance serves whoever asks. The step is registered after
        // the one that obtains the rasters because the walk takes the first implementation claiming
        // a given step, so registration order is what decides which one that is.
        services.AddSingleton<Domain.Terrain.ITerrainRasterPreparer, Terrain.GdalTerrainRasterPreparer>();
        services.AddScoped<Terrain.ITerrainPhase, Terrain.TerrainPreparePhase>();

        // Turning those rasters into tiles. The program that does that is a command-line tool, run
        // by a service of its own that this application never speaks to directly — the two meet on
        // a directory they share — and that service is optional. So the step is registered whether
        // or not anything is deployed to answer it, and says plainly when nothing is: a step left
        // out of the container instead makes every build stop at the one before it and report
        // success, having made nothing.
        services.AddScoped<Terrain.ITerrainPhase, Terrain.TerrainBakePhase>();

        // Reading the tiles back before anything is allowed to believe in them. Every way a pyramid
        // can be wrong is silent — a damaged tile, a level advertised and empty, tiles held and
        // advertised nowhere all end up drawing plausible ground at the wrong height with no error
        // anywhere — so this step is not optional and is never skipped.
        services.AddScoped<Terrain.ITerrainPhase, Terrain.TerrainValidatePhase>();

        // Moving the checked pyramid to where it is served from, in one rename, into an address of
        // this build's own. Registered last because the walk runs the steps in order and stops at
        // the first one nothing implements: without it a build ends as a success whose tiles sit in
        // a directory nothing serves.
        services.AddScoped<Terrain.ITerrainPhase, Terrain.TerrainPublishPhase>();

        services.AddHostedService<ProcessingJobWorker>();

        // Terrain builds are claimed by a worker of their own against the same table. A worker
        // takes one job at a time with no time limit, and a build runs for minutes to hours, so on
        // the general worker one build would hold up every conversion, reading and sweep behind it
        // for its whole duration.
        services.AddHostedService<TerrainProcessingJobWorker>();
        services.AddHostedService<UploadSessionScheduler>();

        services.Configure<DocumentRetentionOptions>(
            configuration.GetSection(DocumentRetentionOptions.SectionName));
        services.AddHostedService<DocumentPurgeScheduler>();

        services.Configure<FeatureIntegrityOptions>(
            configuration.GetSection(FeatureIntegrityOptions.SectionName));
        services.AddHostedService<FeatureIntegrityScheduler>();

        services.Configure<AccessHistoryOptions>(
            configuration.GetSection(AccessHistoryOptions.SectionName));
        services.AddScoped<FileAccessRecorder>();
        services.AddHostedService<AccessHistoryScheduler>();

        services.Configure<TripCalloutOptions>(
            configuration.GetSection(TripCalloutOptions.SectionName));
        services.AddScoped<IProcessingJobHandler, TripCalloutSweepHandler>();
        services.AddHostedService<TripCalloutScheduler>();
        return services;
    }
}
