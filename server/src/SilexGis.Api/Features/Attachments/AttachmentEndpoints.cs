// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Files;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Attachments;

/// <summary>
/// <see cref="EntityType"/>/<see cref="EntityId"/> name the attachment's target:
/// "feature" + a feature id, or a non-feature entity name + its id.
/// </summary>
public sealed record AttachmentDto(
    Guid Id,
    Guid FileId,
    string EntityType,
    Guid EntityId,
    AttachmentRole Role,
    string? Caption,
    int SortOrder,
    Guid? AddedBy,
    FileDto File);

public sealed record AttachmentCreateRequest(
    Guid FileId,
    string EntityType,
    Guid EntityId,
    AttachmentRole Role,
    string? Caption,
    int SortOrder);

public sealed class AttachmentCreateRequestValidator : AbstractValidator<AttachmentCreateRequest>
{
    public AttachmentCreateRequestValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x.EntityType)
            .Must(value => AttachmentTargets.TryParse(value, out _))
            .WithMessage("Unknown entity type.");
        // A file is never an attachment target (it is only a tag target). Allowing it would let a
        // file be attached to a file, and the polymorphic access resolver would then recurse
        // file → attachment → file without bound. Tags travel a separate table, so are unaffected.
        RuleFor(x => x.EntityType)
            .Must(value => !AttachmentTargets.TryParse(value, out var target) || target != AttachedEntityType.StoredFile)
            .WithMessage("Files cannot be attachment targets.");
        RuleFor(x => x.Role).IsInEnum();
    }
}

/// <summary>Editable attachment metadata; the file and its target are fixed at creation.</summary>
public sealed record AttachmentUpdateRequest(AttachmentRole Role, string? Caption, int SortOrder);

public sealed class AttachmentUpdateRequestValidator : AbstractValidator<AttachmentUpdateRequest>
{
    public AttachmentUpdateRequestValidator()
    {
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public static class AttachmentEndpoints
{
    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        var attachments = api.MapGroup("/attachments").WithTags("Attachments");

        attachments.MapGet("/", ListAsync)
            .WithSummary("Attachments of one target, ordered; requires Read on the target.");
        attachments.MapPost("/", CreateAsync).WithValidation<AttachmentCreateRequest>()
            .WithSummary("Attaches an uploaded file to a target; requires Write on the target.");
        attachments.MapPut("/{id:guid}", UpdateAsync).WithValidation<AttachmentUpdateRequest>()
            .WithSummary("Edits an attachment's role/caption/order; requires Write on the target.");
        attachments.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Detaches a file (the file itself is kept); requires Write on the target.");

        return api;
    }

    private static async Task<Results<Ok<List<AttachmentDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        string entityType,
        Guid entityId,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AttachmentTargets.TryParse(entityType, out var parsedType))
        {
            return ApiProblems.BadRequest("attachment.entity_type_unknown", $"Unknown entity type '{entityType}'.");
        }

        if (!await FileAccessRules.CanReadTargetAsync(db, user, new AttachmentTarget(parsedType, entityId), ct))
        {
            // The target is invisible to the caller — so are its attachments.
            return ApiProblems.NotFound("attachment.entity_not_found");
        }

        var scoped = parsedType is { } pairType
            ? db.Attachments.AsNoTracking().Where(a => a.EntityType == pairType && a.EntityId == entityId)
            : db.Attachments.AsNoTracking().Where(a => a.FeatureId == entityId);

        var rows = await scoped
            .Join(db.StoredFiles.AsNoTracking(), a => a.FileId, f => f.Id, (a, f) => new { a, f })
            .OrderBy(x => x.a.SortOrder).ThenBy(x => x.a.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(x => x.a.ToDto(x.f, tokens)).ToList());
    }

    private static async Task<Results<Created<AttachmentDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        AttachmentCreateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AttachmentTargets.TryParse(request.EntityType, out var parsedType))
        {
            return ApiProblems.BadRequest("attachment.entity_type_unknown", $"Unknown entity type '{request.EntityType}'.");
        }

        var target = new AttachmentTarget(parsedType, request.EntityId);
        if (!await FileAccessRules.CanWriteTargetAsync(db, user, target, ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, user, target, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("attachment.entity_not_found");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FileId, ct);
        if (file is null || !await FileAccessRules.CanAccessAsync(db, user, file, ct))
        {
            return ApiProblems.BadRequest("attachment.file_not_found", "The file does not exist.");
        }

        var attachment = new Attachment
        {
            FileId = request.FileId,
            FeatureId = parsedType is null ? request.EntityId : null,
            EntityType = parsedType,
            EntityId = parsedType is null ? null : request.EntityId,
            Role = request.Role,
            Caption = request.Caption,
            SortOrder = request.SortOrder,
            AddedBy = user.UserId,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/attachments/{attachment.Id}", attachment.ToDto(file, tokens));
    }

    private static async Task<Results<Ok<AttachmentDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        AttachmentUpdateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return ApiProblems.NotFound("attachment.not_found");
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, user, TargetOf(attachment), ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, user, TargetOf(attachment), ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("attachment.not_found");
        }

        attachment.Role = request.Role;
        attachment.Caption = request.Caption;
        attachment.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);

        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == attachment.FileId, ct);
        return TypedResults.Ok(attachment.ToDto(file, tokens));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return ApiProblems.NotFound("attachment.not_found");
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, user, TargetOf(attachment), ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, user, TargetOf(attachment), ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("attachment.not_found");
        }

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // The row's XOR maps directly: a feature row has a null EntityType, which is exactly
    // the parsed shape of a feature target.
    private static AttachmentTarget TargetOf(Attachment a) => new(a.EntityType, a.FeatureId ?? a.EntityId!.Value);

    private static AttachmentDto ToDto(this Attachment a, StoredFile file, IFileAccessTokenService tokens) => new(
        a.Id,
        a.FileId,
        AttachmentTargets.NameOf(a.FeatureId, a.EntityType),
        a.FeatureId ?? a.EntityId!.Value,
        a.Role,
        a.Caption,
        a.SortOrder,
        a.AddedBy,
        file.ToDto(tokens));
}
