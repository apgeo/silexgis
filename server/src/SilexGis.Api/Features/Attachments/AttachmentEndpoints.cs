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

public sealed record AttachmentDto(
    Guid Id,
    Guid FileId,
    AttachedEntityType EntityType,
    Guid EntityId,
    AttachmentRole Role,
    string? Caption,
    int SortOrder,
    Guid? AddedBy,
    FileDto File);

public sealed record AttachmentCreateRequest(
    Guid FileId,
    AttachedEntityType EntityType,
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
        RuleFor(x => x.EntityType).IsInEnum();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public static class AttachmentEndpoints
{
    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        var attachments = api.MapGroup("/attachments").WithTags("Attachments");

        attachments.MapGet("/", ListAsync)
            .WithSummary("Attachments of one entity, ordered; requires Read on the entity.");
        attachments.MapPost("/", CreateAsync).WithValidation<AttachmentCreateRequest>()
            .WithSummary("Attaches an uploaded file to an entity; requires Write on the entity.");
        attachments.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Detaches a file (the file itself is kept); requires Write on the entity.");

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

        // Query-string enum binding is case-sensitive; the JSON convention is camelCase —
        // parse leniently so the wire format matches the body enums.
        if (!Enum.TryParse<AttachedEntityType>(entityType, ignoreCase: true, out var parsedType))
        {
            return ApiProblems.BadRequest("attachment.entity_type_unknown", $"Unknown entity type '{entityType}'.");
        }

        if (!await FileAccessRules.CanReadEntityAsync(db, user, parsedType, entityId, ct))
        {
            // The entity is invisible to the caller — so are its attachments.
            return ApiProblems.NotFound("attachment.entity_not_found");
        }

        var rows = await db.Attachments.AsNoTracking()
            .Where(a => a.EntityType == parsedType && a.EntityId == entityId)
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

        if (!await FileAccessRules.CanWriteEntityAsync(db, user, request.EntityType, request.EntityId, ct))
        {
            return await FileAccessRules.CanReadEntityAsync(db, user, request.EntityType, request.EntityId, ct)
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
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Role = request.Role,
            Caption = request.Caption,
            SortOrder = request.SortOrder,
            AddedBy = user.UserId,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/attachments/{attachment.Id}", attachment.ToDto(file, tokens));
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

        if (!await FileAccessRules.CanWriteEntityAsync(db, user, attachment.EntityType, attachment.EntityId, ct))
        {
            return await FileAccessRules.CanReadEntityAsync(db, user, attachment.EntityType, attachment.EntityId, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("attachment.not_found");
        }

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static AttachmentDto ToDto(this Attachment a, StoredFile file, IFileAccessTokenService tokens) => new(
        a.Id,
        a.FileId,
        a.EntityType,
        a.EntityId,
        a.Role,
        a.Caption,
        a.SortOrder,
        a.AddedBy,
        file.ToDto(tokens));
}
