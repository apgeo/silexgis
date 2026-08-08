// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Geofiles;

public sealed record GeofileDto(
    Guid Id,
    string Name,
    string? Description,
    Guid FileId,
    GeofileFormat Format,
    int? Srid,
    GeofileImportStatus ImportStatus,
    string? ImportError,
    int FeatureCount,
    GeoJsonGeometry? Bbox,
    JsonElement? Style,
    GeofileSourceOptions? SourceOptions,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The header of a delimited upload, for the screen that says which column is which.</summary>
public sealed record GeofileColumnsDto(IReadOnlyList<string> Columns);

/// <summary>
/// Re-read the upload, optionally under corrected source options. Omitting them re-reads with
/// what is already stored — the way a failed import is retried after the cause was elsewhere.
/// </summary>
public sealed record GeofileReimportRequest(GeofileSourceOptions? SourceOptions);

/// <summary>Lightweight polling target while the import job runs.</summary>
public sealed record GeofileStatusDto(
    Guid Id,
    GeofileImportStatus ImportStatus,
    string? ImportError,
    int FeatureCount);

/// <summary>Metadata update — the uploaded file itself is immutable.</summary>
public sealed record GeofileUpdateRequest(
    string Name,
    string? Description,
    JsonElement? Style,
    Guid? CavingGroupId,
    Visibility Visibility);

internal static class GeofileMapping
{
    public static GeofileDto ToDto(this Geofile g) => new(
        g.Id,
        g.Name,
        g.Description,
        g.FileId,
        g.Format,
        g.Srid,
        g.ImportStatus,
        g.ImportError,
        g.FeatureCount,
        g.Bbox is null ? null : GeoJsonGeometry.From(g.Bbox),
        g.Style is null ? null : JsonSerializer.Deserialize<JsonElement>(g.Style),
        ReadSourceOptions(g),
        g.OwnerUserId,
        g.CavingGroupId,
        g.Visibility,
        g.CreatedAt,
        g.UpdatedAt);

    public static GeofileStatusDto ToStatusDto(this Geofile g) =>
        new(g.Id, g.ImportStatus, g.ImportError, g.FeatureCount);

    /// <summary>
    /// Parse options as stored. Unreadable JSON reads as none rather than throwing: the list
    /// endpoint maps every row the caller can see, and one mangled blob must not take the
    /// whole page down with it.
    /// </summary>
    private static GeofileSourceOptions? ReadSourceOptions(Geofile geofile)
    {
        if (string.IsNullOrWhiteSpace(geofile.SourceOptions))
        {
            return null;
        }

        try
        {
            return ImportJson.Deserialize<GeofileSourceOptions>(geofile.SourceOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
