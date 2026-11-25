// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Sms;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Api.Features.Admin;

/// <summary>
/// The operator's view of mail, SMS, sign-in policy and protection disclosure, governed by the
/// Settings domain: reading the page needs Read, saving a section Write, and sending a test
/// message Execute.
/// </summary>
/// <remarks>
/// <para>
/// These are the same settings the deployment can set through environment variables. Configuration
/// supplies the installation's defaults; saving here replaces them. An operator who runs
/// everything from a compose file never opens this page, and one who would rather not redeploy to
/// change a mail server never touches the compose file — neither has to know about the other.
/// </para>
/// <para>
/// Secrets go one way. The password and the authorization header are accepted on write and never
/// returned, so the page reports only whether one is stored.
/// </para>
/// </remarks>
public static class AdminSettingsEndpoints
{
    public static RouteGroupBuilder MapAdminSettingsEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/settings").WithTags("Admin");

        admin.MapGet("/", GetAsync)
            .WithSummary("Mail, SMS, sign-in policy and protection disclosure, with secrets redacted.");
        admin.MapPut("/mail", SaveMailAsync)
            .WithValidation<MailSettingsWriteRequest>()
            .WithSummary("Saves the mail server settings.");
        admin.MapPut("/sms", SaveSmsAsync)
            .WithValidation<SmsSettingsWriteRequest>()
            .WithSummary("Saves the SMS gateway settings.");
        admin.MapPut("/security", SaveSecurityAsync)
            .WithValidation<SecuritySettingsDto>()
            .WithSummary("Saves the sign-in policy: address confirmation and which second factors are allowed.");
        admin.MapPut("/protection", SaveProtectionAsync)
            .WithValidation<ProtectionSettingsDto>()
            .WithSummary("Saves what the installation discloses about a protected feature's associations.");
        admin.MapPut("/import", SaveImportAsync)
            .WithValidation<ImportSettingsDto>()
            .WithSummary("Saves whether a vector import may create objects without review, and how far duplicate detection looks.");
        admin.MapPut("/interface", SaveInterfaceAsync)
            .WithValidation<InterfaceSettingsDto>()
            .WithSummary("Saves the starting interface arrangement new users begin from; a default, never a policy.");
        admin.MapPut("/notifications", SaveNotificationsAsync)
            .WithValidation<NotificationSettingsDto>()
            .WithSummary("Saves how long notifications are kept before they and the record of how they were sent are deleted.");
        admin.MapPut("/announcements", SaveAnnouncementsAsync)
            .WithValidation<AnnouncementSettingsDto>()
            .WithSummary("Saves whether an announcement to a caving group may cost money, and how much in a day.");
        admin.MapPut("/sync", SaveSyncAsync)
            .WithValidation<SyncSettingsDto>()
            .WithSummary("Saves what the installation offers phones: the people directory and whom it lists, trip conversion, and how long waiting bytes are kept.");
        admin.MapPut("/survey-uploads", SaveSurveyUploadsAsync)
            .WithValidation<SurveyUploadSettingsDto>()
            .WithSummary("Saves what the installation decides about surveys sent from a field device: whether a cave an upload created takes that upload's first model as its shape.");
        admin.MapPut("/test-data", SaveTestDataAsync)
            .WithValidation<TestDataSettingsWriteRequest>()
            .WithSummary(
                "Switches the test-data generator on or off for full administrators. "
                + "Works only where the deployment allows the generator at all (TestData:Allowed).");
        admin.MapPut("/map-symbols", SaveMapSymbolsAsync)
            .WithValidation<MapSymbolSettingsDto>()
            .WithSummary(
                "Saves the markers cave entrances are drawn with on the map and in the 3D scene: "
                + "one for an entrance at its surveyed position, one for an entrance shown approximately.");
        admin.MapPut("/photo-libraries", SavePhotoLibrariesAsync)
            .WithValidation<PhotoLibrarySuspensionDto>()
            .WithSummary(
                "Stops or resumes this installation's use of each neighbouring photo library. "
                + "Cannot connect one: which libraries exist is the deployment's decision.");
        // The two routes here that make the installation send something to a destination the
        // caller types in, so both carry the per-address budget the credential surfaces use.
        // "May change the settings" is not "may message any address in the world as fast as a
        // script can ask". The text one costs the operator real money on every call, so it has a
        // second, durable cooldown of its own inside the handler.
        admin.MapPost("/mail/test", TestMailAsync)
            .WithValidation<TestMessageRequest>()
            .RequireRateLimiting("auth")
            .WithSummary("Sends a test message to prove the mail server works.");
        admin.MapPost("/sms/test", TestSmsAsync)
            .WithValidation<TestMessageRequest>()
            .RequireRateLimiting("auth")
            .WithSummary("Sends a test text to prove the gateway works.");

