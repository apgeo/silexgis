// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;

namespace SilexGis.Api.Features.Admin;

/// <summary>
/// The administrator's view of mail, SMS and sign-in policy.
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

        admin.MapGet("/", GetAsync).WithSummary("Mail, SMS and sign-in policy, with secrets redacted.");
        admin.MapPut("/mail", SaveMailAsync)
            .WithValidation<MailSettingsWriteRequest>()
            .WithSummary("Saves the mail server settings.");
        admin.MapPut("/sms", SaveSmsAsync)
            .WithValidation<SmsSettingsWriteRequest>()
            .WithSummary("Saves the SMS gateway settings.");
        admin.MapPut("/security", SaveSecurityAsync)
            .WithValidation<SecuritySettingsDto>()
            .WithSummary("Saves the sign-in policy: address confirmation and which second factors are allowed.");
        admin.MapPost("/mail/test", TestMailAsync)
            .WithValidation<TestMessageRequest>()
            .WithSummary("Sends a test message to prove the mail server works.");
        admin.MapPost("/sms/test", TestSmsAsync)
            .WithValidation<TestMessageRequest>()
            .WithSummary("Sends a test text to prove the gateway works.");

        return api;
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        IUserContextAccessor userAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var caller = await userAccessor.GetAsync(ct);
        if (caller is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!caller.IsAdmin)
        {
            return ApiProblems.Forbidden("admin.requires_admin");
        }

        return TypedResults.Ok(await SnapshotAsync(settings, emailDelivery, smsDelivery, ct));
    }

    private static async Task<Results<Ok<AdminSettingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveMailAsync(
        MailSettingsWriteRequest request,
        IUserContextAccessor userAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var caller = await userAccessor.GetAsync(ct);
        if (caller is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!caller.IsAdmin)
        {
            return ApiProblems.Forbidden("admin.requires_admin");
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
        IUserContextAccessor userAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var caller = await userAccessor.GetAsync(ct);
        if (caller is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!caller.IsAdmin)
        {
            return ApiProblems.Forbidden("admin.requires_admin");
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
        IUserContextAccessor userAccessor,
        IAppSettingsService settings,
        IEmailDelivery emailDelivery,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var caller = await userAccessor.GetAsync(ct);
        if (caller is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!caller.IsAdmin)
        {
            return ApiProblems.Forbidden("admin.requires_admin");
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

    /// <summary>
    /// Sends a plain diagnostic message rather than a catalogued one: this is proving the transport
    /// works, and routing it through a template the operator may have just broken would confuse the
    /// two questions.
    /// </summary>
    private static async Task<Results<Ok<TestMessageResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> TestMailAsync(
        TestMessageRequest request,
        IUserContextAccessor userAccessor,
        IEmailSender emailSender,
        IEmailDelivery emailDelivery,
        CancellationToken ct)
    {
        var caller = await userAccessor.GetAsync(ct);
        if (caller is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!caller.IsAdmin)
        {
            return ApiProblems.Forbidden("admin.requires_admin");
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
        IUserContextAccessor userAccessor,
        ISmsSender smsSender,
        ISmsDelivery smsDelivery,
        CancellationToken ct)
    {
        var caller = await userAccessor.GetAsync(ct);
        if (caller is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!caller.IsAdmin)
        {
            return ApiProblems.Forbidden("admin.requires_admin");
        }

        if (!await smsDelivery.IsConfiguredAsync(ct))
        {
            return ApiProblems.BadRequest("admin.sms_not_configured", "No SMS gateway is configured.");
        }

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
