// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;

namespace SilexGis.Api.Features.History;

/// <summary>
/// Put fields of an entity back to the value they had before one change of its history.
/// </summary>
/// <param name="EntityType">The kind of entity; "feature" (caves, entrances and every other feature) is the only one.</param>
/// <param name="EntityId">The entity the change is about — the history event's own <c>entityId</c>.</param>
/// <param name="AuditEntryId">The history event (an edit) whose earlier values are wanted back.</param>
/// <param name="Fields">The fields to put back, as the event names them; every restorable field of the change when omitted.</param>
public sealed record HistoryRestoreRequest(
    string EntityType,
    Guid EntityId,
    long AuditEntryId,
    string[]? Fields);

/// <summary>A field a restore left as it is.</summary>
/// <param name="Field">The field, as the history event names it.</param>
/// <param name="Reason">not_restorable | redacted | no_pre_image | forbidden.</param>
public sealed record HistoryRestoreSkippedDto(string Field, string Reason);

/// <summary>What a restore put back and what it left.</summary>
public sealed record HistoryRestoreResultDto(string[] Applied, HistoryRestoreSkippedDto[] Skipped);

public sealed class HistoryRestoreRequestValidator : AbstractValidator<HistoryRestoreRequest>
{
    public HistoryRestoreRequestValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty()
            .Must(t => string.Equals(t, FeatureAudit.RootName, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only a feature's history can be restored from.");
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.AuditEntryId).GreaterThan(0);
        RuleFor(x => x.Fields).Must(f => f is null || f.Length <= 100).WithMessage("At most 100 fields.");
        RuleForEach(x => x.Fields).NotEmpty().MaximumLength(100);
    }
}

internal static class HistoryRestoreEndpoint
{
    public static async Task<Results<Ok<HistoryRestoreResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> RestoreAsync(
        HistoryRestoreRequest request,
        IAccessContextAccessor accessAccessor,
        HistoryRestoreService restore,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var outcome = await restore.RestoreAsync(ctx, request.EntityId, request.AuditEntryId, request.Fields, ct);
        return outcome.Status switch
        {
            HistoryRestoreStatus.NotFound => ApiProblems.NotFound(outcome.Code!),
            HistoryRestoreStatus.Forbidden => ApiProblems.Forbidden(),
            HistoryRestoreStatus.Invalid => ApiProblems.BadRequest(outcome.Code!, outcome.Detail),
            _ => TypedResults.Ok(new HistoryRestoreResultDto(
                [.. outcome.Applied],
                [.. outcome.Skipped.Select(s => new HistoryRestoreSkippedDto(s.Field, s.Reason))])),
        };
    }
}
