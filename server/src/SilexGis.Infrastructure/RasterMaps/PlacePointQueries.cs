// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.RasterMaps;

/// <summary>One place pinned on a phone's raster map, as the pins are read.</summary>
public sealed class PlacePointRow
{
    /// <summary>The pin's identifier: the link's, which is the one the phone gave the pin.</summary>
    public Guid Id { get; init; }

    public Guid MapId { get; init; }

    /// <summary>The map's picture document, which the pin's link names.</summary>
    public Guid DocumentId { get; init; }

    public Guid PlaceId { get; init; }

    /// <summary>The picture member's payload: the stored pixel, and the fraction it is drawn at.</summary>
    public string? Anchor { get; init; }

    /// <summary>The file the pixel was measured on.</summary>
    public Guid? AnchorFileId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The pin's revision, which a phone arbitrates its edits on.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }
}

/// <summary>
/// Where the pins of phones' raster maps are read: the place-pin links, each with its map, its
/// place and its pixel. The rasters stream, the review screens and the held lines all read pins
/// through this, so what a pin is — which link, which two members, which map — is said once.
/// </summary>
/// <remarks>
/// <para>
/// A pin is a link of the place-pin relation with the map's picture as its main member, anchored at
/// a point, and the place as its one feature member. Its map is the one whose picture that is. A
/// link missing either end — what a place removed for good leaves behind — is no pin.
/// </para>
/// <para>
/// Read through the map's filter, so the pins of a removed map are hidden with it, as they always
/// were; a reader that must see them lifts the filters for the whole statement, which lifts this one
/// with them. Removed pins are not filtered: the moment a pin went is what tells a phone its map
/// changed, so every read that wants only live pins says so itself.
/// </para>
/// </remarks>
public static class PlacePointQueries
{
    public static IQueryable<PlacePointRow> Rows(SilexGisDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return
            from link in db.ResLinks
            join relation in db.ResLinkRelationTypes on link.RelationTypeId equals (long?)relation.Id
            where relation.Code == ResLinkRelationTypeSeeds.MapPlacePointCode
            from picture in db.ResLinkMembers
                .Where(m => m.ResLinkId == link.Id && m.IsMain && m.EntityType == AttachedEntityType.Document)
            from place in db.ResLinkMembers.Where(m => m.ResLinkId == link.Id && m.FeatureId != null)
            join map in db.RasterMaps on picture.EntityId equals map.DocumentId
            select new PlacePointRow
            {
                Id = link.Id,
                MapId = map.Id,
                DocumentId = map.DocumentId,
                PlaceId = place.FeatureId!.Value,
                Anchor = picture.Anchor,
                AnchorFileId = picture.AnchorFileId,
                CreatedAt = link.CreatedAt,
                UpdatedAt = link.UpdatedAt,
                DeletedAt = link.DeletedAt,
            };
    }

    /// <summary>The place-pin links among <paramref name="links"/>, whole or husk.</summary>
    public static IQueryable<ResLink> PlacePoints(SilexGisDbContext db, IQueryable<ResLink> links)
    {
        ArgumentNullException.ThrowIfNull(db);
        var relationIds = db.ResLinkRelationTypes
            .Where(r => r.Code == ResLinkRelationTypeSeeds.MapPlacePointCode)
            .Select(r => (long?)r.Id);
        return links.Where(l => relationIds.Contains(l.RelationTypeId));
    }

    /// <summary>The place-pin relation's row id, or null before it is seeded.</summary>
    public static Task<long?> RelationIdAsync(SilexGisDbContext db, CancellationToken ct = default) =>
        db.ResLinkRelationTypes.AsNoTracking()
            .Where(r => r.Code == ResLinkRelationTypeSeeds.MapPlacePointCode)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The stored pixel a pin's payload carries, or null when it carries none.</summary>
    public static (int X, int Y)? Pixel(PlacePointRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return PlacePointAnchor.Read(row.Anchor) is { } parsed ? (parsed.Px, parsed.Py) : null;
    }
}

/// <summary>The frame a phone measures pins in, read off a stored file.</summary>
public static class PictureFrames
{
    /// <summary>
    /// A file's frame: its size as it was read when it was stored — or, for a file stored before
    /// that was recorded, as its camera profile states it — and the code the renderer obeys with the
    /// turn recorded on it. Null when the size cannot be told.
    /// </summary>
    public static PictureFrame? Of(int? pixelWidth, int? pixelHeight, int? pixelOrientation, int quarterTurns, string? metadata)
    {
        var stated = PhotoExif.FromMetadata(metadata);
        var width = pixelWidth ?? stated.WidthPixels;
        var height = pixelHeight ?? stated.HeightPixels;
        return width is { } w && height is { } h && w > 0 && h > 0
            ? PictureFrame.Of(w, h, pixelOrientation ?? stated.Orientation, quarterTurns)
            : null;
    }

    public static PictureFrame? Of(StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Of(file.PixelWidth, file.PixelHeight, file.PixelOrientation, file.OrientationQuarterTurns, file.Metadata);
    }
}
