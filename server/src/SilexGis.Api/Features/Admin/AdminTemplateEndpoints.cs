// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Messaging;

namespace SilexGis.Api.Features.Admin;

/// <summary>One language of one message: what is in force, and what shipped with the product.</summary>
/// <param name="Customised">
/// Whether an operator has rewritten this one. False means the text below is the shipped wording
/// and will follow the product if it improves.
/// </param>
public sealed record MessageTemplateLocaleDto(
    string Locale,
    string? Subject,
    string Body,
    string? DefaultSubject,
    string DefaultBody,
    bool Customised);

public sealed record MessageTemplateDto(
    string Key,
    MessageChannel Channel,
    string Description,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<MessageTemplateLocaleDto> Locales);

public sealed record MessageTemplateWriteRequest(string? Subject, string Body);

public sealed class MessageTemplateWriteRequestValidator : AbstractValidator<MessageTemplateWriteRequest>
{
    public MessageTemplateWriteRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Subject).MaximumLength(300);
    }
}

/// <summary>
/// Editing the wording of the messages the application sends.
/// </summary>
/// <remarks>
/// Governed by the MessageTemplates domain: listing needs Read, rewriting or resetting Write.
/// The list of messages is fixed — one exists only because some code path sends it — but every
/// one of them can be rewritten per language. Saving is refused when the text uses a placeholder
/// the message will never be given a value for, because that placeholder would render as nothing
/// and the mistake would only surface as a user unable to find the code they were promised.
/// </remarks>
public static class AdminTemplateEndpoints
{
    public static RouteGroupBuilder MapAdminTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/admin/message-templates").WithTags("Admin");

        templates.MapGet("/", ListAsync)
            .WithSummary("Every message the application sends, in every language, with the shipped wording alongside.");
        templates.MapPut("/{key}/{locale}", SaveAsync)
            .WithValidation<MessageTemplateWriteRequest>()
            .WithSummary("Rewrites one message in one language.");
        templates.MapDelete("/{key}/{locale}", ResetAsync)
            .WithSummary("Drops a rewrite so the shipped wording applies again.");

        return api;
    }

    private static async Task<Results<Ok<List<MessageTemplateDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        IAccessContextAccessor accessAccessor,
        MessageTemplateStore store,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.MessageTemplates, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var overrides = (await store.AllOverridesAsync(ct))
            .ToDictionary(row => (row.Key, row.Locale), row => row);

        var result = MessageTemplateCatalog.All
            .Select(definition => new MessageTemplateDto(
                definition.Key,
                definition.Channel,
                definition.Description,
                definition.Placeholders,
                [.. MessageTemplateCatalog.Locales.Select(locale =>
                {
                    var shipped = MessageTemplateCatalog.Default(definition, locale);
                    var customised = overrides.TryGetValue((definition.Key, locale), out var row);
                    return new MessageTemplateLocaleDto(
                        locale,
                        customised ? row!.Subject : shipped.Subject,
                        customised ? row!.Body : shipped.Body,
                        shipped.Subject,
                        shipped.Body,
                        customised);
                })]))
            .ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> SaveAsync(
        string key,
        string locale,
        MessageTemplateWriteRequest request,
        IAccessContextAccessor accessAccessor,
        MessageTemplateStore store,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.MessageTemplates, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        if (MessageTemplateCatalog.Find(key) is not { } definition)
        {
            return ApiProblems.NotFound("admin.template_unknown");
        }

        if (!MessageTemplateCatalog.Locales.Contains(locale, StringComparer.OrdinalIgnoreCase))
        {
            return ApiProblems.NotFound("admin.template_locale_unknown");
        }

        // SMS has no subject; accepting one would store a field nothing will ever read.
        var subject = definition.Channel == MessageChannel.Email ? request.Subject : null;
        if (definition.Channel == MessageChannel.Email && string.IsNullOrWhiteSpace(subject))
        {
            return ApiProblems.BadRequest("admin.template_subject_required", "An email needs a subject.");
        }

        var unknown = MessageTemplateRenderer.UnknownPlaceholders(definition, subject, request.Body);
        if (unknown.Count > 0)
        {
            return ApiProblems.BadRequest(
                "admin.template_placeholder_unknown",
                $"This message is never given: {string.Join(", ", unknown.Select(name => $"{{{name}}}"))}.");
        }

        await store.SaveAsync(definition, locale, subject, request.Body, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> ResetAsync(
        string key,
        string locale,
        IAccessContextAccessor accessAccessor,
        MessageTemplateStore store,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.MessageTemplates, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        if (MessageTemplateCatalog.Find(key) is null)
        {
            return ApiProblems.NotFound("admin.template_unknown");
        }

        return await store.ResetAsync(key, locale, ct)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("admin.template_not_customised");
    }
}
