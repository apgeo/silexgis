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

public sealed record TaggingDto(long Id, TagDto Tag, AttachedEntityType EntityType, Guid EntityId);

public sealed record TaggingCreateRequest(string TagName, AttachedEntityType EntityType, Guid EntityId);

public sealed class TaggingCreateRequestValidator : AbstractValidator<TaggingCreateRequest>
{
    public TaggingCreateRequestValidator()
    {
        RuleFor(x => x.TagName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.EntityType).IsInEnum();
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
            .WithSummary("Tags of one entity; requires Read on the entity.");
        taggings.MapPost("/", CreateAsync).WithValidation<TaggingCreateRequest>()
            .WithSummary("Tags an entity, creating the tag if new; requires Write on the entity. Idempotent.");
        taggings.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Removes a tag from an entity; requires Write on the entity.");

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

        if (!Enum.TryParse<AttachedEntityType>(entityType, ignoreCase: true, out var parsedType))
        {
            return ApiProblems.BadRequest("tagging.entity_type_unknown", $"Unknown entity type '{entityType}'.");
        }

        if (!await FileAccessRules.CanReadEntityAsync(db, user, parsedType, entityId, ct))
        {
            return ApiProblems.NotFound("tagging.entity_not_found");
        }

        var rows = await db.Taggings.AsNoTracking()
            .Where(x => x.EntityType == parsedType && x.EntityId == entityId)
            .Join(db.Tags.AsNoTracking(), x => x.TagId, t => t.Id,
                (x, t) => new TaggingDto(x.Id, new TagDto(t.Id, t.Name, t.Slug), x.EntityType, x.EntityId))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
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

        if (!await FileAccessRules.CanWriteEntityAsync(db, user, request.EntityType, request.EntityId, ct))
        {
            return await FileAccessRules.CanReadEntityAsync(db, user, request.EntityType, request.EntityId, ct)
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

        var existing = await db.Taggings.AsNoTracking().FirstOrDefaultAsync(
            x => x.TagId == tag.Id && x.EntityType == request.EntityType && x.EntityId == request.EntityId, ct);
        if (existing is not null)
        {
            // Idempotent: tagging twice is a no-op.
            return TypedResults.Created(
                $"/api/v1/taggings/{existing.Id}",
                new TaggingDto(existing.Id, new TagDto(tag.Id, tag.Name, tag.Slug), existing.EntityType, existing.EntityId));
        }

        var tagging = new Tagging
        {
            TagId = tag.Id,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            AddedBy = user.UserId,
        };
        db.Taggings.Add(tagging);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/taggings/{tagging.Id}",
            new TaggingDto(tagging.Id, new TagDto(tag.Id, tag.Name, tag.Slug), tagging.EntityType, tagging.EntityId));
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

        if (!await FileAccessRules.CanWriteEntityAsync(db, user, tagging.EntityType, tagging.EntityId, ct))
        {
            return ApiProblems.NotFound("tagging.not_found");
        }

        db.Taggings.Remove(tagging);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

}
