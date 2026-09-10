// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
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

    private static async Task<Results<Ok<List<TagDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListTagsAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        string? search,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Every account holds this through the All Users seed, so the catalogue reads as
        // openly as it always did — but as an entry an installation can tighten, rather
        // than as an absence of any rule.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Tags, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
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
        IAccessService access,
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
            return ApiProblems.BadRequest("tagging.entity_type_unknown", $"Unknown entity type '{entityType}'.");
        }

        if (!await FileAccessRules.CanReadTargetAsync(db, access, ctx, new AttachmentTarget(parsedType, entityId), ct))
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
        IAccessService access,
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
            return ApiProblems.BadRequest("tagging.entity_type_unknown", $"Unknown entity type '{request.EntityType}'.");
        }

        var target = new AttachmentTarget(parsedType, request.EntityId);
        if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, target, ct))
        {
            return await FileAccessRules.CanReadTargetAsync(db, access, ctx, target, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("tagging.entity_not_found");
        }

        var name = request.TagName.Trim();
        var slug = Tag.Slugify(name);
        var tag = await db.Tags.FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (tag is null)
        {
            // Applying an existing tag is part of writing the object, which is already
            // established. Coining a NEW one adds to a vocabulary everybody shares, so
            // that much is the tag domain's business rather than this object's.
            if (!CreateRules.MayCreate(ctx, AccessDomain.Tags))
            {
                return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
            }

            tag = new Tag { Name = name, Slug = slug };
            db.Tags.Add(tag);
            try
            {
                // Materialize the identity now — the tagging row references it by value.
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (IsRaceOn(e, TagIdentityIndexes))
            {
                // Somebody coined the same tag between the look-up above and this write. Saving a
                // form that names two new tags at once produces exactly that, and the loser used
                // to leave the endpoint as an unhandled 500.
                //
                // The loser adopts the winner's row rather than refusing. A tag is identified by
                // its slug and carries nothing else that could differ, so the caller asked for a
                // tag by name and a tag by that name now exists: refusing would be reporting a
                // conflict over an outcome that is precisely what was wanted. This is the one
                // place in the codebase where a slug race resolves by adoption instead of by a
                // conflict, and that is why — elsewhere the two writers meant different things.
                db.Entry(tag).State = EntityState.Detached;
                tag = await db.Tags.FirstOrDefaultAsync(x => x.Slug == slug, ct);

                // The row is gone again only if it was deleted in the same instant it was created,
                // which nothing in the product does. Refusing beats inventing an identity.
                if (tag is null)
                {
                    return ApiProblems.Conflict(
                        "tagging.tag_race",
                        "The tag was created and removed while this request was being handled. "
                        + "Try again.");
                }
            }
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
            AddedBy = ctx.UserId,
        };
        db.Taggings.Add(tagging);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsRaceOn(e, TaggingIdentityIndexes))
        {
            // The same tag applied to the same target twice at once. The look-up above found
            // nothing for either request, so both tried to add — and this route is documented
            // idempotent, which it was only for requests far enough apart to see each other's row.
            // The loser reads the winner's row and reports the same success it would have reported
            // had it arrived a moment later, which is what idempotent has to mean under
            // concurrency to mean anything.
            db.Entry(tagging).State = EntityState.Detached;
            var winner = parsedType is { } racedType
                ? await db.Taggings.AsNoTracking().FirstOrDefaultAsync(
                    x => x.TagId == tagId && x.EntityType == racedType && x.EntityId == request.EntityId, ct)
                : await db.Taggings.AsNoTracking().FirstOrDefaultAsync(
                    x => x.TagId == tagId && x.FeatureId == request.EntityId, ct);

            if (winner is null)
            {
                return ApiProblems.Conflict(
                    "tagging.race",
                    "The tag was applied and removed while this request was being handled. Try again.");
            }

            return TypedResults.Created(
                $"/api/v1/taggings/{winner.Id}",
                winner.ToDto(new TagDto(tag.Id, tag.Name, tag.Slug)));
        }

        return TypedResults.Created(
            $"/api/v1/taggings/{tagging.Id}",
            tagging.ToDto(new TagDto(tag.Id, tag.Name, tag.Slug)));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        long id,
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

        var tagging = await db.Taggings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tagging is null)
        {
            return ApiProblems.NotFound("tagging.not_found");
        }

        if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, TargetOf(tagging), ct))
        {
            return ApiProblems.NotFound("tagging.not_found");
        }

        db.Taggings.Remove(tagging);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The unique indexes that make a tag's identity, and the ones that make a tagging's.
    /// </summary>
    /// <remarks>
    /// A tag is unique on both its slug and its name, and either can be the one PostgreSQL
    /// reports — they are derived from the same input, so which index refuses first is not this
    /// code's to predict. Matching only the slug caught the race in a test and not in the wild.
    /// A tagging is unique per tag and target, in two indexes because the target is a feature or
    /// a non-feature entity and never both.
    /// </remarks>
    private static readonly string[] TagIdentityIndexes = ["ix_tags_slug", "ix_tags_name"];

    private static readonly string[] TaggingIdentityIndexes =
        ["ix_taggings_tag_id_feature_id", "ix_taggings_tag_id_entity_type_entity_id"];

    /// <summary>
    /// Whether a failed write is a second request about the same thing arriving at the same moment.
    /// </summary>
    /// <remarks>
    /// Matched on the index by name rather than on the error code alone, so that a different
    /// unique violation arriving through this path — a defect somewhere else in the write — is
    /// not quietly swallowed and answered as somebody else's success.
    /// </remarks>
    private static bool IsRaceOn(DbUpdateException e, string[] indexes) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
        && violation.ConstraintName is { } name
        && Array.IndexOf(indexes, name) >= 0;

    // The row's XOR maps directly: a feature row has a null EntityType, which is exactly
    // the parsed shape of a feature target.
    private static AttachmentTarget TargetOf(Tagging x) => new(x.EntityType, x.FeatureId ?? x.EntityId!.Value);

    private static TaggingDto ToDto(this Tagging x, TagDto tag) => new(
        x.Id,
        tag,
        AttachmentTargets.NameOf(x.FeatureId, x.EntityType),
        x.FeatureId ?? x.EntityId!.Value);
}
