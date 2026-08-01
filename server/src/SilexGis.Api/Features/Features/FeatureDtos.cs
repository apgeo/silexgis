// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Features;

/// <summary>One step of the primary-parent chain (outermost ancestor first).</summary>
public sealed record FeatureBreadcrumbDto(Guid Id, string? Name);

/// <summary>
/// The kind-independent view of a feature row. Geometry is served under location
/// protection: for callers without exact view a point is grid-snapped
/// (<c>ApproximateLocation</c>) and any other geometry class is withheld entirely
/// (<c>OmittedLocation</c>) — extended geometry cannot be safely snapped.
/// </summary>
public sealed record FeatureDto(
    Guid Id,
    FeatureKind Kind,
    string? FeatureTypeCode,
    FeatureCategory Category,
    string? Name,
    string? Description,
    GeoJsonGeometry? Geometry,
    JsonElement Properties,
    bool LocationProtected,
    bool ApproximateLocation,
    bool OmittedLocation,
    IReadOnlyList<FeatureBreadcrumbDto> Parents,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FeatureListItemDto(
    Guid Id,
    FeatureKind Kind,
    string? FeatureTypeCode,
    FeatureCategory Category,
    string? Name,
    GeoJsonGeometry? Geometry,
    bool LocationProtected,
    bool ApproximateLocation,
    bool OmittedLocation,
    Visibility Visibility,
    DateTimeOffset UpdatedAt);

/// <summary>Cave subtype attributes (identity, access and geometry live on the feature).</summary>
public sealed record FeatureCaveDto(
    string? OtherToponyms,
    string? IdentificationCode,
    long CaveTypeId,
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
    int EntranceCount);

public sealed record FeatureEntranceDto(
    Guid CaveFeatureId,
    long EntranceTypeId,
    bool IsMain,
    decimal? Altitude,
    PositionQuality PositionQuality,
    DateOnly? SurveyedAt);

public sealed record FeatureCenterlineDto(
    Guid CaveFeatureId,
    Guid? SurveyModelId,
    bool IsDefault,
    decimal? LengthM,
    int PathCount,
    int? SkeletonPathCount,
    CenterlineSource Source);

/// <summary>
/// The typed resolver envelope: the common feature view plus the subtype attributes
/// matching <paramref name="Kind"/> (exactly one of the optional parts is set for
/// subtyped kinds; all are null for generic kinds).
/// </summary>
public sealed record FeatureEnvelopeDto(
    FeatureKind Kind,
    FeatureDto Feature,
    FeatureCaveDto? Cave,
    FeatureEntranceDto? Entrance,
    FeatureCenterlineDto? Centerline);

/// <summary>One requested containment edge.</summary>
public sealed record ParentEdgeRequest(Guid ParentId, bool IsPrimary);

/// <summary>
/// Create payload — server assigns identity and ownership. Only generic kinds are
/// created here; subtyped kinds (caves, entrances, centerlines) have typed endpoints.
/// </summary>
public sealed record FeatureCreateRequest(
    FeatureKind Kind,
    string? Name,
    long FeatureTypeId,
    GeoJsonGeometry? Geometry,
    string? Description,
    JsonElement? Properties,
    IReadOnlyList<ParentEdgeRequest>? Parents,
    bool LocationProtected,
    Guid? CavingGroupId,
    Visibility Visibility);

/// <summary>Full-update payload (generic kinds only). Containment edges are edited via the parents endpoint.</summary>
public sealed record FeatureUpdateRequest(
    string? Name,
    long FeatureTypeId,
    GeoJsonGeometry? Geometry,
    string? Description,
    JsonElement? Properties,
    bool LocationProtected,
    Guid? CavingGroupId,
    Visibility Visibility);

/// <summary>Full replacement of a feature's containment edges.</summary>
public sealed record SetParentsRequest(IReadOnlyList<ParentEdgeRequest> Parents);

public sealed record FeatureParentDto(Guid Id, string? Name, bool IsPrimary);

public sealed record FeatureChildDto(Guid Id, FeatureKind Kind, string? Name, bool IsPrimary);

/// <summary>A typed association row; one endpoint is always the requested feature.</summary>
public sealed record FeatureLinkDto(Guid FromId, Guid ToId, string LinkKindCode, string? Note);

public sealed record FeatureLinkWriteRequest(Guid ToId, string LinkKindCode, string? Note);

/// <summary>Full replacement of the feature's outgoing links.</summary>
public sealed record SetLinksRequest(IReadOnlyList<FeatureLinkWriteRequest> Links);

internal static class FeatureMapping
{
    /// <summary>
    /// How a protected row presents to a caller without exact view. Subtyped kinds have a
    /// fixed policy (cave/entrance points snap; centerline rows never reach display
    /// paths non-exactly — they are withheld before mapping); generic kinds carry theirs
    /// on the feature type. An unknown type withholds — fail closed.
    /// </summary>
    public static ProtectedDisplay DisplayPolicy(Feature f, FeatureType? featureType) => f.Kind switch
    {
        FeatureKind.Cave or FeatureKind.CaveEntrance => ProtectedDisplay.SnapPoint,
        FeatureKind.Generic => featureType?.ProtectedDisplay ?? ProtectedDisplay.Withhold,
        _ => ProtectedDisplay.Withhold,
    };

    /// <summary>
    /// Applies location protection to the geometry a caller is shown: exact view passes
    /// through; otherwise a plain point snaps to the obfuscation grid (only a point can
    /// be safely snapped — anything else leaks shape or extent) and every other class,
    /// or a withhold-policy kind, is omitted entirely.
    /// </summary>
    public static (GeoJsonGeometry? Geometry, bool Approximate, bool Omitted) DisplayGeometry(
        Feature f, ProtectedDisplay policy, bool exact, double gridMeters)
    {
        if (f.Geom is null)
        {
            return (null, false, false);
        }

        if (exact)
        {
            return (GeoJsonGeometry.From(f.Geom), false, false);
        }

        var geometryClass = GeometryClasses.Of(f.Geom);
        if (policy == ProtectedDisplay.SnapPoint
            && geometryClass is not null && GeometryClasses.CanSnap(geometryClass.Value))
        {
            return (GeoJsonGeometry.From(LocationProtection.Snap((Point)f.Geom, gridMeters)), true, false);
        }

        return (null, false, true);
    }

    public static FeatureDto ToDto(
        this Feature f,
        string? featureTypeCode,
        ProtectedDisplay policy,
        bool exact,
        double gridMeters,
        IReadOnlyList<FeatureBreadcrumbDto> parents)
    {
        var (geometry, approximate, omitted) = DisplayGeometry(f, policy, exact, gridMeters);
        return new FeatureDto(
            f.Id,
            f.Kind,
            featureTypeCode,
            f.Category,
            f.Name,
            f.Description,
            geometry,
            JsonSerializer.Deserialize<JsonElement>(f.Properties),
            f.LocationProtected,
            approximate,
            omitted,
            parents,
            f.OwnerUserId,
            f.CavingGroupId,
            f.Visibility,
            f.CreatedAt,
            f.UpdatedAt);
    }

    public static FeatureListItemDto ToListItem(
        this Feature f, string? featureTypeCode, ProtectedDisplay policy, bool exact, double gridMeters)
    {
        var (geometry, approximate, omitted) = DisplayGeometry(f, policy, exact, gridMeters);
        return new FeatureListItemDto(
            f.Id,
            f.Kind,
            featureTypeCode,
            f.Category,
            f.Name,
            geometry,
            f.LocationProtected,
            approximate,
            omitted,
            f.Visibility,
            f.UpdatedAt);
    }

    /// <summary>Without exact view the precise-location text fields are redacted.</summary>
    public static FeatureCaveDto ToCaveDto(this Cave c, bool exact) => new(
        c.OtherToponyms,
        c.IdentificationCode,
        c.CaveTypeId,
        c.Website,
        c.Region,
        c.HydrographicBasin,
        c.Valley,
        c.TributaryRiver,
        exact ? c.ClosestAddress : null,
        exact ? c.LandRegistryNumber : null,
        exact ? c.LocationNotes : null,
        c.RockTypeId,
        c.RockAge,
        c.SurveyedLength,
        c.EstimatedLength,
        c.RealExtension,
        c.ProjectedExtension,
        c.Depth,
        c.PositiveDepth,
        c.NegativeDepth,
        c.PotentialDepth,
        c.Altitude,
        c.Volume,
        c.Area,
        c.RamificationIndex,
        c.CaveAge,
        c.ExplorationStatus,
        c.ProtectionClass,
        c.IsShowCave,
        c.ShowCaveLength,
        c.DiscoveryDate,
        c.Discoverer,
        c.EntranceCount);

    /// <summary>Without exact view the altitude and position quality are redacted (they refine the snapped point).</summary>
    public static FeatureEntranceDto ToEntranceDto(this CaveEntrance e, bool exact) => new(
        e.CaveFeatureId,
        e.EntranceTypeId,
        e.IsMain,
        exact ? e.Altitude : null,
        exact ? e.PositionQuality : PositionQuality.Unknown,
        e.SurveyedAt);

    public static FeatureCenterlineDto ToCenterlineDto(this Centerline c) => new(
        c.CaveFeatureId,
        c.SurveyModelId,
        c.IsDefault,
        c.LengthM,
        c.PathCount,
        c.SkeletonPathCount,
        c.Source);
}
