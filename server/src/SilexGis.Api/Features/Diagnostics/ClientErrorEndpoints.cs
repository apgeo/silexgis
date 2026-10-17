// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SilexGis.Api.Common;
using SilexGis.Domain;

namespace SilexGis.Api.Features.Diagnostics;

/// <summary>One error a browser saw, as the application's own reporter records it.</summary>
public sealed record ClientErrorReport(
    string Fingerprint,
    string Kind,
    string? Name,
    string Message,
    string? Frame,
    string? Stack,
    string? PageUrl,
    string? At,
    IReadOnlyList<string>? Breadcrumbs,
    string? ComponentStack,
    string? UserAgent,
    string? Viewport);

/// <summary>The errors a browser collected over a moment, sent together.</summary>
public sealed record ClientErrorBatch(IReadOnlyList<ClientErrorReport> Errors);

public sealed class ClientErrorBatchValidator : AbstractValidator<ClientErrorBatch>
{
    /// <summary>The most a browser sends at once, the same cap the reporter keeps.</summary>
    public const int MaxErrors = 50;

    public ClientErrorBatchValidator()
    {
        RuleFor(x => x.Errors).NotEmpty().Must(e => e.Count <= MaxErrors)
            .WithMessage($"At most {MaxErrors} errors are taken at once.");
        RuleForEach(x => x.Errors).ChildRules(error =>
        {
            error.RuleFor(e => e.Fingerprint).NotEmpty().MaximumLength(64);
            error.RuleFor(e => e.Kind).NotEmpty().Must(k => k is "uncaught" or "console")
                .WithMessage("The kind is uncaught or console.");
            error.RuleFor(e => e.Name).MaximumLength(200);
            error.RuleFor(e => e.Message).NotEmpty().MaximumLength(2_000);
            error.RuleFor(e => e.Frame).MaximumLength(500);
            error.RuleFor(e => e.Stack).MaximumLength(8_000);
            error.RuleFor(e => e.PageUrl).MaximumLength(2_000);
            error.RuleFor(e => e.At).MaximumLength(40);
            error.RuleFor(e => e.Breadcrumbs).Must(b => b is null || b.Count <= 30)
                .WithMessage("At most 30 breadcrumbs.");
            error.RuleForEach(e => e.Breadcrumbs).MaximumLength(300);
            error.RuleFor(e => e.ComponentStack).MaximumLength(8_000);
            error.RuleFor(e => e.UserAgent).MaximumLength(500);
            error.RuleFor(e => e.Viewport).MaximumLength(20);
        });
    }
}

/// <summary>Where a browser's errors land in the server's log, so they read as one source.</summary>
public sealed class ClientErrorLog;

/// <summary>
/// Errors a signed-in person's browser saw, written into this installation's own log.
/// <para>
/// In development the application reports to the development server, which writes a file; a packaged
/// installation has no development server, so without this route nobody using one could report
/// anything and nobody running one would ever hear of a fault in the browser. The reports go
/// nowhere else — no service, no account — and the log is the installation's own.
/// </para>
/// <para>
/// Only for a signed-in account, like every other route here: an address that takes writes from
/// anybody would be the one unauthenticated write path in the application. The caller's own
/// browser is the only thing reported on, so being signed in is the whole of the right. Bounded
/// three ways, because a page caught in a loop can report thousands of times a second: per account
/// per minute, per request body, and per member.
/// </para>
/// </summary>
public static class ClientErrorEndpoints
{
    /// <summary>The policy name, registered in the application's rate-limiter setup.</summary>
    public const string RateLimitPolicy = "client-errors";

    /// <summary>Reports a minute from one account: a few dozen faults, not a loop.</summary>
    public const int PerMinute = 30;

    public const string TooLargeCode = "client_errors.too_large";

    /// <summary>The largest body taken, well above a full batch of capped members.</summary>
    public const long MaxBodyBytes = 1024 * 1024;

    public static RouteGroupBuilder MapClientErrorEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/diagnostics/client-errors", ReportAsync)
            .WithTags("Diagnostics")
            // Kestrel enforces the limit as the body streams in; a declared length past it is
            // answered first, before the validator, so the refusal says what was wrong with it.
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.Request.ContentLength > MaxBodyBytes
                    ? TypedResults.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "Payload too large",
                        detail: "The report is larger than any batch of errors.",
                        extensions: new Dictionary<string, object?> { ["code"] = TooLargeCode })
                    : await next(context))
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithValidation<ClientErrorBatch>()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Writes errors the caller's browser saw into this installation's log.");

        return api;
    }

    private static Results<NoContent, UnauthorizedHttpResult> ReportAsync(
        ClientErrorBatch batch,
        ICurrentUser currentUser,
        ILogger<ClientErrorLog> log)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        foreach (var error in batch.Errors)
        {
            // One warning per error with the identity in the message, so a log reader can count a
            // defect by its fingerprint; the stacks and the trail travel beside it as properties,
            // which a structured sink keeps and a plain console leaves out.
            using (log.BeginScope(new Dictionary<string, object?>
            {
                ["ClientStack"] = error.Stack,
                ["ClientComponentStack"] = error.ComponentStack,
                ["ClientBreadcrumbs"] = error.Breadcrumbs,
                ["ClientUserAgent"] = error.UserAgent,
                ["ClientViewport"] = error.Viewport,
                ["ClientAt"] = error.At,
            }))
            {
                log.LogWarning(
                    "Browser error {Fingerprint} ({Kind}) {Name}: {Message} at {Frame} on {Page} for user {UserId}",
                    error.Fingerprint, error.Kind, error.Name ?? "", error.Message, error.Frame ?? "",
                    PathOf(error.PageUrl), userId);
            }
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The page's path without its query: an address can carry a share token or a search somebody
    /// typed, and neither belongs in a log line written for counting faults.
    /// </summary>
    private static string PathOf(string? pageUrl) =>
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "";
}
