// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;

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

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveMailAsync(
        MailSettingsWriteRequest request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        var current = await settings.GetMailAsync(ct);
        await settings.SaveAsync(
            AppSettingSections.Mail,
            new MailSettings
            {
                Enabled = request.Enabled,
                Host = request.Host.Trim(),
                Port = request.Port,
                Security = request.Security,
                Username = Blank(request.Username),
                Password = KeepOrReplaceSecret(current.Password, request.Password),
                FromAddress = request.FromAddress.Trim(),
                FromName = request.FromName.Trim(),
                ReplyTo = Blank(request.ReplyTo),
                TimeoutSeconds = request.TimeoutSeconds,
                AcceptInvalidCertificate = request.AcceptInvalidCertificate,
            },
            ct);

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSmsAsync(
        SmsSettingsWriteRequest request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        var current = await settings.GetSmsAsync(ct);
        await settings.SaveAsync(
            AppSettingSections.Sms,
            new SmsSettings
            {
                Enabled = request.Enabled,
                Url = request.Url.Trim(),
                Method = request.Method.ToUpperInvariant(),
                ContentType = request.ContentType.Trim(),
                BodyTemplate = request.BodyTemplate,
                Headers = request.Headers is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase),
                AuthHeader = KeepOrReplaceSecret(current.AuthHeader, request.AuthHeader),
                From = Blank(request.From),
                TimeoutSeconds = request.TimeoutSeconds,
            },
            ct);

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSecurityAsync(
        SecuritySettingsDto request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveProtectionAsync(
        ProtectionSettingsDto request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        await settings.SaveAsync(
            AppSettingSections.Protection,
            new ProtectionSettings { RevealProtectedAssociations = request.RevealProtectedAssociations },
            ct);

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    /// <summary>
    /// Saves the retention window. It was a deployment key alone, and stays readable as one: an
    /// installation that never opens this page keeps whatever its environment says, and one that
    /// saves here stops having to think about the environment for this value.
    /// </summary>
    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveNotificationsAsync(
        NotificationSettingsDto request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        await settings.SaveAsync(
            AppSettingSections.Notifications,
            new NotificationSettings { RetentionDays = request.RetentionDays },
            ct);

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveAnnouncementsAsync(
        AnnouncementSettingsDto request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        await settings.SaveAsync(
            AppSettingSections.Announcements,
            new AnnouncementSettings
            {
                PaidChannelsEnabled = request.PaidChannelsEnabled,
                DailyPaidMessageCap = request.DailyPaidMessageCap,
            },
            ct);

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveImportAsync(
        ImportSettingsDto request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveInterfaceAsync(
        InterfaceSettingsDto request,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
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

        await settings.SaveAsync(
            AppSettingSections.Interface,
            new InterfaceSettings { PanelDefaults = request.PanelDefaults },
            ct);

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
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

    private static async Task<AdminSettingsDto> SnapshotAsync(
        IAppSettingsService settings, IEmailDelivery emailDelivery, ISmsDelivery smsDelivery, CancellationToken ct)
    {
        var mail = await settings.GetMailAsync(ct);
        var sms = await settings.GetSmsAsync(ct);
        var security = await settings.GetSecurityAsync(ct);
        var disclosure = await settings.GetProtectionAsync(ct);
        var import = await settings.GetImportAsync(ct);
        var ui = await settings.GetInterfaceAsync(ct);
        var notifications = await settings.GetNotificationsAsync(ct);
        var announcements = await settings.GetAnnouncementsAsync(ct);

        return new AdminSettingsDto(
            new MailSettingsDto(
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
                mail.AcceptInvalidCertificate),
            new SmsSettingsDto(
                sms.Enabled,
                sms.Url,
                sms.Method,
                sms.ContentType,
                sms.BodyTemplate,
                sms.Headers,
                !string.IsNullOrEmpty(sms.AuthHeader),
                sms.From,
                sms.TimeoutSeconds),
            new SecuritySettingsDto(
                security.RequireConfirmedEmail,
                security.SendConfirmationOnRegistration,
                security.AuthenticatorTwoFactorEnabled,
                security.EmailTwoFactorEnabled,
                security.SmsTwoFactorEnabled,
                security.TwoFactorCodeLifetimeMinutes,
                security.TwoFactorResendIntervalSeconds),
            new ProtectionSettingsDto(disclosure.RevealProtectedAssociations),
            new ImportSettingsDto(
                import.AllowCreateWithoutReview,
                import.DuplicateRadiusMeters,
                import.DuplicateNameSimilarity,
                import.PhotoProximityRadiusMeters,
                import.PhotoClusterRadiusMeters),
            new InterfaceSettingsDto(ui.PanelDefaults),
            new NotificationSettingsDto(notifications.EffectiveRetentionDays),
            new AnnouncementSettingsDto(
                announcements.PaidChannelsEnabled, announcements.EffectiveDailyPaidMessageCap),
            await emailDelivery.IsConfiguredAsync(ct),
            await smsDelivery.IsConfiguredAsync(ct));
    }

    /// <summary>
    /// Null leaves a stored secret alone — the page never received it, so it cannot send it back.
    /// An empty string is an explicit "there is no secret any more".
    /// </summary>
    private static string? KeepOrReplaceSecret(string? stored, string? supplied) =>
        supplied is null ? stored : Blank(supplied);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
