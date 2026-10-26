// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Sync;

/// <summary>One raster map a phone asks this server to write.</summary>
/// <param name="Id">The phone's identifier for the map, adopted verbatim the first time it arrives.</param>
/// <param name="BaseRevision">
/// The server revision the phone last saw for this map, or null to say it is new. Compared for
/// equality and nothing else, exactly as a feature row's is.
/// </param>
/// <param name="Deleted">Asks for the map to go. Arbitrated exactly like an edit.</param>
/// <param name="CaveId">The cave the map is of; required unless the map is being removed.</param>
/// <param name="CaveAreaId">The part of that cave the map covers, when it covers one part.</param>
/// <param name="Title">The map's title as the phone shows it; required unless the map is being removed.</param>
/// <param name="MapType">What the map shows; required unless the map is being removed.</param>
/// <param name="SortOrder">Where the map sits among the cave's maps on the phone.</param>
/// <param name="FileName">The picture's file name on the phone; the staged transfer's own name otherwise.</param>
/// <param name="Sha256">
/// The lowercase hex SHA-256 of the picture. Required on a new map, which always brings its picture;
/// a map already here keeps the picture it has.
/// </param>
/// <param name="SizeBytes">The picture's size, checked against the staged bytes when stated.</param>
/// <param name="StagedUploadId">The staged transfer holding the picture; required on a new map.</param>
/// <param name="ClientUpdatedAt">When the phone believes it last wrote the map, by its own clock. Provenance only.</param>
public sealed record SyncUploadRasterMapDto(
    Guid Id,
    DateTimeOffset? BaseRevision = null,
    bool Deleted = false,
    Guid? CaveId = null,
    Guid? CaveAreaId = null,
    string? Title = null,
    RasterMapType? MapType = null,
    int? SortOrder = null,
    string? FileName = null,
    string? Sha256 = null,
    long? SizeBytes = null,
    Guid? StagedUploadId = null,
    DateTimeOffset? ClientUpdatedAt = null);

/// <summary>One pin on a raster map that a phone asks this server to write.</summary>
/// <param name="Id">The phone's identifier for the pin, adopted verbatim.</param>
/// <param name="RasterMapId">
/// The map the pin is on. A pin belongs to one map for good: the same identifier sent under another
/// map is refused as taken.
/// </param>
/// <param name="PlaceId">The place pinned; required unless the pin is being removed.</param>
/// <param name="X">Pixels from the left edge of the picture; required unless the pin is being removed.</param>
/// <param name="Y">Pixels from the top edge of the picture; required unless the pin is being removed.</param>
public sealed record SyncUploadRasterPinDto(
    Guid Id,
    Guid RasterMapId,
    DateTimeOffset? BaseRevision = null,
    bool Deleted = false,
    Guid? PlaceId = null,
    int? X = null,
    int? Y = null);

/// <summary>One raster map as the rasters stream hands it, with its pins embedded.</summary>
/// <param name="DocumentId">
/// The picture, as a document of the documents stream. It reaches the phone through that stream's
/// catalogue and the file links route, under the same reach rule as any other photo.
/// </param>
/// <param name="Revision">
/// The map's own server revision — the value the phone sends back as its base revision when it
/// edits the map. Its pins carry revisions of their own.
/// </param>
/// <param name="Pins">
/// Every live pin on the map that this reader may be given. The list is otherwise complete, so a
/// phone replaces its copy of the map's pins wholesale. A pin of a place this reader is not handed
/// is left out rather than sent without its place.
/// </param>
public sealed record SyncRasterMapDto(
    Guid Id,
    Guid CaveId,
    Guid? CaveAreaId,
    string Title,
    RasterMapType MapType,
    int SortOrder,
    Guid DocumentId,
    DateTimeOffset Revision,
    IReadOnlyList<SyncRasterPinDto> Pins);

/// <summary>One pin on a raster map, as the rasters stream hands it.</summary>
public sealed record SyncRasterPinDto(Guid Id, Guid PlaceId, int X, int Y, DateTimeOffset Revision);

/// <summary>
/// One page of the rasters stream: the raster maps of the caves the set hands this reader, and the
/// maps that have gone, on one cursor.
/// </summary>
public sealed record SyncRastersPageDto(
    long SetRevision,
    JsonElement Settings,
    IReadOnlyList<SyncRasterMapDto> Maps,
    IReadOnlyList<SyncTombstoneDto> Tombstones,
    string? NextCursor,
    bool HasMore);
