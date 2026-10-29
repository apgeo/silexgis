// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.RasterMaps;

/// <summary>One place pin as its writer holds it: the link, its two members, and its map.</summary>
public sealed class PlacePoint
{
    public required ResLink Link { get; init; }

    /// <summary>The map's picture, anchored at the pin's point; the link's main member.</summary>
    public required ResLinkMember Picture { get; init; }

    /// <summary>The place pinned.</summary>
    public required ResLinkMember Place { get; init; }

    /// <summary>The map the pin belongs to, read untracked.</summary>
    public required RasterMap Map { get; init; }

    public Guid PlaceId => Place.FeatureId!.Value;

    /// <summary>The stored-frame pixel the pin carries.</summary>
    public (int X, int Y) Pixel =>
        PlacePointAnchor.Read(Picture.Anchor) is { } parsed ? (parsed.Px, parsed.Py) : (0, 0);
}

/// <summary>
/// The one writer of the places pinned on phones' raster maps. A pin is a link of the place-pin
/// relation, and nothing else creates one or changes what it says: the upload section and review
/// acceptance, the undo, the browser's map screen, a new revision of the picture, a turn recorded on
/// it and the development seed all come here. Tracked, not saved, unless a method says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// The pin keeps the identifier the phone gave it, as the link's. Its revision — what a phone
/// arbitrates an edit on — is the link's own write time, so every change a phone must hear of
/// (a pixel, a place, a removal, a return) moves it, while redrawing the fraction for a turn or
/// carrying the pin onto a same-size new picture leaves it alone and leaves the phone's held
/// revision valid.
/// </para>
/// <para>
/// One live pin per place on a map cannot be an index, because the pair spans two member rows.
/// Every write that could break it takes the map's row lock first and asks after the lock; the
/// integrity check is the backstop.
/// </para>
/// </remarks>
public sealed class PlacePointStore(SilexGisDbContext db)
{
    /// <summary>Takes the map's row lock for the rest of the surrounding transaction.</summary>
    public Task LockAsync(Guid mapId, CancellationToken ct = default) => PlacePointSql.LockMapAsync(db, mapId, ct);

    /// <summary>Whether another live pin already marks the place on the map.</summary>
    public Task<bool> LivePinOfAsync(Guid mapId, Guid placeId, Guid except, CancellationToken ct = default) =>
        PlacePointQueries.Rows(db).AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(p => p.MapId == mapId && p.PlaceId == placeId && p.DeletedAt == null && p.Id != except, ct);

    /// <summary>The original file of the map's current picture: the file the phone measures on.</summary>
    public async Task<StoredFile?> PictureOfAsync(RasterMap map, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        return (await DocumentQueries.CurrentFileAsync(db, map.DocumentId, ct))?.File;
    }