        return api;
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveMailAsync(
        MailSettingsWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Mail, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        var current = await settings.GetMailAsync(ct);
        var host = Text(request.Host);
        var dropPassword = request.Password is null && MessagingEndpointRules.MailServerMoved(current, host, request.Port);
        await settings.SaveAsync(
            AppSettingSections.Mail,
            new MailSettings
            {
                Enabled = request.Enabled,
                Host = host,
                Port = request.Port,
                Security = request.Security,
                Username = Blank(request.Username),
                Password = dropPassword ? null : KeepOrReplaceSecret(current.Password, request.Password),
                FromAddress = Text(request.FromAddress),
                FromName = Text(request.FromName),
                ReplyTo = Blank(request.ReplyTo),
                TimeoutSeconds = request.TimeoutSeconds,
                AcceptInvalidCertificate = request.AcceptInvalidCertificate,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Mail, emailDelivery, smsDelivery, testData.Value, ct,
            dropPassword && current.Password is not null ? [MessagingEndpointRules.MailPasswordClearedCode] : []));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSmsAsync(
        SmsSettingsWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        IOptions<SmsGatewayOptions> gateway,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var url = Text(request.Url);
        if (!gateway.Value.AllowInternalAddresses
            && Uri.TryCreate(url, UriKind.Absolute, out var address)
            && await SmsGatewayAddress.PointsInsideAsync(address.DnsSafeHost, ct))
        {
            return ApiProblems.BadRequest(
                MessagingEndpointRules.SmsGatewayInternalCode,
                "The SMS gateway address is on this machine or on an internal network. An installation whose gateway runs there allows it in its own configuration.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Sms, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        var current = await settings.GetSmsAsync(ct);
        var dropAuthHeader = request.AuthHeader is null && MessagingEndpointRules.SmsGatewayMoved(current, url);
        await settings.SaveAsync(
            AppSettingSections.Sms,
            new SmsSettings
            {
                Enabled = request.Enabled,
                Url = url,
                Method = Text(request.Method).ToUpperInvariant(),
                ContentType = Text(request.ContentType),
                BodyTemplate = request.BodyTemplate ?? string.Empty,
                Headers = request.Headers is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase),
                AuthHeader = dropAuthHeader ? null : KeepOrReplaceSecret(current.AuthHeader, request.AuthHeader),
                From = Blank(request.From),
                TimeoutSeconds = request.TimeoutSeconds,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Sms, emailDelivery, smsDelivery, testData.Value, ct,
            dropAuthHeader && current.AuthHeader is not null ? [MessagingEndpointRules.SmsAuthHeaderClearedCode] : []));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSecurityAsync(
        SecuritySettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Security, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Security,
            new SecuritySettings
            {
                RequireConfirmedEmail = request.RequireConfirmedEmail,
                SendConfirmationOnRegistration = request.SendConfirmationOnRegistration,
                AuthenticatorTwoFactorEnabled = request.AuthenticatorTwoFactorEnabled,
                EmailTwoFactorEnabled = request.EmailTwoFactorEnabled,
                SmsTwoFactorEnabled = request.SmsTwoFactorEnabled,
                TwoFactorCodeLifetimeMinutes = request.TwoFactorCodeLifetimeMinutes,
                TwoFactorResendIntervalSeconds = request.TwoFactorResendIntervalSeconds,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Security, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSyncAsync(
        SyncSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Sync, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Sync,
            new SyncSettings
            {
                PeopleDirectoryEnabled = request.PeopleDirectoryEnabled,
                PeopleDirectoryScope = request.PeopleDirectoryScope,
                ConvertTripsWithProblems = request.ConvertTripsWithProblems,
                StagedUploadRetentionDays = request.StagedUploadRetentionDays,
                RejectedHeldBytesRetentionDays = request.RejectedHeldBytesRetentionDays,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Sync, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSurveyUploadsAsync(
        SurveyUploadSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.SurveyUploads, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.SurveyUploads,
            new SurveyUploadSettings { FirstModelOfCreatedCave = request.FirstModelOfCreatedCave },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.SurveyUploads, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveProtectionAsync(
        ProtectionSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Protection, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Protection,
            new ProtectionSettings
            {
                RevealProtectedAssociations = request.RevealProtectedAssociations,
                CalendarFeedEnabled = request.CalendarFeedEnabled,
                KeepLocationInPhotoFiles = request.KeepLocationInPhotoFiles,
                ServeUnstrippableOriginalsToLocationReaders = request.ServeUnstrippableOriginalsToLocationReaders,
                DescribeLocationFreeCopies = request.DescribeLocationFreeCopies,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Protection, emailDelivery, smsDelivery, testData.Value, ct));
    }

    /// <summary>
    /// Saves the retention window. It was a deployment key alone, and stays readable as one: an
    /// installation that never opens this page keeps whatever its environment says, and one that
    /// saves here stops having to think about the environment for this value.
    /// </summary>
    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveNotificationsAsync(
        NotificationSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Notifications, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Notifications,
            new NotificationSettings { RetentionDays = request.RetentionDays },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Notifications, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveAnnouncementsAsync(
        AnnouncementSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Announcements, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Announcements,
            new AnnouncementSettings
            {
                PaidChannelsEnabled = request.PaidChannelsEnabled,
                DailyPaidMessageCap = request.DailyPaidMessageCap,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Announcements, emailDelivery, smsDelivery, testData.Value, ct));
    }

    /// <summary>
    /// Stops or resumes this installation's use of each neighbouring photo library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A brake and only a brake. Which photo libraries this installation has is decided by the
    /// deployment that supplied their addresses and credentials, and nothing saved here can supply
    /// one: a library switched "off" here is stopped and can be started again, and a library the
    /// deployment never gave this installation cannot be conjured into existence from a settings
    /// screen. The asymmetry is deliberate and is the whole shape of the feature — the value stored
    /// says <em>suspended</em>, never <em>enabled</em>, so there is no state in which these pages
    /// claim a library that is not there.
    /// </para>
    /// <para>
    /// Saving it changes nothing on the far side: a suspended library goes on running at its own
    /// address, goes on indexing, and goes on serving anyone who signs into it directly. What stops
    /// is this application asking it anything.
    /// </para>
    /// <para>
    /// Both switches are written together because saving a section replaces the whole stored
    /// document, so a request carrying one would release the other.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SavePhotoLibrariesAsync(
        PhotoLibrarySuspensionDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.PhotoLibrarySuspension, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.PhotoLibrarySuspension,
            new PhotoLibrarySuspensionSettings
            {
                ImmichSuspended = request.ImmichSuspended,
                PhotoPrismSuspended = request.PhotoPrismSuspended,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.PhotoLibrarySuspension, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveImportAsync(
        ImportSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Import, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Import,
            new ImportSettings
            {
                AllowCreateWithoutReview = request.AllowCreateWithoutReview,
                DuplicateRadiusMeters = request.DuplicateRadiusMeters,
                DuplicateNameSimilarity = request.DuplicateNameSimilarity,
                PhotoProximityRadiusMeters = request.PhotoProximityRadiusMeters,
                PhotoClusterRadiusMeters = request.PhotoClusterRadiusMeters,
            },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Import, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveInterfaceAsync(
        InterfaceSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.Interface, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.Interface,
            new InterfaceSettings { PanelDefaults = request.PanelDefaults },
            ct);

        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.Interface, emailDelivery, smsDelivery, testData.Value, ct));
    }

    /// <summary>
    /// Sends a plain diagnostic message rather than a catalogued one: this is proving the transport
    /// works, and routing it through a template the operator may have just broken would confuse the
    /// two questions.
    /// </summary>
    private static async Task<Results<Ok<TestMessageResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> TestMailAsync(
        TestMessageRequest request,
        IAccessContextAccessor accessAccessor,
        IEmailSender emailSender,
        IEmailDelivery emailDelivery,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        if (!await emailDelivery.IsConfiguredAsync(ct))
        {
            return ApiProblems.BadRequest("admin.mail_not_configured", "No mail server is configured.");
        }

        try
        {
            await emailSender.SendAsync(
                request.Recipient.Trim(),
                "SilexGIS test message",
                "This is a test message from SilexGIS. If you received it, mail delivery is working.",
                ct);
            return TypedResults.Ok(new TestMessageResultDto(true, null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The failure is the answer here, not an error: the operator is trying to find out
            // what is wrong, and the exception message is usually exactly that.
            return TypedResults.Ok(new TestMessageResultDto(false, ex.Message));
        }
    }

    private static async Task<Results<Ok<TestMessageResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> TestSmsAsync(
        TestMessageRequest request,
        IAccessContextAccessor accessAccessor,
        AdminTestSendThrottle throttle,
        ISmsSender smsSender,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        if (!await smsDelivery.IsConfiguredAsync(ct))
        {
            return ApiProblems.BadRequest("admin.sms_not_configured", "No SMS gateway is configured.");
        }

        // A durable cooldown, not the per-address request budget: that budget is shared with the
        // sign-in routes, it refills every minute, and a second replica of this application keeps
        // a second copy of it — while every call it lets through sends a text the operator pays
        // for. The budget stays as the outer guard; this is the bound.
        if (await throttle.TooSoonAsync(ctx.UserId))
        {
            return ApiProblems.BadRequest(
                "admin.sms_test_too_soon", "Wait a moment before sending another test text.");
        }

        await throttle.MarkSentAsync(ctx.UserId);

        try
        {
            await smsSender.SendAsync(
                request.Recipient.Replace(" ", string.Empty),
                "SilexGIS test message. If you received this, SMS delivery is working.",
                ct);
            return TypedResults.Ok(new TestMessageResultDto(true, null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TypedResults.Ok(new TestMessageResultDto(false, ex.Message));
        }
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveTestDataAsync(
        TestDataSettingsWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.TestData, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(AppSettingSections.TestData, new TestDataSettings { Enabled = request.Enabled }, ct);
        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.TestData, emailDelivery, smsDelivery, testData.Value, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveMapSymbolsAsync(
        MapSymbolSettingsDto request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        IOptions<TestDataOptions> testData,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await HoldSectionAsync(http, db, settings, AppSettingSections.MapSymbols, testData.Value, ct) is { } stale)
        {
            return stale;
        }

        await settings.SaveAsync(
            AppSettingSections.MapSymbols,
            new MapSymbolSettings
            {
                EntranceSymbol = request.EntranceSymbol,
                ApproximateEntranceSymbol = request.ApproximateEntranceSymbol,
            },
            ct);
        return TypedResults.Ok(await AnswerSavedAsync(
            http, transaction, settings, AppSettingSections.MapSymbols, emailDelivery, smsDelivery, testData.Value, ct));
    }

    /// <summary>
    /// Opens a section's save, inside the caller's transaction: serialises the saves of that
    /// section until the commit, and refuses the save when it names a version of the section other
    /// than the one stored now.
    /// </summary>
    /// <remarks>
    /// Saving a section replaces it whole, so a save made from a page opened before somebody else's
    /// save would put back every field they changed. The version is offered rather than required,
    /// like every other versioned write here: a save without <c>If-Match</c> keeps
    /// last-write-wins. The section is read past the cache, because the cache may still hold a
    /// copy older than the stored row, and a version computed from that copy would let the save
    /// through over the change it never saw.
    /// </remarks>
    private static async Task<ProblemHttpResult?> HoldSectionAsync(
        HttpContext http,
        SilexGisDbContext db,
        IAppSettingsService settings,
        string section,
        TestDataOptions testData,
        CancellationToken ct)
    {
        await ConcurrencySql.LockWholeSetAsync(db, $"settings:{section}", ct);
        settings.Forget(section);
        return Concurrency.CheckIfMatchVersion(http, SectionVersion(await ShownAsync(settings, section, testData, ct)));
    }

    /// <summary>
    /// Commits a section's save and answers with the whole page as it now stands, the saved
    /// section's new version in the <c>ETag</c> header as well as in the body.
    /// </summary>
    private static async Task<AdminSettingsDto> AnswerSavedAsync(
        HttpContext http,
        IDbContextTransaction transaction,
        IAppSettingsService settings,
        string section,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        TestDataOptions testData,
        CancellationToken ct,
        IReadOnlyList<string>? notices = null)
    {
        await transaction.CommitAsync(ct);
        // Dropped again now that the row is committed: a read made between the save and the commit
        // could have cached the old value.
        settings.Forget(section);
        var snapshot = await SnapshotAsync(settings, emailDelivery, smsDelivery, testData, ct, notices);
        http.Response.Headers.ETag = $"\"{SectionVersion(await ShownAsync(settings, section, testData, ct))}\"";
        return snapshot;
    }

    private static readonly JsonSerializerOptions VersionSerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A section's version: a digest of the section exactly as the page is shown it. It moves
    /// when what the page shows moves and only then, and it never carries a secret, which the
    /// page is shown only as whether one is stored.
    /// </summary>
    private static string SectionVersion(object shown) =>
        Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(shown, shown.GetType(), VersionSerializerOptions))[..16]);

    /// <summary>One section as the page is shown it.</summary>
    private static async Task<object> ShownAsync(
        IAppSettingsService settings, string section, TestDataOptions testData, CancellationToken ct) =>
        section switch
        {
            AppSettingSections.Mail => MailShown(await settings.GetMailAsync(ct)),
            AppSettingSections.Sms => SmsShown(await settings.GetSmsAsync(ct)),
            AppSettingSections.Security => SecurityShown(await settings.GetSecurityAsync(ct)),
            AppSettingSections.Protection => ProtectionShown(await settings.GetProtectionAsync(ct)),
            AppSettingSections.Import => ImportShown(await settings.GetImportAsync(ct)),
            AppSettingSections.Interface => new InterfaceSettingsDto((await settings.GetInterfaceAsync(ct)).PanelDefaults),
            AppSettingSections.Notifications =>
                new NotificationSettingsDto((await settings.GetNotificationsAsync(ct)).EffectiveRetentionDays),
            AppSettingSections.Announcements => AnnouncementsShown(await settings.GetAnnouncementsAsync(ct)),
            AppSettingSections.PhotoLibrarySuspension => PhotoLibrariesShown(await settings.GetPhotoLibrarySuspensionAsync(ct)),
            AppSettingSections.Sync => SyncShown(await settings.GetSyncAsync(ct)),
            AppSettingSections.SurveyUploads =>
                new SurveyUploadSettingsDto((await settings.GetSurveyUploadsAsync(ct)).FirstModelOfCreatedCave),
            AppSettingSections.TestData => new TestDataSettingsDto((await settings.GetTestDataAsync(ct)).Enabled, testData.Allowed),
            AppSettingSections.MapSymbols => MapSymbolsShown(await settings.GetMapSymbolsAsync(ct)),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Not a section of the settings page."),
        };

    private static async Task<AdminSettingsDto> SnapshotAsync(
        IAppSettingsService settings, IEmailDelivery emailDelivery, ISmsDelivery smsDelivery, TestDataOptions testData, CancellationToken ct,
        IReadOnlyList<string>? notices = null)
    {
        // Each section is read once and its version taken from that same reading, so the values
        // and the version answered for a section always belong together.
        async Task<(T Shown, string Version)> Read<T>(string section)
        {
            var shown = await ShownAsync(settings, section, testData, ct);
            return ((T)shown, SectionVersion(shown));
        }

        var mail = await Read<MailSettingsDto>(AppSettingSections.Mail);
        var sms = await Read<SmsSettingsDto>(AppSettingSections.Sms);
        var security = await Read<SecuritySettingsDto>(AppSettingSections.Security);
        var protection = await Read<ProtectionSettingsDto>(AppSettingSections.Protection);
        var import = await Read<ImportSettingsDto>(AppSettingSections.Import);
        var ui = await Read<InterfaceSettingsDto>(AppSettingSections.Interface);
        var notifications = await Read<NotificationSettingsDto>(AppSettingSections.Notifications);
        var announcements = await Read<AnnouncementSettingsDto>(AppSettingSections.Announcements);
        var photoLibraries = await Read<PhotoLibrarySuspensionDto>(AppSettingSections.PhotoLibrarySuspension);
        var sync = await Read<SyncSettingsDto>(AppSettingSections.Sync);
        var surveyUploads = await Read<SurveyUploadSettingsDto>(AppSettingSections.SurveyUploads);
        var generator = await Read<TestDataSettingsDto>(AppSettingSections.TestData);
        var symbols = await Read<MapSymbolSettingsDto>(AppSettingSections.MapSymbols);

        return new AdminSettingsDto(
            mail.Shown,
            sms.Shown,
            security.Shown,
            protection.Shown,
            import.Shown,
            ui.Shown,
            notifications.Shown,
            announcements.Shown,
            photoLibraries.Shown,
            sync.Shown,
            surveyUploads.Shown,
            generator.Shown,
            symbols.Shown,
            await emailDelivery.IsConfiguredAsync(ct),
            await smsDelivery.IsConfiguredAsync(ct),
            notices ?? [],
            new AdminSettingsVersionsDto(
                mail.Version,
                sms.Version,
                security.Version,
                protection.Version,
                import.Version,
                ui.Version,
                notifications.Version,
                announcements.Version,
                photoLibraries.Version,
                sync.Version,
                surveyUploads.Version,
                generator.Version,
                symbols.Version));
    }

    private static MailSettingsDto MailShown(MailSettings mail) =>
        new(
            mail.Enabled,
            mail.Host,
            mail.Port,
            mail.Security,
            mail.Username,
            !string.IsNullOrEmpty(mail.Password),
            mail.FromAddress,
            mail.FromName,
            mail.ReplyTo,
            mail.TimeoutSeconds,
            mail.AcceptInvalidCertificate);

    private static SmsSettingsDto SmsShown(SmsSettings sms) =>
        new(
            sms.Enabled,
            sms.Url,
            sms.Method,
            sms.ContentType,
            sms.BodyTemplate,
            sms.Headers,
            !string.IsNullOrEmpty(sms.AuthHeader),
            sms.From,
            sms.TimeoutSeconds);

    private static SecuritySettingsDto SecurityShown(SecuritySettings security) =>
        new(
            security.RequireConfirmedEmail,
            security.SendConfirmationOnRegistration,
            security.AuthenticatorTwoFactorEnabled,
            security.EmailTwoFactorEnabled,
            security.SmsTwoFactorEnabled,
            security.TwoFactorCodeLifetimeMinutes,
            security.TwoFactorResendIntervalSeconds);

    private static ProtectionSettingsDto ProtectionShown(ProtectionSettings disclosure) =>
        new(
            disclosure.RevealProtectedAssociations,
            disclosure.CalendarFeedEnabled,
            disclosure.KeepLocationInPhotoFiles,
            disclosure.ServeUnstrippableOriginalsToLocationReaders,
            disclosure.DescribeLocationFreeCopies);

    private static ImportSettingsDto ImportShown(ImportSettings import) =>
        new(
            import.AllowCreateWithoutReview,
            import.DuplicateRadiusMeters,
            import.DuplicateNameSimilarity,
            import.PhotoProximityRadiusMeters,
            import.PhotoClusterRadiusMeters);

    private static AnnouncementSettingsDto AnnouncementsShown(AnnouncementSettings announcements) =>
        new(announcements.PaidChannelsEnabled, announcements.EffectiveDailyPaidMessageCap);

    private static PhotoLibrarySuspensionDto PhotoLibrariesShown(PhotoLibrarySuspensionSettings photoLibraries) =>
        new(photoLibraries.ImmichSuspended, photoLibraries.PhotoPrismSuspended);

    private static SyncSettingsDto SyncShown(SyncSettings sync) =>
        new(
            sync.PeopleDirectoryEnabled,
            sync.PeopleDirectoryScope,
            sync.ConvertTripsWithProblems,
            sync.StagedUploadRetentionDays,
            sync.RejectedHeldBytesRetentionDays);

    private static MapSymbolSettingsDto MapSymbolsShown(MapSymbolSettings symbols) =>
        new(symbols.EntranceSymbol, symbols.ApproximateEntranceSymbol);

    /// <summary>
    /// Null leaves a stored secret alone — the page never received it, so it cannot send it back.
    /// An empty string is an explicit "there is no secret any more".
    /// </summary>
    private static string? KeepOrReplaceSecret(string? stored, string? supplied) =>
        supplied is null ? stored : Blank(supplied);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A text field the stored section always has. A switched-off channel may be saved half
    /// filled, and the validator only asks for these once it is switched on, so a field the form
    /// sent as null is stored blank rather than failing the save.
    /// </summary>
    private static string Text(string? value) => value?.Trim() ?? string.Empty;
}
