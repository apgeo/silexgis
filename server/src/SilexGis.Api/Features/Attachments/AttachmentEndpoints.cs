// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Api.Features.Files;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
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
        IAccessService access,
        AssociationDisclosure associations,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AttachmentTargets.TryParse(entityType, out var parsedType))
        {
            return ApiProblems.BadRequest("attachment.entity_type_unknown", $"Unknown entity type '{entityType}'.");
        }

        if (!await FileAccessRules.CanReadTargetAsync(db, access, ctx, new AttachmentTarget(parsedType, entityId), ct))
        {
            // The target is invisible to the caller — so are its attachments.
            return ApiProblems.NotFound("attachment.entity_not_found");
        }

        var scoped = parsedType is { } pairType
            ? db.Attachments.AsNoTracking().Where(a => a.EntityType == pairType && a.EntityId == entityId)
            : db.Attachments.AsNoTracking().Where(a => a.FeatureId == entityId);

        var rows = await (from attachment in scoped
                          join file in db.StoredFiles.AsNoTracking() on attachment.FileId equals file.Id
                          join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                          join document in db.Documents.AsNoTracking() on version.DocumentId equals document.Id
                          orderby attachment.SortOrder, attachment.CreatedAt
                          select new
                          {
                              Attachment = attachment,
                              Content = new DocumentFile(file, version),
                              Document = document,
                              HasOwnPosition = file.Geom != null,
                          })
            .ToListAsync(ct);

        // Being able to read what a document hangs on is how most documents are reached, but
        // it is not the only thing that decides them: a rule written against the document
        // itself is consulted first and a deny among them is final, so a listing that showed
        // everything attached here would hand out exactly what such a deny was written to
        // stop. Answered without a further query — reach is not in doubt for these rows,
        // because the target they name is the one this caller was just authorised for.
        rows = [.. rows.Where(x =>
            DocumentAccessRules.AllowedByOwnRulesOrAttachment(ctx, x.Document, AccessAction.Read))];

        // The documents themselves are none of this rule's business — what a caller may read is
        // decided by the rules written about each one. Withheld here is only the pairing: a
        // caller who may not place this feature exactly is not told which documents point at it.
        var withheld = await associations.WithheldIdsAsync(
            ctx,
            [.. rows.Select(x => new AssociationCandidate(
                x.Attachment.Id,
                new FeatureAssociation(x.Attachment.FeatureId, x.HasOwnPosition)))],
            ct);

        var shown = rows.Where(x => !withheld.Contains(x.Attachment.Id)).ToList();

        // Asked separately from the withholding above, and it has to be: that answer is per
        // row, and a photo hanging on two features — one open, one guarded — has a row that
        // survives. The bytes do not care which row was followed to reach them, so the right
        // to have them is decided over everything the photo hangs on at once.
        var mayHaveOriginals = await photos.DisclosableIdsAsync(
            ctx, [.. shown.Select(x => x.Attachment.FileId)], ct);

        return TypedResults.Ok(shown
            .Select(x => x.Attachment.ToDto(x.Content, tokens, mayHaveOriginals.Contains(x.Attachment.FileId)))
            .ToList());
    }

    private static async Task<Results<Created<AttachmentDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        AttachmentCreateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AttachmentTargets.TryParse(request.EntityType, out var parsedType))
        {
            return ApiProblems.BadRequest("attachment.entity_type_unknown", $"Unknown entity type '{request.EntityType}'.");
        }

        var target = new AttachmentTarget(parsedType, request.EntityId);
        if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, target, ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, access, ctx, target, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("attachment.entity_not_found");
        }

        // Attaching hands the document to everyone who can read the target, so the caller
        // has to be able to read it themselves — asked through the document's own walk, or a
        // rule written against the document would not reach the one act that republishes it.
        var subject = await DocumentQueries.FileSubjectAsync(db, request.FileId, ct);
        if (subject is null || !await DocumentAccessRules.CanReadFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.BadRequest("attachment.file_not_found", "The file does not exist.");
        }

        var content = new DocumentFile(subject.File, subject.Version);

        var attachment = new Attachment
        {
            FileId = request.FileId,
            FeatureId = parsedType is null ? request.EntityId : null,
            EntityType = parsedType,
            EntityId = parsedType is null ? null : request.EntityId,
            Role = request.Role,
            Caption = request.Caption,
            SortOrder = request.SortOrder,
            AddedBy = ctx.UserId,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        // Attaching a photo to a cave is what can make its capture point sensitive, so the
        // answer is resolved after the row exists rather than before.
        var mayHaveOriginal = (await photos.DisclosableIdsAsync(ctx, [attachment.FileId], ct))
            .Contains(attachment.FileId);
        return TypedResults.Created(
            $"/api/v1/attachments/{attachment.Id}", attachment.ToDto(content, tokens, mayHaveOriginal));
    }

    private static async Task<Results<Ok<AttachmentDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        AttachmentUpdateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return ApiProblems.NotFound("attachment.not_found");
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, TargetOf(attachment), ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, access, ctx, TargetOf(attachment), ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("attachment.not_found");
        }

        // This response carries a delivery URL for the attached document, so the document's
        // own rules decide it as they decide the listing this row would appear in — a row the
        // listing withholds is answered here the way the listing answers it, as absent.
        var subject = await DocumentQueries.FileSubjectAsync(db, attachment.FileId, ct);
        if (subject is null || !await DocumentAccessRules.CanReadFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.NotFound("attachment.not_found");
        }

        attachment.Role = request.Role;
        attachment.Caption = request.Caption;
        attachment.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);

        var mayHaveOriginal = (await photos.DisclosableIdsAsync(ctx, [attachment.FileId], ct))
            .Contains(attachment.FileId);
        return TypedResults.Ok(
            attachment.ToDto(new DocumentFile(subject.File, subject.Version), tokens, mayHaveOriginal));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return ApiProblems.NotFound("attachment.not_found");
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, TargetOf(attachment), ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, access, ctx, TargetOf(attachment), ct)
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

    private static AttachmentDto ToDto(
        this Attachment a, DocumentFile content, IFileAccessTokenService tokens, bool mayHaveOriginal) => new(
        a.Id,
        a.FileId,
        AttachmentTargets.NameOf(a.FeatureId, a.EntityType),
        a.FeatureId ?? a.EntityId!.Value,
        a.Role,
        a.Caption,
        a.SortOrder,
        a.AddedBy,
        content.File.ToDto(content.Version, tokens, mayHaveOriginal));
}
