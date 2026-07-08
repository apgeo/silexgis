// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;

namespace SilexGis.Api.Features.Caves;

public sealed record CaveDto(
    Guid Id,
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
    GeoJsonPoint? MainGeom,
    bool ApproximateLocation,
    Guid OwnerUserId,
    Guid? TeamId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CaveListItemDto(
    Guid Id,
    string Name,
    string? IdentificationCode,
    long CaveTypeId,
    string? Region,
    decimal? SurveyedLength,
    decimal? Depth,
    ExplorationStatus ExplorationStatus,
    bool LocationProtected,
    int EntranceCount,
    GeoJsonPoint? MainGeom,
    bool ApproximateLocation,
    Visibility Visibility,
    DateTimeOffset UpdatedAt);

/// <summary>Create/update payload — server assigns identity, ownership and derived fields.</summary>
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
    Guid? TeamId,
    Visibility Visibility);

internal static class CaveMapping
{
    /// <summary>
    /// Maps to DTO applying location protection: without ViewExactLocation, the
    /// main geometry is grid-snapped and precise-location text fields are redacted.
    /// </summary>
    public static CaveDto ToDto(
        this Cave c, UserContext? user, double gridMeters, IReadOnlySet<Guid>? exactGrants = null)
    {
        var exact = LocationProtection.CanViewExactLocation(
            user, c, exactGrants != null && exactGrants.Contains(c.Id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None);
        return new CaveDto(
            c.Id, c.Name, c.OtherToponyms, c.IdentificationCode, c.CaveTypeId, c.Description,
            c.Website, c.Region, c.HydrographicBasin, c.Valley, c.TributaryRiver,
            exact ? c.ClosestAddress : null,
            exact ? c.LandRegistryNumber : null,
            exact ? c.LocationNotes : null,
            c.RockTypeId, c.RockAge,
            c.SurveyedLength, c.EstimatedLength, c.RealExtension, c.ProjectedExtension,
            c.Depth, c.PositiveDepth, c.NegativeDepth, c.PotentialDepth, c.Altitude,
            c.Volume, c.Area, c.RamificationIndex, c.CaveAge,
            c.ExplorationStatus, c.ProtectionClass, c.IsShowCave, c.ShowCaveLength,
            c.DiscoveryDate, c.Discoverer, c.LocationProtected, c.EntranceCount,
            MapGeom(c, exact, gridMeters),
            ApproximateLocation: !exact,
            c.OwnerUserId, c.TeamId, c.Visibility, c.CreatedAt, c.UpdatedAt);
    }

    public static CaveListItemDto ToListItem(
        this Cave c, UserContext? user, double gridMeters, IReadOnlySet<Guid>? exactGrants = null)
    {
        var exact = LocationProtection.CanViewExactLocation(
            user, c, exactGrants != null && exactGrants.Contains(c.Id) ? ObjectPermission.ViewExactLocation : ObjectPermission.None);
        return new CaveListItemDto(
            c.Id, c.Name, c.IdentificationCode, c.CaveTypeId, c.Region, c.SurveyedLength,
            c.Depth, c.ExplorationStatus, c.LocationProtected, c.EntranceCount,
            MapGeom(c, exact, gridMeters), ApproximateLocation: !exact, c.Visibility, c.UpdatedAt);
    }

    public static void Apply(this CaveWriteRequest request, Cave cave)
    {
        cave.Name = request.Name;
        cave.OtherToponyms = request.OtherToponyms;
        cave.IdentificationCode = request.IdentificationCode;
        cave.CaveTypeId = request.CaveTypeId;
        cave.Description = request.Description;
        cave.Website = request.Website;
        cave.Region = request.Region;
        cave.HydrographicBasin = request.HydrographicBasin;
        cave.Valley = request.Valley;
        cave.TributaryRiver = request.TributaryRiver;
        cave.ClosestAddress = request.ClosestAddress;
        cave.LandRegistryNumber = request.LandRegistryNumber;
        cave.LocationNotes = request.LocationNotes;
        cave.RockTypeId = request.RockTypeId;
        cave.RockAge = request.RockAge;
        cave.SurveyedLength = request.SurveyedLength;
        cave.EstimatedLength = request.EstimatedLength;
        cave.RealExtension = request.RealExtension;
        cave.ProjectedExtension = request.ProjectedExtension;
        cave.Depth = request.Depth;
        cave.PositiveDepth = request.PositiveDepth;
        cave.NegativeDepth = request.NegativeDepth;
        cave.PotentialDepth = request.PotentialDepth;
        cave.Altitude = request.Altitude;
        cave.Volume = request.Volume;
        cave.Area = request.Area;
        cave.RamificationIndex = request.RamificationIndex;
        cave.CaveAge = request.CaveAge;
        cave.ExplorationStatus = request.ExplorationStatus;
        cave.ProtectionClass = request.ProtectionClass;
        cave.IsShowCave = request.IsShowCave;
        cave.ShowCaveLength = request.ShowCaveLength;
        cave.DiscoveryDate = request.DiscoveryDate;
        cave.Discoverer = request.Discoverer;
        cave.LocationProtected = request.LocationProtected;
        cave.TeamId = request.TeamId;
        cave.Visibility = request.Visibility;
    }

    private static GeoJsonPoint? MapGeom(Cave c, bool exact, double gridMeters) =>
        c.MainGeom is null
            ? null
            : GeoJsonPoint.From(exact ? c.MainGeom : LocationProtection.Snap(c.MainGeom, gridMeters));
}
