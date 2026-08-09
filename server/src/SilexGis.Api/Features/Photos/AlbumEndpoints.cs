// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Photos;

/// <summary>One album, as a list of them shows it.</summary>
/// <param name="PhotoCount">
/// How many of its pictures this caller may read — the same rule as the listing that opens when
/// it is clicked, so the number and the grid agree. A count over everything in it would state
/// exactly how much the grid withheld.
/// </param>
/// <param name="CoverThumbnailUrl">A rendering of the cover, or null when there is no cover to draw.</param>
public sealed record AlbumDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? CoverDocumentId,
    string? CoverThumbnailUrl,
    string? SubjectEntityType,
    Guid? SubjectEntityId,
    Visibility Visibility,
    Guid? CavingGroupId,
    Guid OwnerUserId,
    int PhotoCount,
    bool HasActiveShare,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Creating or editing an album.</summary>
public sealed record AlbumWriteRequest(
    string Title,
    string? Description,
    Visibility Visibility,
    Guid? CavingGroupId,
    string? SubjectEntityType,
    Guid? SubjectEntityId);

public sealed class AlbumWriteRequestValidator : AbstractValidator<AlbumWriteRequest>
{
    public AlbumWriteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Visibility).IsInEnum();

        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be a band that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");

        RuleFor(x => x.SubjectEntityId).NotNull()
            .When(x => !string.IsNullOrWhiteSpace(x.SubjectEntityType))
            .WithMessage("A subject needs both a type and an id.");
    }
}

/// <summary>Moving a picture inside an album.</summary>
/// <param name="AfterDocumentId">The picture it goes after, or null to put it first.</param>
public sealed record AlbumReorderRequest(Guid DocumentId, Guid? AfterDocumentId);

/// <summary>A minted share link. The token is shown once and never stored in the clear.</summary>
public sealed record AlbumShareDto(Guid Id, string? Token, FeatureShareMode Mode, DateTimeOffset CreatedAt);

/// <summary>
/// Named, ordered sets of photographs, and the links that put one on a club's website.
/// </summary>
/// <remarks>
/// <para>
/// An album is content in its own right — owner, club, visibility — and is governed exactly as
/// a document is. Membership grants nothing in either direction: adding a picture to an album
/// does not publish it, and a reader of the album sees only the pictures they could already
/// have seen. That is what makes an album safe to share.
/// </para>
/// <para>
/// The share link is the one anonymous surface here, and it opens renderings only: no original
/// bytes, no capture positions, and no path from a photograph to the cave it was taken at. Not
/// as a property of the link but of the route it opens, which is where the rule is enforced.
/// </para>
/// </remarks>
public static class AlbumEndpoints
{
    public const string NotFoundCode = "album.not_found";
    public const string WriteForbiddenCode = "album.write_forbidden";