    /// <summary>
    /// A new pin under <paramref name="id"/>: the map's picture at the pixel, measured on
    /// <paramref name="picture"/>, and the place. The caller has asked whether the place is free.
    /// </summary>
    public async Task<ResLink> AddAsync(
        RasterMap map, StoredFile picture, Guid id, Guid placeId, int px, int py, Guid? by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(picture);
        var relationId = await PlacePointQueries.RelationIdAsync(db, ct)
            ?? throw new InvalidOperationException("The place-pin relation is not seeded.");
        var link = new ResLink
        {
            Id = id,
            ShortCode = ResLinkRules.NewShortCode(),
            RelationTypeId = relationId,
            CreatedBy = by,
        };
        db.ResLinks.Add(link);
        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = id,
            EntityType = AttachedEntityType.Document,
            EntityId = map.DocumentId,
            IsMain = true,
            AnchorKind = AnchorKind.ImageRegion,
            Anchor = PlacePointAnchor.Write(px, py, PictureFrames.Of(picture)),
            AnchorFileId = picture.Id,
            SortOrder = 0,
            AddedBy = by,
        });
        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = id,
            FeatureId = placeId,
            SortOrder = 1,
            AddedBy = by,
        });
        return link;
    }

    /// <summary>
    /// The pin under this identifier, tracked, removed or not, with its map whether or not the map
    /// is removed; null when no pin has it.
    /// </summary>
    public async Task<PlacePoint?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var row = await PlacePointQueries.Rows(db).AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
        {
            return null;
        }

        var link = await db.ResLinks.FirstAsync(l => l.Id == id, ct);
        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == id).ToListAsync(ct);
        var map = await db.RasterMaps.AsNoTracking().IgnoreQueryFilters().FirstAsync(m => m.Id == row.MapId, ct);
        return new PlacePoint
        {
            Link = link,
            Picture = members.First(m => m.IsMain && m.EntityType == AttachedEntityType.Document),
            Place = members.First(m => m.FeatureId != null),
            Map = map,
        };
    }

    /// <summary>
    /// The pin moved to a pixel of the map's current picture — the frame a phone measures in, and
    /// the one the browser's map screen draws — so a pin left on an earlier picture is placed
    /// again by moving it.
    /// </summary>
    public async Task MoveAsync(PlacePoint pin, int px, int py, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var picture = await PictureOfAsync(pin.Map, ct);
        if (picture is not null)
        {
            pin.Picture.AnchorFileId = picture.Id;
        }

        var frame = picture is not null
            ? PictureFrames.Of(picture)
            : await FrameOfAsync(pin.Picture.AnchorFileId, ct);
        pin.Picture.Anchor = PlacePointAnchor.Write(px, py, frame);
        Changed(pin);
    }

    /// <summary>The pin now marks another place.</summary>
    public void SetPlace(PlacePoint pin, Guid placeId)
    {
        ArgumentNullException.ThrowIfNull(pin);
        pin.Place.FeatureId = placeId;
        Changed(pin);
    }

    /// <summary>The pin removed — stamped, so the phones are told and an undo can bring it back.</summary>
    public static void Remove(PlacePoint pin, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(pin);
        pin.Link.DeletedAt ??= at;
    }

    /// <summary>A removed pin back under its own identifier, so the phones receive it again.</summary>
    public void Restore(PlacePoint pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        pin.Link.DeletedAt = null;
        Changed(pin);
    }

    /// <summary>
    /// The fractions of the pins measured on these files — or, with none named, of every pin still
    /// lacking one — drawn again from their stored pixels, for the frame the file has now. Saved.
    /// </summary>
    /// <remarks>
    /// What a turn recorded on a file calls, and what a database whose pins came across from the old
    /// table calls once at start. The pin's revision does not move: the pixel a phone holds is what
    /// it was, and only how the browser draws it changed.
    /// </remarks>
    public async Task RefreshFramesAsync(IReadOnlyCollection<Guid>? fileIds, CancellationToken ct = default)
    {
        var pins = PlacePointQueries.Rows(db).AsNoTracking().IgnoreQueryFilters().Where(p => p.AnchorFileId != null);
        pins = fileIds is null
            ? pins.Where(p => !EF.Functions.JsonExists(p.Anchor!, "x"))
            : pins.Where(p => fileIds.Contains(p.AnchorFileId!.Value));
        var wanted = await pins.Select(p => p.Id).ToListAsync(ct);
        if (wanted.Count == 0)
        {
            return;
        }

        var members = await db.ResLinkMembers
            .Where(m => wanted.Contains(m.ResLinkId) && m.IsMain && m.EntityType == AttachedEntityType.Document)
            .ToListAsync(ct);
        var frames = await FramesOfAsync([.. members.Select(m => m.AnchorFileId!.Value)], ct);
        foreach (var member in members)
        {
            Redraw(member, frames.GetValueOrDefault(member.AnchorFileId!.Value));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A new revision of a picture has taken over: the pins measured on the revision it replaced
    /// are carried onto <paramref name="toFileId"/> when the two are the same stored size, which
    /// keeps every pixel meaning the same spot, and stay on the file they were measured on
    /// otherwise. Carrying a pin over does not move its revision. Tracked, not saved — the new
    /// file may itself be waiting in the tracker.
    /// </summary>
    public async Task FollowRevisionAsync(IReadOnlyCollection<Guid> fromFileIds, Guid toFileId, CancellationToken ct = default)
    {
        if (fromFileIds.Count == 0)
        {
            return;
        }

        var relationId = await PlacePointQueries.RelationIdAsync(db, ct);
        if (relationId is null)
        {
            return;
        }

        var members = await (
                from member in db.ResLinkMembers
                join link in db.ResLinks on member.ResLinkId equals link.Id
                where link.RelationTypeId == relationId
                    && member.AnchorFileId != null && fromFileIds.Contains(member.AnchorFileId.Value)
                select member)
            .ToListAsync(ct);
        if (members.Count == 0)
        {
            return;
        }

        var to = await db.StoredFiles.FindAsync([toFileId], ct);
        var toFrame = to is null ? null : PictureFrames.Of(to);
        if (toFrame is not { } target)
        {
            return;
        }

        var fromFrames = await FramesOfAsync([.. members.Select(m => m.AnchorFileId!.Value)], ct);
        foreach (var member in members)
        {
            if (fromFrames.GetValueOrDefault(member.AnchorFileId!.Value) is { } from
                && from.Width == target.Width && from.Height == target.Height)
            {
                member.AnchorFileId = toFileId;
                Redraw(member, target);
            }
        }
    }

    /// <summary>The pin's payload drawn again for <paramref name="frame"/>, its pixel unchanged.</summary>
    private static void Redraw(ResLinkMember picture, PictureFrame? frame)
    {
        if (PlacePointAnchor.Read(picture.Anchor) is not { } parsed)
        {
            return;
        }

        var redrawn = PlacePointAnchor.Write(parsed.Px, parsed.Py, frame);
        if (!string.Equals(redrawn, picture.Anchor, StringComparison.Ordinal))
        {
            picture.Anchor = redrawn;
        }
    }

    /// <summary>A change the phones must hear of moves the pin's own revision.</summary>
    private void Changed(PlacePoint pin) => db.Entry(pin.Link).Property(l => l.UpdatedAt).IsModified = true;

    private async Task<PictureFrame?> FrameOfAsync(Guid? fileId, CancellationToken ct) =>
        fileId is { } id ? (await FramesOfAsync([id], ct)).GetValueOrDefault(id) : null;

    private async Task<Dictionary<Guid, PictureFrame?>> FramesOfAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct)
    {
        var ids = fileIds.Distinct().ToList();
        var files = await db.StoredFiles.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.PixelWidth, f.PixelHeight, f.PixelOrientation, f.OrientationQuarterTurns, f.Metadata })
            .ToListAsync(ct);
        return files.ToDictionary(
            f => f.Id,
            f => PictureFrames.Of(f.PixelWidth, f.PixelHeight, f.PixelOrientation, f.OrientationQuarterTurns, f.Metadata));
    }
}
