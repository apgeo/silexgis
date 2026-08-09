// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>An album write the album's own rules refused, carrying a stable code.</summary>
public sealed class AlbumWriteException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The single mutator of an album's arrangement: what is in it, in what order, and which
/// picture stands for it.
///
/// <para>
/// One place, because the three are not independent. A cover has to be a member, so removing a
/// picture may have to clear the cover; a position has to be free, so a drop into a full gap
/// has to spread the sequence out first. Spread across the endpoints, each of those would be a
/// rule somebody remembered on one route and not another.
/// </para>
/// <para>
/// Nothing here decides who may do any of it. Whether the caller may write the album is settled
/// by the request, which this deliberately does not have.
/// </para>
/// </summary>
public sealed class AlbumWriteService(SilexGisDbContext db)
{
    public const string FullCode = "album.full";
    public const string NotAMemberCode = "album.cover_not_a_member";
    public const string NotAPhotographCode = "album.not_a_photograph";

    /// <summary>
    /// Adds pictures to the end of an album, in the order given, skipping any already in it.
    /// Tracked, not saved.
    /// </summary>
    /// <remarks>
    /// Adding what is already there is the same fact rather than an error: somebody selecting
    /// forty pictures of which three are already in the album meant to add the other
    /// thirty-seven, not to be told off.
    /// </remarks>
    public async Task<IReadOnlyList<AlbumItem>> AddAsync(
        Guid albumId, IReadOnlyList<Guid> documentIds, Guid addedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var existing = await db.AlbumItems.AsNoTracking()
            .Where(i => i.AlbumId == albumId)
            .Select(i => new { i.DocumentId, i.SortOrder })
            .ToListAsync(ct);

        var already = existing.Select(i => i.DocumentId).ToHashSet();
        var wanted = documentIds.Distinct().Where(id => !already.Contains(id)).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        if (existing.Count + wanted.Count > AlbumOrdering.MaxItems)
        {
            throw new AlbumWriteException(
                FullCode, $"An album holds at most {AlbumOrdering.MaxItems} pictures.");
        }

        // Only photographs. An album of survey PDFs is a cabinet, and the viewer that opens an
        // album has nothing to draw for one.
        var photographs = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Kind == FileKind.Image
                && db.DocumentVersions.Any(v =>
                    v.Id == f.DocumentVersionId && v.IsCurrent && wanted.Contains(v.DocumentId)))
            .Join(
                db.DocumentVersions.AsNoTracking(),
                f => f.DocumentVersionId,
                v => v.Id,
                (_, v) => v.DocumentId)
            .Distinct()
            .ToListAsync(ct);

        if (photographs.Count != wanted.Count)
        {
            throw new AlbumWriteException(NotAPhotographCode, "An album holds photographs only.");
        }

        var next = existing.Count == 0 ? (int?)null : existing.Max(i => i.SortOrder);
        var added = new List<AlbumItem>(wanted.Count);
        foreach (var documentId in wanted)
        {
            next = AlbumOrdering.Append(next);
            var item = new AlbumItem
            {
                AlbumId = albumId,
                DocumentId = documentId,
                SortOrder = next.Value,
                AddedBy = addedBy,
            };
            db.AlbumItems.Add(item);
            added.Add(item);
        }

        return added;
    }

    /// <summary>
    /// Removes pictures from an album, clearing the cover if it was one of them. Tracked, not
    /// saved.
    /// </summary>
    public async Task RemoveAsync(Guid albumId, IReadOnlyList<Guid> documentIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var going = await db.AlbumItems
            .Where(i => i.AlbumId == albumId && documentIds.Contains(i.DocumentId))
            .ToListAsync(ct);
        db.AlbumItems.RemoveRange(going);

        // A cover has to be a member. Left pointing at a picture that is no longer in the album,
        // it would vanish from the shelf for anybody who may not read that one picture — a
        // listing whose covers depend on the reader is a listing nobody can describe.
        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == albumId, ct);
        if (album?.CoverDocumentId is { } cover && documentIds.Contains(cover))
        {
            album.CoverDocumentId = null;
        }
    }

    /// <summary>
    /// Moves one picture to sit between two others. Tracked, not saved.
    /// </summary>
    /// <param name="afterDocumentId">The picture it goes after, or null for the start.</param>
    /// <remarks>
    /// One row changes in the ordinary case. When the gap between its new neighbours has run
    /// out — which takes about ten drops into the same place — the album is spread out again
    /// first, and that is the one time every row is rewritten.
    /// </remarks>
    public async Task MoveAsync(
        Guid albumId, Guid documentId, Guid? afterDocumentId, CancellationToken ct = default)
    {
        var items = await db.AlbumItems
            .Where(i => i.AlbumId == albumId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);

        var moving = items.Find(i => i.DocumentId == documentId);
        if (moving is null)
        {
            return;
        }

        var (before, after) = NeighboursOf(items, moving, afterDocumentId);
        var position = AlbumOrdering.Between(before?.SortOrder, after?.SortOrder);

        if (position is null)
        {
            // Out of room. Spread the album out and ask again — against the respread numbers,
            // which are wide enough that the second answer always exists.
            var order = items.Where(i => i.Id != moving.Id).ToList();
            var spread = AlbumOrdering.Respread(order.Count);
            for (var i = 0; i < order.Count; i++)
            {
                order[i].SortOrder = spread[i];
            }

            (before, after) = NeighboursOf(order, moving, afterDocumentId);
            position = AlbumOrdering.Between(before?.SortOrder, after?.SortOrder);
        }

        moving.SortOrder = position ?? AlbumOrdering.Append(items.Max(i => i.SortOrder));
    }

    /// <summary>
    /// Sets the album's cover, which must be one of its pictures. Tracked, not saved.
    /// </summary>
    /// <exception cref="AlbumWriteException"><c>album.cover_not_a_member</c>.</exception>
    public async Task SetCoverAsync(Guid albumId, Guid? documentId, CancellationToken ct = default)
    {
        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == albumId, ct)
            ?? throw new AlbumWriteException("album.not_found", "The album no longer exists.");

        if (documentId is { } cover
            && !await db.AlbumItems.AsNoTracking()
                .AnyAsync(i => i.AlbumId == albumId && i.DocumentId == cover, ct))
        {
            throw new AlbumWriteException(
                NotAMemberCode, "An album's cover has to be one of its pictures.");
        }

        album.CoverDocumentId = documentId;
    }

    /// <summary>
    /// The rows either side of where a picture is going.
    /// </summary>
    /// <remarks>
    /// The moving row is excluded from both, because a picture is never its own neighbour: a
    /// drop that landed next to where it already was would otherwise compute a position between
    /// itself and something else.
    /// </remarks>
    private static (AlbumItem? Before, AlbumItem? After) NeighboursOf(
        IReadOnlyList<AlbumItem> items, AlbumItem moving, Guid? afterDocumentId)
    {
        var others = items.Where(i => i.Id != moving.Id).OrderBy(i => i.SortOrder).ToList();
        if (afterDocumentId is not { } anchor)
        {
            return (null, others.Count > 0 ? others[0] : null);
        }

        var index = others.FindIndex(i => i.DocumentId == anchor);
        return index < 0
            ? (others.Count > 0 ? others[^1] : null, null)
            : (others[index], index + 1 < others.Count ? others[index + 1] : null);
    }
}
