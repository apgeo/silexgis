// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.FeatureShares;

public sealed record FeatureShareCreateRequest(FeatureShareMode Mode, bool IncludeSubtree = true);

/// <summary>
/// Mint response — the only moment the plaintext token exists in a response. It is never
/// stored (only its hash is), so it cannot be shown again; callers must copy it now.
/// </summary>
public sealed record FeatureShareCreatedDto(
    Guid Id,
    string Token,
    FeatureShareMode Mode,
    bool IncludeSubtree,
    DateTimeOffset CreatedAt);

/// <summary>Share metadata for management lists — deliberately token-free.</summary>
public sealed record FeatureShareDto(
    Guid Id,
    FeatureShareMode Mode,
    bool IncludeSubtree,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

/// <summary>
/// The resolved share payload: the typed feature envelope (kind + supertype row + the
/// matching subtype part) plus, when the share covers its subtree, the primary-chain
/// children as bare id/kind/name rows. Shape mirrors the feature resolver's envelope but
/// is declared locally — this response is served to anonymous callers and must never
/// grow a field by accident of another slice's evolution.
/// </summary>
public sealed record SharedFeatureEnvelopeDto(
    FeatureKind Kind,
    SharedFeatureDto Feature,
    SharedCaveDto? Cave,
    SharedEntranceDto? Entrance,
    SharedCenterlineDto? Centerline,
    IReadOnlyList<SharedFeatureChildDto>? Children);

/// <summary>
/// Supertype part of the shared envelope. <paramref name="ApproximateLocation"/> is true
/// when location protection was applied: point geometry is then grid-snapped and any
/// other geometry class is omitted entirely.
/// </summary>
public sealed record SharedFeatureDto(
    Guid Id,
    FeatureKind Kind,
    long? FeatureTypeId,
    FeatureCategory Category,
    string? Name,
    GeoJsonGeometry? Geometry,
    bool ApproximateLocation,
    string? Description,
    JsonElement Properties,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SharedCaveDto(
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

/// <summary>Altitude and position quality are protected fields — null under obfuscation.</summary>
public sealed record SharedEntranceDto(
    long EntranceTypeId,
    bool IsMain,
    decimal? Altitude,
    PositionQuality? PositionQuality,
    DateOnly? SurveyedAt);

public sealed record SharedCenterlineDto(
    bool IsDefault,
    decimal? LengthM,
    int PathCount,
    CenterlineSource Source);

/// <summary>Subtree rows carry identity only — never geometry or coordinates.</summary>
public sealed record SharedFeatureChildDto(Guid Id, FeatureKind Kind, string? Name);
