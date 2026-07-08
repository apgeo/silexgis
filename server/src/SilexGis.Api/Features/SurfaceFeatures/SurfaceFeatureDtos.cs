// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.SurfaceFeatures;

public sealed record SurfaceFeatureDto(
    Guid Id,
    string? Name,
    long FeatureTypeId,
    GeoJsonGeometry Geometry,
    string? Description,
    JsonElement Properties,
    Guid? CaveId,
    Guid OwnerUserId,
    Guid? TeamId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Create/update payload — server assigns identity and ownership.</summary>
public sealed record SurfaceFeatureWriteRequest(
    string? Name,
    long FeatureTypeId,
    GeoJsonGeometry Geometry,
    string? Description,
    JsonElement? Properties,
    Guid? CaveId,
    Guid? TeamId,
    Visibility Visibility);

internal static class SurfaceFeatureMapping
{
    public static SurfaceFeatureDto ToDto(this SurfaceFeature f) => new(
        f.Id,
        f.Name,
        f.FeatureTypeId,
        GeoJsonGeometry.From(f.Geom),
        f.Description,
        JsonSerializer.Deserialize<JsonElement>(f.Properties),
        f.CaveId,
        f.OwnerUserId,
        f.TeamId,
        f.Visibility,
        f.CreatedAt,
        f.UpdatedAt);

    /// <summary>Applies the write payload; ownership and identity are never touched here.</summary>
    public static void Apply(this SurfaceFeatureWriteRequest r, SurfaceFeature f, NetTopologySuite.Geometries.Geometry geom)
    {
        f.Name = r.Name;
        f.FeatureTypeId = r.FeatureTypeId;
        f.Geom = geom;
        f.Description = r.Description;
        f.Properties = r.Properties is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : "{}";
        f.CaveId = r.CaveId;
        f.TeamId = r.TeamId;
        f.Visibility = r.Visibility;
    }
}
