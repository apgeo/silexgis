// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>One containment parent of the cave, for breadcrumbs (primary edge first).</summary>
public sealed record CaveParentDto(Guid Id, string? Name, bool IsPrimary);

public sealed record CaveDto(
    Guid Id,
    FeatureKind Kind,
    string Name,
    string? OtherToponyms,
    string? IdentificationCode,
    long CaveTypeId,
    string? Description,
    string? Website,
    string? Region,
    string? HydrographicBasin,
    string? Valley,
    string? TributaryRiver,
    string? ClosestAddress,
    string? LandRegistryNumber,
    string? LocationNotes,
    long? RockTypeId,
    string? RockAge,
    decimal? SurveyedLength,
    decimal? EstimatedLength,
    decimal? RealExtension,
    decimal? ProjectedExtension,
    decimal? Depth,
    decimal? PositiveDepth,
    decimal? NegativeDepth,
    decimal? PotentialDepth,
    decimal? Altitude,
    decimal? Volume,
    decimal? Area,
    decimal? RamificationIndex,
    int? CaveAge,
    ExplorationStatus ExplorationStatus,
    string? ProtectionClass,
    bool IsShowCave,
    decimal? ShowCaveLength,
    string? DiscoveryDate,
    string? Discoverer,
    bool LocationProtected,
    int EntranceCount,
    JsonElement Properties,
    GeoJsonPoint? Geom,
    bool ApproximateLocation,
    IReadOnlyList<CaveParentDto> Parents,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CaveListItemDto(
    Guid Id,
    FeatureKind Kind,
    string Name,
    string? IdentificationCode,
    long CaveTypeId,
    string? Region,
    decimal? SurveyedLength,
    decimal? Depth,
    ExplorationStatus ExplorationStatus,
    bool LocationProtected,
    int EntranceCount,
    GeoJsonPoint? Geom,
    bool ApproximateLocation,
    Visibility Visibility,
    DateTimeOffset UpdatedAt);

/// <summary>The cave's main entrance as shown on the detail header (location-protected).</summary>
public sealed record CaveMainEntranceDto(
    Guid Id,
    string? Name,
    GeoJsonPoint? Geom,
    bool ApproximateLocation);

/// <summary>What the caller may do with the cave — UI capability hints, not enforcement.</summary>
public sealed record CavePermissionsDto(
    bool CanWrite,
    bool CanDelete,
    bool CanShare,
    bool CanManagePermissions,
    bool CanViewExactLocation);

/// <summary>
/// The one picture that stands for a cave wherever it is named — a list row, a card, the panel
/// beside the map.
/// </summary>
/// <param name="ThumbnailUrl">
/// A rendering, and only ever a rendering: the token minted for it does not open the upload. A
/// headline picture is shown to everybody who may see the cave at all, so it must not be a way
/// to obtain bytes that the picture's own read rule would have withheld.
/// </param>
public sealed record CaveHeadlinePictureDto(
    Guid AttachmentId,
    Guid DocumentId,
    Guid FileId,
    string ThumbnailUrl,
    string? Caption);

/// <param name="HeadlinePicture">
/// Null both when nobody has chosen one and when the chosen one is not this caller's to see —
/// the same two rules that filter <paramref name="AttachmentCount"/>. A headline that survived
/// them would pair a cave with a photograph the caller was not to be told about, which is the
/// pairing itself and not merely the picture.
/// </param>
public sealed record CaveSummaryDto(
    Guid Id,
    string Name,
    int EntranceCount,
    int CenterlineCount,
    int SurveyModelCount,
    int AttachmentCount,
    int TripLogCount,
    CaveMainEntranceDto? MainEntrance,
    CavePermissionsDto Permissions,
    CaveHeadlinePictureDto? HeadlinePicture);

/// <summary>
/// Create/update payload — server assigns identity, ownership and derived fields.
/// <c>ParentId</c> places a new cave under a containing feature (karst area, system) on
/// create only; hierarchy edits afterwards go through the feature hierarchy routes.
/// </summary>
public sealed record CaveWriteRequest(
    string Name,
    string? OtherToponyms,
    string? IdentificationCode,
    long CaveTypeId,
    string? Description,
    string? Website,
    string? Region,
    string? HydrographicBasin,
    string? Valley,
    string? TributaryRiver,
    string? ClosestAddress,
    string? LandRegistryNumber,
    string? LocationNotes,
    long? RockTypeId,
    string? RockAge,
    decimal? SurveyedLength,
    decimal? EstimatedLength,
    decimal? RealExtension,
    decimal? ProjectedExtension,
    decimal? Depth,
    decimal? PositiveDepth,
    decimal? NegativeDepth,
    decimal? PotentialDepth,
    decimal? Altitude,
    decimal? Volume,
    decimal? Area,
    decimal? RamificationIndex,
    int? CaveAge,
    ExplorationStatus ExplorationStatus,
    string? ProtectionClass,
    bool IsShowCave,
    decimal? ShowCaveLength,
    string? DiscoveryDate,
    string? Discoverer,
    bool LocationProtected,
    JsonElement? Properties,
    Guid? ParentId,
    Guid? CavingGroupId,
    Visibility Visibility);

internal static class CaveMapping
{
    /// <summary>
    /// Maps the cave aggregate (feature row + cave subtype row) applying location protection:
    /// without exact view the main-entrance point is grid-snapped and precise-location text
    /// fields are redacted.
    /// </summary>
    /// <param name="c">
    /// The subtype row, passed rather than read off the feature's navigation on purpose. Only
    /// one direction of the pair is guarded by the database — the subtype row carries a
    /// composite foreign key on identifier and kind, so it cannot exist without its feature,
    /// while a feature that has lost its subtype row is a state nothing refuses. Reading the
    /// navigation here would turn such a row into a null dereference inside a projection, which
    /// is a server error for whoever asked; taking the row as an argument makes every caller
    /// decide what to do about a missing one where it still has an answer to give.
    /// </param>
    public static CaveDto ToDto(
        this Feature f, Cave c, bool exact, double gridMeters, IReadOnlyList<CaveParentDto> parents)
    {
        return new CaveDto(
            f.Id, f.Kind, f.Name ?? string.Empty, c.OtherToponyms, c.IdentificationCode,
            c.CaveTypeId, f.Description, c.Website, c.Region, c.HydrographicBasin,
            c.Valley, c.TributaryRiver,
            exact ? c.ClosestAddress : null,
            exact ? c.LandRegistryNumber : null,
            exact ? c.LocationNotes : null,
            c.RockTypeId, c.RockAge,
            c.SurveyedLength, c.EstimatedLength, c.RealExtension, c.ProjectedExtension,
            // Altitude is a position, not a dimension, and belongs with the fields above it rather
            // than with the lengths and depths beside it. It is the elevation of the main entrance:
            // in karst terrain, held against a snapped point and a named region, it narrows a
            // search from a hillside to a contour. The entrance route has always withheld it from a
            // reader who may not place the cave exactly; the cave route handed out the same number
            // to everyone, so asking about the cave answered what asking about its entrance would
            // not.
            c.Depth, c.PositiveDepth, c.NegativeDepth, c.PotentialDepth,
            exact ? c.Altitude : null,
            c.Volume, c.Area, c.RamificationIndex, c.CaveAge,
            c.ExplorationStatus, c.ProtectionClass, c.IsShowCave, c.ShowCaveLength,
            c.DiscoveryDate, c.Discoverer, f.LocationProtected, c.EntranceCount,
            JsonSerializer.Deserialize<JsonElement>(f.Properties),
            MapGeom(f.Geom, exact, gridMeters),
            ApproximateLocation: !exact,
            parents,
            f.OwnerUserId, f.CavingGroupId, f.Visibility, f.CreatedAt, f.UpdatedAt);
    }

    /// <summary>
    /// One row of the cave listing. The subtype row is a parameter for the reason spelled out on
    /// the single-cave mapping above: a listing that dereferenced a missing one would answer the
    /// whole page with a server error, for every reader of the archive rather than for whoever
    /// owns the broken row.
    /// </summary>
    public static CaveListItemDto ToListItem(this Feature f, Cave c, bool exact, double gridMeters)
    {
        return new CaveListItemDto(
            f.Id, f.Kind, f.Name ?? string.Empty, c.IdentificationCode, c.CaveTypeId,
            c.Region, c.SurveyedLength, c.Depth, c.ExplorationStatus, f.LocationProtected,
            c.EntranceCount, MapGeom(f.Geom, exact, gridMeters),
            ApproximateLocation: !exact, f.Visibility, f.UpdatedAt);
    }

    /// <summary>
    /// Applies the write payload onto the aggregate's two rows. Never touched here:
    /// identity, ownership, and <c>LocationProtected</c> — flipping a protection root
    /// restamps effective protection over the whole subtree and must go through the
    /// feature write service.
    /// </summary>
    public static void Apply(this CaveWriteRequest r, Feature f, Cave c)
    {
        f.Name = r.Name;
        f.Description = r.Description;
        f.Properties = r.Properties is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : "{}";
        f.CavingGroupId = r.CavingGroupId;
        f.Visibility = r.Visibility;
        c.OtherToponyms = r.OtherToponyms;
        c.IdentificationCode = r.IdentificationCode;
        c.CaveTypeId = r.CaveTypeId;
        c.Website = r.Website;
        c.Region = r.Region;
        c.HydrographicBasin = r.HydrographicBasin;
        c.Valley = r.Valley;
        c.TributaryRiver = r.TributaryRiver;
        c.ClosestAddress = r.ClosestAddress;
        c.LandRegistryNumber = r.LandRegistryNumber;
        c.LocationNotes = r.LocationNotes;
        c.RockTypeId = r.RockTypeId;
        c.RockAge = r.RockAge;
        c.SurveyedLength = r.SurveyedLength;
        c.EstimatedLength = r.EstimatedLength;
        c.RealExtension = r.RealExtension;
        c.ProjectedExtension = r.ProjectedExtension;
        c.Depth = r.Depth;
        c.PositiveDepth = r.PositiveDepth;
        c.NegativeDepth = r.NegativeDepth;
        c.PotentialDepth = r.PotentialDepth;
        c.Altitude = r.Altitude;
        c.Volume = r.Volume;
        c.Area = r.Area;
        c.RamificationIndex = r.RamificationIndex;
        c.CaveAge = r.CaveAge;
        c.ExplorationStatus = r.ExplorationStatus;
        c.ProtectionClass = r.ProtectionClass;
        c.IsShowCave = r.IsShowCave;
        c.ShowCaveLength = r.ShowCaveLength;
        c.DiscoveryDate = r.DiscoveryDate;
        c.Discoverer = r.Discoverer;
    }

    /// <summary>
    /// A cave feature's geometry is its main-entrance point cache; snapped for callers
    /// without exact view. Anything that is not a point is never emitted here — extended
    /// geometry cannot be safely snapped.
    /// </summary>
    public static GeoJsonPoint? MapGeom(Geometry? geom, bool exact, double gridMeters) =>
        geom is Point point
            ? GeoJsonPoint.From(exact ? point : LocationProtection.Snap(point, gridMeters))
            : null;
}