    public static RouteGroupBuilder MapAlbumEndpoints(this RouteGroupBuilder api)
    {
        var albums = api.MapGroup("/albums").WithTags("Albums");

        albums.MapGet("/", ListAsync)
            .WithSummary("Albums the caller may read.");
        albums.MapPost("/", CreateAsync).WithValidation<AlbumWriteRequest>()
            .WithSummary("Creates an album.");
        albums.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One album.");
        albums.MapPut("/{id:guid}", UpdateAsync).WithValidation<AlbumWriteRequest>()
            .WithSummary("Renames or re-audiences an album; requires write access.");
        albums.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an album. The photographs in it are untouched.");

        albums.MapPost("/{id:guid}/items", AddItemsAsync)
            .WithSummary("Adds photographs to the end of an album.");
        albums.MapDelete("/{id:guid}/items/{documentId:guid}", RemoveItemAsync)
            .WithSummary("Takes a photograph out of an album; clears the cover if it was the cover.");
        albums.MapPost("/{id:guid}/reorder", ReorderAsync).WithValidation<AlbumReorderRequest>()
            .WithSummary("Moves a photograph to sit after another, or to the start.");
        albums.MapPut("/{id:guid}/cover", SetCoverAsync)
            .WithSummary("Sets the album's cover, which has to be one of its photographs.");

        albums.MapPost("/{id:guid}/share", ShareAsync)
            .WithSummary("Mints a share link; the token is returned once and never stored in the clear.");
        albums.MapDelete("/{id:guid}/share", RevokeShareAsync)
            .WithSummary("Revokes every share link of an album.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<AlbumDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        Guid? subjectEntityId = null,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var albums = db.Albums.AsNoTracking().VisibleTo(ctx, AccessDomain.Documents);
        if (subjectEntityId is { } subject)
        {
            albums = albums.Where(a => a.SubjectEntityId == subject || a.SubjectFeatureId == subject);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var paged = await albums
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.Id)
            .ToPagedAsync(p, size, a => a, ct);

        var items = await ProjectAsync(db, ctx, tokens, paged.Items, ct);
        return TypedResults.Ok(new PagedResult<AlbumDto>(
            items, paged.Page, paged.PageSize, paged.TotalItems));
    }

    private static async Task<Results<Ok<AlbumDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var album = await AlbumAccess.ReadableAsync(db, access, ctx, id, ct);
        if (album is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var projected = await ProjectAsync(db, ctx, tokens, [album], ct);
        return TypedResults.Ok(projected[0]);
    }

    private static async Task<Results<Created<AlbumDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        AlbumWriteRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // An album is content in the documents domain, so creating one asks the same question
        // uploading a document does — and naming a club is guarded on its own terms, so it
        // cannot be a way into one.
        if (request.CavingGroupId is { } groupId)
        {
            if (!await db.CavingGroups.AsNoTracking().AnyAsync(g => g.Id == groupId, ct))
            {
                return ApiProblems.BadRequest("document.caving_group_not_found");
            }

            if (!CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, groupId))
            {
                return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
            }
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var (subjectType, subjectId, subjectFeatureId, problem) = ParseSubject(request);
        if (problem is not null)
        {
            return problem;
        }

        var album = new Album
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Visibility = request.Visibility,
            CavingGroupId = request.CavingGroupId,
            OwnerUserId = ctx.UserId,
            SubjectEntityType = subjectType,
            SubjectEntityId = subjectId,
            SubjectFeatureId = subjectFeatureId,
        };
        db.Albums.Add(album);
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, ctx, tokens, [album], ct);
        return TypedResults.Created($"/api/v1/albums/{album.Id}", projected[0]);
    }

    private static async Task<Results<Ok<AlbumDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        AlbumWriteRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (album is null || await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        // Binding to a club hands its members whatever their rulesets grant over its content, so
        // it is guarded beyond write — and asked only when the binding actually changes, since
        // re-saving an album into the club it is already in is not a fresh act of binding.
        if (request.CavingGroupId is { } groupId
            && groupId != album.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, groupId))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        var (subjectType, subjectId, subjectFeatureId, problem) = ParseSubject(request);
        if (problem is not null)
        {
            return problem;
        }

        album.Title = request.Title.Trim();
        album.Description = request.Description;
        album.Visibility = request.Visibility;
        album.CavingGroupId = request.CavingGroupId;
        album.SubjectEntityType = subjectType;
        album.SubjectEntityId = subjectId;
        album.SubjectFeatureId = subjectFeatureId;
        await db.SaveChangesAsync(ct);

        var projected = await ProjectAsync(db, ctx, tokens, [album], ct);
        return TypedResults.Ok(projected[0]);
    }

    /// <summary>
    /// Deletes an album. The photographs in it are untouched — an album is an arrangement of
    /// pictures, never the pictures themselves, and deleting one must not be a way to lose them.
    /// </summary>
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

        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (album is null || await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        db.Albums.Remove(album);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Adds photographs to an album.
    /// </summary>
    /// <remarks>
    /// A picture has to be readable by whoever is adding it: putting something into an album
    /// hands it to everyone who may read the album, so this is the same question attaching a
    /// file to a cave asks, and for the same reason.
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> AddItemsAsync(
        Guid id,
        List<Guid> documentIds,
        SilexGisDbContext db,
        AlbumWriteService albums,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        var readable = new List<Guid>();
        foreach (var documentId in documentIds.Distinct())
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
            var content = document is null ? null : await DocumentQueries.CurrentFileAsync(db, documentId, ct);
            if (document is not null
                && content is not null
                && await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content.File, ct))
            {
                readable.Add(documentId);
            }
        }

        try
        {
            await albums.AddAsync(id, readable, ctx.UserId, ct);
        }
        catch (AlbumWriteException e)
        {
            return ApiProblems.Conflict(e.Code, e.Message);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RemoveItemAsync(
        Guid id,
        Guid documentId,
        SilexGisDbContext db,
        AlbumWriteService albums,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        await albums.RemoveAsync(id, [documentId], ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> ReorderAsync(
        Guid id,
        AlbumReorderRequest request,
        SilexGisDbContext db,
        AlbumWriteService albums,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        await albums.MoveAsync(id, request.DocumentId, request.AfterDocumentId, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> SetCoverAsync(
        Guid id,
        Guid? documentId,
        SilexGisDbContext db,
        AlbumWriteService albums,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        try
        {
            await albums.SetCoverAsync(id, documentId, ct);
        }
        catch (AlbumWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Mints a share link. The plaintext token is returned once and stored only as a hash, so
    /// the link cannot be recovered from the database — it is revoked and re-minted instead.
    /// </summary>
    private static async Task<Results<Created<AlbumShareDto>, UnauthorizedHttpResult, ProblemHttpResult>> ShareAsync(
        Guid id,
        FeatureShareMode? mode,
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

        if (await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Sharing is not writing: it publishes. Guarded on Share in the documents domain, the
        // same right that governs every other link this application mints.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Documents, AccessAction.Share, null).Allowed
            && !await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var share = new AlbumShare
        {
            AlbumId = id,
            TokenHash = HashToken(token),
            Mode = mode ?? FeatureShareMode.Public,
            CreatedBy = ctx.UserId,
        };
        db.AlbumShares.Add(share);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/albums/{id}", new AlbumShareDto(share.Id, token, share.Mode, share.CreatedAt));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RevokeShareAsync(
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

        if (await AlbumAccess.ReadableAsync(db, access, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await AlbumAccess.CanWriteAsync(db, access, ctx, id, ct))
        {
            return ApiProblems.Forbidden(WriteForbiddenCode);
        }

        var now = DateTimeOffset.UtcNow;
        await db.AlbumShares
            .Where(s => s.AlbumId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        return TypedResults.NoContent();
    }

    /// <summary>SHA-256 of a share token, base64url — the only form ever stored.</summary>
    internal static string HashToken(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>The album's subject, split into the feature half and the polymorphic pair.</summary>
    private static (AttachedEntityType? Type, Guid? Id, Guid? FeatureId, ProblemHttpResult? Problem)
        ParseSubject(AlbumWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectEntityType) || request.SubjectEntityId is not { } subjectId)
        {
            return (null, null, null, null);
        }

        if (!AttachmentTargets.TryParse(request.SubjectEntityType, out var parsed))
        {
            return (null, null, null, ApiProblems.BadRequest("attachment.entity_type_unknown"));
        }

        return parsed is null ? (null, null, subjectId, null) : (parsed, subjectId, null, null);
    }

    /// <summary>
    /// Turns albums into responses, counting each one's pictures under the same rule as the
    /// listing that opens when it is clicked.
    /// </summary>
    private static async Task<IReadOnlyList<AlbumDto>> ProjectAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IFileAccessTokenService tokens,
        IReadOnlyList<Album> albums,
        CancellationToken ct)
    {
        if (albums.Count == 0)
        {
            return [];
        }

        var ids = albums.Select(a => a.Id).ToList();
        var photographs = await PhotoQueries.VisiblePhotographsAsync(db, ctx, ct);

        // One grouped statement rather than a count per album: the access walk inside it would
        // otherwise become a correlated subplan run once per row of a page.
        var counts = await db.AlbumItems.AsNoTracking()
            .Where(i => ids.Contains(i.AlbumId) && photographs.Any(d => d.Id == i.DocumentId))
            .GroupBy(i => i.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AlbumId, x => x.Count, ct);

        var shared = await db.AlbumShares.AsNoTracking()
            .Where(s => ids.Contains(s.AlbumId) && s.RevokedAt == null)
            .Select(s => s.AlbumId)
            .Distinct()
            .ToListAsync(ct);

        var coverIds = albums.Where(a => a.CoverDocumentId is not null)
            .Select(a => a.CoverDocumentId!.Value)
            .ToList();
        var covers = coverIds.Count == 0
            ? []
            : await (from file in db.StoredFiles.AsNoTracking()
                     join version in db.DocumentVersions.AsNoTracking()
                         on file.DocumentVersionId equals version.Id
                     where version.IsCurrent
                         && coverIds.Contains(version.DocumentId)
                         && photographs.Any(d => d.Id == version.DocumentId)
                     select new { version.DocumentId, file.Id, file.Geom })
                .ToDictionaryAsync(x => x.DocumentId, x => x, ct);

        return
        [
            .. albums.Select(a =>
            {
                var cover = a.CoverDocumentId is { } coverId ? covers.GetValueOrDefault(coverId) : null;
                return new AlbumDto(
                    a.Id,
                    a.Title,
                    a.Description,
                    a.CoverDocumentId,
                    // A cover is drawn as a rendering and never as the upload, and it is only
                    // offered when the reader may see that picture — a cover that vanished for
                    // some readers is why the cover has to be a member in the first place.
                    cover is null
                        ? null
                        : $"/api/v1/files/{cover.Id}/thumbnail?size={PhotoMapping.ThumbnailSize}"
                          + $"&token={Uri.EscapeDataString(tokens.CreateToken(cover.Id, FileDelivery.DerivativesOnly))}",
                    a.SubjectFeatureId is not null
                        ? AttachmentTargets.FeatureName
                        : a.SubjectEntityType is { } type
                            ? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(type.ToString())
                            : null,
                    a.SubjectFeatureId ?? a.SubjectEntityId,
                    a.Visibility,
                    a.CavingGroupId,
                    a.OwnerUserId,
                    counts.GetValueOrDefault(a.Id),
                    shared.Contains(a.Id),
                    a.CreatedAt,
                    a.UpdatedAt);
            }),
        ];
    }
}

/// <summary>
/// Who may read or write an album.
/// </summary>
/// <remarks>
/// An album carries the owner / club / visibility trio and is governed by the same evaluator as
/// every other owned row in the documents domain. Deliberately no reach-through-membership: a
/// picture being in an album has never made the album readable, because that would let anybody
/// who could see one picture see the arrangement somebody else made of a hundred.
/// </remarks>
internal static class AlbumAccess
{
    public static async Task<Album?> ReadableAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid albumId, CancellationToken ct)
    {
        var album = await db.Albums.AsNoTracking().FirstOrDefaultAsync(a => a.Id == albumId, ct);
        if (album is null)
        {
            return null;
        }

        return (await access.DecideAsync(ctx, AccessAction.Read, album, ct)).Allowed ? album : null;
    }

    public static async Task<bool> CanWriteAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid albumId, CancellationToken ct)
    {
        var album = await db.Albums.AsNoTracking().FirstOrDefaultAsync(a => a.Id == albumId, ct);
        return album is not null
            && (await access.DecideAsync(ctx, AccessAction.Write, album, ct)).Allowed;
    }
}
