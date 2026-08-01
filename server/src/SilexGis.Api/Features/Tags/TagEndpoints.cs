// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Tags;

public sealed record TagDto(long Id, string Name, string Slug);

/// <summary>
/// <see cref="EntityType"/>/<see cref="EntityId"/> name the tagged target: "feature" +
/// a feature id, or a non-feature entity name + its id.
/// </summary>
public sealed record TaggingDto(long Id, TagDto Tag, string EntityType, Guid EntityId);

public sealed record TaggingCreateRequest(string TagName, string EntityType, Guid EntityId);

public sealed class TaggingCreateRequestValidator : AbstractValidator<TaggingCreateRequest>
{
    public TaggingCreateRequestValidator()
    {
        RuleFor(x => x.TagName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.EntityType)
            .Must(value => AttachmentTargets.TryParse(value, out _))
            .WithMessage("Unknown entity type.");
        RuleFor(x => x.TagName)
            .Must(name => Tag.Slugify(name).Length > 0)
            .WithMessage("The tag name must contain letters or digits.");
    }
}

public static class TagEndpoints
{
    public static RouteGroupBuilder MapTagEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tags", ListTagsAsync)
            .WithTags("Tags")
            .WithSummary("Tag catalog with optional name search.");

        var taggings = api.MapGroup("/taggings").WithTags("Tags");
        taggings.MapGet("/", ListTaggingsAsync)
            .WithSummary("Tags of one target; requires Read on the target.");
        taggings.MapPost("/", CreateAsync).WithValidation<TaggingCreateRequest>()
            .WithSummary("Tags a target, creating the tag if new; requires Write on the target. Idempotent.");
        taggings.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Removes a tag from a target; requires Write on the target.");

        return api;
    }

    private static async Task<Results<Ok<List<TagDto>>, UnauthorizedHttpResult>> ListTagsAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        string? search,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Tags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Name), EF.Functions.Unaccent(pattern)));
        }

        var tags = await query.OrderBy(x => x.Name).Take(100)
            .Select(x => new TagDto(x.Id, x.Name, x.Slug))
            .ToListAsync(ct);
        return TypedResults.Ok(tags);
    }

    private static async Task<Results<Ok<List<TaggingDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListTaggingsAsync(
        string entityType,
        Guid entityId,
        SilexGisDbContext db,
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
            return ApiProblems.BadRequest("tagging.entity_type_unknown", $"Unknown entity type '{entityType}'.");
        }

        if (!await FileAccessRules.CanReadTargetAsync(db, user, new AttachmentTarget(parsedType, entityId), ct))
        {
            return ApiProblems.NotFound("tagging.entity_not_found");
        }

        var scoped = parsedType is { } pairType
            ? db.Taggings.AsNoTracking().Where(x => x.EntityType == pairType && x.EntityId == entityId)
            : db.Taggings.AsNoTracking().Where(x => x.FeatureId == entityId);

        var rows = await scoped
            .Join(db.Tags.AsNoTracking(), x => x.TagId, t => t.Id, (x, t) => new { x, t })
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(r => r.x.ToDto(new TagDto(r.t.Id, r.t.Name, r.t.Slug))).ToList());
    }

    private static async Task<Results<Created<TaggingDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TaggingCreateRequest request,
        SilexGisDbContext db,
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
            return ApiProblems.BadRequest("tagging.entity_type_unknown", $"Unknown entity type '{request.EntityType}'.");
        }

        var target = new AttachmentTarget(parsedType, request.EntityId);
        if (!await FileAccessRules.CanWriteTargetAsync(db, user, target, ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, user, target, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("tagging.entity_not_found");
        }

        var name = request.TagName.Trim();
        var slug = Tag.Slugify(name);
        var tag = await db.Tags.FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (tag is null)
        {
            tag = new Tag { Name = name, Slug = slug };
            db.Tags.Add(tag);
            // Materialize the identity now — the tagging row references it by value.
            await db.SaveChangesAsync(ct);
        }

        var tagId = tag.Id;
        var existing = parsedType is { } pairType
            ? await db.Taggings.AsNoTracking().FirstOrDefaultAsync(
                x => x.TagId == tagId && x.EntityType == pairType && x.EntityId == request.EntityId, ct)
            : await db.Taggings.AsNoTracking().FirstOrDefaultAsync(
                x => x.TagId == tagId && x.FeatureId == request.EntityId, ct);
        if (existing is not null)
        {
            // Idempotent: tagging twice is a no-op.
            return TypedResults.Created(
                $"/api/v1/taggings/{existing.Id}",
                existing.ToDto(new TagDto(tag.Id, tag.Name, tag.Slug)));
        }

        var tagging = new Tagging
        {
            TagId = tag.Id,
            FeatureId = parsedType is null ? request.EntityId : null,
            EntityType = parsedType,
            EntityId = parsedType is null ? null : request.EntityId,
            AddedBy = user.UserId,
        };
        db.Taggings.Add(tagging);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/taggings/{tagging.Id}",
            tagging.ToDto(new TagDto(tag.Id, tag.Name, tag.Slug)));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        long id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var tagging = await db.Taggings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tagging is null)
        {
            return ApiProblems.NotFound("tagging.not_found");
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, user, TargetOf(tagging), ct))
        {
            return ApiProblems.NotFound("tagging.not_found");
        }

        db.Taggings.Remove(tagging);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // The row's XOR maps directly: a feature row has a null EntityType, which is exactly
    // the parsed shape of a feature target.
    private static AttachmentTarget TargetOf(Tagging x) => new(x.EntityType, x.FeatureId ?? x.EntityId!.Value);

    private static TaggingDto ToDto(this Tagging x, TagDto tag) => new(
        x.Id,
        tag,
        AttachmentTargets.NameOf(x.FeatureId, x.EntityType),
        x.FeatureId ?? x.EntityId!.Value);
}
