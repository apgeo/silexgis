// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.AccessHistory;

/// <summary>
/// One recorded reading: when, by whom, and of which file of which document. The reader's
/// name is a resolved label rather than a column, and is null where the account is gone.
/// </summary>
public sealed record FileAccessEventDto(
    long Id,
    DateTimeOffset At,
    Guid? UserId,
    string? UserName,
    Guid FileId,
    Guid DocumentId,
    string? DocumentTitle);

/// <summary>
/// Who has read a document, and what a person has read.
/// </summary>
/// <remarks>
/// <para>
/// This is a list of people, not a list of documents, and it is authorised as one. Being
/// allowed to read a document does not make its readership yours to see: a club member
/// could otherwise learn which other members had been looking at a particular survey, which
/// is a fact about them and one they never agreed to publish. So the per-document surface
/// asks for the right held over the audit trail — the same domain-wide right that opens the
/// change trail — or for the document's own owner, who is answerable for what they put in
/// and may reasonably ask who took a copy.
/// </para>
/// <para>
/// Both surfaces are additionally gated on being able to read the document at all, and
/// refuse with "not found" rather than "forbidden", because a history that answered
/// differently for a document that exists would disclose its existence.
/// </para>
/// <para>
/// A person's own history needs no permission beyond being that person. Nobody may fetch
/// somebody else's by naming them; the per-document view is the only way one person's
/// reading is visible to another, and it is bounded to a document they already administer.
/// </para>
/// </remarks>
public static class AccessHistoryEndpoints
{
    public static RouteGroupBuilder MapAccessHistoryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/access-history").WithTags("Access history");

        group.MapGet("/me", MineAsync)
            .WithSummary("Documents you have taken a copy of, newest first.");
        group.MapGet("/documents/{documentId:guid}", ForDocumentAsync)
            .WithSummary("Who has taken a copy of this document; requires Read on the Audit domain, or being the document's owner.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<FileAccessEventDto>>, UnauthorizedHttpResult>> MineAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.FileAccessEvents.AsNoTracking().Where(e => e.UserId == user.UserId);
        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        var titles = await TitlesAsync(db, rows.Select(r => r.DocumentId), ct);
        var items = rows.Select(e => new FileAccessEventDto(
            e.Id, e.At, e.UserId, null, e.FileId, e.DocumentId,
            titles.GetValueOrDefault(e.DocumentId))).ToList();

        return TypedResults.Ok(new PagedResult<FileAccessEventDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<PagedResult<FileAccessEventDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ForDocumentAsync(
        Guid documentId,
        SilexGisDbContext db,
        IAccessService access,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        var current = document is null ? null : await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        if (document is null
            || !await DocumentAccessRules.CanReadAsync(db, access, ctx, document, current?.File, ct))
        {
            return ApiProblems.NotFound("document.not_found");
        }

        var maySeeReaders = document.OwnerUserId == user.UserId
            || AccessEvaluator.Decide(ctx, AccessDomain.Audit, AccessAction.Read, null).Allowed;
        if (!maySeeReaders)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var query = db.FileAccessEvents.AsNoTracking().Where(e => e.DocumentId == documentId);
        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        // Names resolved after the page materialises rather than joined in: the label a
        // user may be shown under is a rule, and it lives in one place.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value), ct);

        var items = rows.Select(e => new FileAccessEventDto(
            e.Id, e.At, e.UserId,
            e.UserId is { } reader ? labels.GetValueOrDefault(reader) : null,
            e.FileId, e.DocumentId, document.Title)).ToList();

        return TypedResults.Ok(new PagedResult<FileAccessEventDto>(items, p, size, total));
    }

    private static async Task<Dictionary<Guid, string>> TitlesAsync(
        SilexGisDbContext db, IEnumerable<Guid> documentIds, CancellationToken ct)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        // A person's own history names documents they have already been handed the bytes
        // of, so the title discloses nothing the reading did not.
        return await db.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Title, ct);
    }
}
