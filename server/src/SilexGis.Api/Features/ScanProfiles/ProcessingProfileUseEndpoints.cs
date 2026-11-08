// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Persistence.Configurations;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Features.ScanProfiles;

/// <summary>A copy of a profile, changed or not, saved as a new profile of the caller's choosing.</summary>
/// <param name="Name">The copy's name; the original's with "(copy)" after it when not sent.</param>
/// <param name="Description">The copy's description; the original's when not sent.</param>
/// <param name="Scope">Who the copy is kept for; the caller alone when not sent.</param>
/// <param name="CavingGroupId">The group, for a group copy; absent for every other scope.</param>
/// <param name="Values">The copy's values; the original's when not sent. This is how a built-in is edited.</param>
public sealed record ProcessingProfileCopyRequest(
    string? Name = null,
    string? Description = null,
    ProcessingProfileScope Scope = ProcessingProfileScope.Personal,
    Guid? CavingGroupId = null,
    JsonElement? Values = null);

/// <summary>The profile a person used last for one step.</summary>
public sealed record ProcessingProfileChoiceDto(string StepId, Guid ProfileId, DateTimeOffset ChosenAt);

public sealed class ProcessingProfileCopyRequestValidator : AbstractValidator<ProcessingProfileCopyRequest>
{
    public ProcessingProfileCopyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProcessingProfileConfiguration.NameLength).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(ProcessingProfileConfiguration.DescriptionLength);
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.CavingGroupId).NotNull().When(x => x.Scope == ProcessingProfileScope.CavingGroup)
            .WithMessage("A group profile names its caving group.");
        RuleFor(x => x.CavingGroupId).Null().When(x => x.Scope != ProcessingProfileScope.CavingGroup)
            .WithMessage("Only a group profile names a caving group.");
        RuleFor(x => x.Values).Must(v => v!.Value.ValueKind == JsonValueKind.Object).When(x => x.Values is not null)
            .WithMessage("Values must be a JSON object.");
    }
}

/// <summary>
/// Using profiles: saving a copy of one — the only way a built-in is changed — and the profile
/// each step was run with last, which an upload offers again.
/// </summary>
public static class ProcessingProfileUseEndpoints
{
    /// <summary>The suffix a copy's name gets when the request names none.</summary>
    public const string CopySuffix = " (copy)";

    public static RouteGroupBuilder MapProcessingProfileUseEndpoints(this RouteGroupBuilder profiles)
    {
        profiles.MapPost("/{id:guid}/copy", CopyAsync).WithValidation<ProcessingProfileCopyRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Saves a copy of a profile the caller may read, with new values if sent — how a built-in is edited. A personal copy is anybody's; an installation or group copy needs the scan-profiles Create right.");
        profiles.MapGet("/remembered", RememberedAsync)
            .WithSummary("The profile the caller ran each step with last, where that profile is still one they may use.");
        return profiles;
    }

    private static async Task<Results<Created<ProcessingProfileDto>, UnauthorizedHttpResult, ProblemHttpResult>> CopyAsync(
        Guid id,
        ProcessingProfileCopyRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        ScanStepParameterSchemas schemas,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var source = await db.ProcessingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (source is null || !ProcessingProfileRules.MayRead(ctx, source))
        {
            return ApiProblems.NotFound(ProcessingProfileEndpoints.NotFoundCode);
        }

        if (!ProcessingProfileRules.MayManage(ctx, request.Scope, ctx.UserId, AccessAction.Create))
        {
            return ApiProblems.Forbidden(ProcessingProfileEndpoints.ForbiddenCode);
        }

        if (request.CavingGroupId is { } groupId && !await db.CavingGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return ApiProblems.BadRequest(ProcessingProfileEndpoints.GroupUnknownCode);
        }

        var (schema, refusal) = await ProcessingProfileEndpoints.SchemaForAsync(source.StepId, schemas, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        using var original = JsonDocument.Parse(source.Values);
        var values = request.Values ?? original.RootElement;
        if (ProcessingProfileEndpoints.Refuse(schemas, schema!, values) is { } invalid)
        {
            return invalid;
        }

        // A copy that keeps the values keeps where they came from and the version they were written
        // for — except a built-in's, since the copy is now somebody's own and may be changed.
        var changed = !JsonElement.DeepEquals(values, original.RootElement);
        var name = (request.Name ?? source.Name + CopySuffix).Trim();
        if (name.Length > ProcessingProfileConfiguration.NameLength)
        {
            name = name[..ProcessingProfileConfiguration.NameLength];
        }

        var description = request.Description ?? source.Description;
        var copy = new ProcessingProfile
        {
            StepId = source.StepId,
            ToolVersion = changed ? schema!.ToolVersion : source.ToolVersion ?? schema!.ToolVersion,
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Scope = request.Scope,
            OwnerUserId = ctx.UserId,
            CavingGroupId = request.Scope == ProcessingProfileScope.CavingGroup ? request.CavingGroupId : null,
            Values = values.GetRawText(),
            Origin = changed || ProcessingProfileOrigins.IsBuiltIn(source.Origin) ? ProcessingProfileOrigins.Edited : source.Origin,
        };
        db.ProcessingProfiles.Add(copy);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/scan-profiles/{copy.Id}", ProcessingProfileEndpoints.ToDto(ctx, copy));
    }

    private static async Task<Results<Ok<List<ProcessingProfileChoiceDto>>, UnauthorizedHttpResult>> RememberedAsync(
        string? stepId,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var userId = ctx.UserId;
        var choices = db.ScanProfileChoices.AsNoTracking().Where(c => c.UserId == userId);
        if (!string.IsNullOrEmpty(stepId))
        {
            choices = choices.Where(c => c.StepId == stepId);
        }

        var rows = await choices
            .Join(db.ProcessingProfiles.AsNoTracking(), c => c.ProfileId, p => p.Id, (c, p) => new { Choice = c, Profile = p })
            .OrderBy(x => x.Choice.StepId)
            .ToListAsync(ct);

        // A profile that has since moved out of the caller's reach — a group they left — is not
        // offered again, though the choice stays in case it comes back.
        return TypedResults.Ok(rows
            .Where(x => ProcessingProfileRules.MayRead(ctx, x.Profile))
            .Select(x => new ProcessingProfileChoiceDto(x.Choice.StepId, x.Choice.ProfileId, x.Choice.ChosenAt))
            .ToList());
    }
}
