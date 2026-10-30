// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Export;

/// <summary>
/// The record of a vector export (GeoJSON, GPX, KML, CSV, shapefile): who took which rows out of
/// the installation, and which of them left with a protected position treated.
///
/// <para>
/// Auditing rides a save interceptor that notices changed rows, and an export changes none, so
/// without this a file that outlives every permission check that produced it left no trace at
/// all. The row is added by hand, before the bytes are handed over and in the same request, so
/// there is no window in which a file exists and the record of who took it does not.
/// </para>
/// <para>
/// It carries counts for every row and ids only for the rows whose position was treated — the
/// snapped ones and the withheld ones — because the question asked afterwards is "which rows left
/// without their true position, and which did not leave at all", and the exact majority of a large
/// file is answered by a count. Never a coordinate: an audit row that held the true position of
/// a protected cave would be a second, unprotected copy of it.
/// </para>
/// </summary>
internal static class VectorExportAudit
{
    /// <summary>The one entity type every vector export is recorded under; the layer tells them apart.</summary>
    public const string EntityType = "VectorExport";

    /// <summary>
    /// Adds the row and saves it. <paramref name="exported"/> is every row that went into the
    /// file; the other two lists are subsets of the rows considered: those written at the
    /// protection grid instead of their true position, and those left out of the file because
    /// no safe form of them exists.
    /// </summary>
    public static async Task RecordAsync(
        SilexGisDbContext db,
        ICurrentUser currentUser,
        string layer,
        string format,
        int exported,
        IReadOnlyCollection<Guid> snapped,
        IReadOnlyCollection<Guid> withheld,
        Guid? savedFilterId,
        bool filtered,
        IReadOnlyDictionary<string, object?>? extra,
        CancellationToken ct)
    {
        var detail = new Dictionary<string, object?>
        {
            ["layer"] = layer,
            ["format"] = format.ToLowerInvariant(),
            ["exportedCount"] = exported,
            ["snappedCount"] = snapped.Count,
            ["snappedIds"] = Sorted(snapped),
            ["withheldCount"] = withheld.Count,
            ["withheldIds"] = Sorted(withheld),
            ["filtered"] = filtered,
            ["savedFilterId"] = savedFilterId?.ToString(),
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            detail[key] = value;
        }

        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.Exported,
            EntityType = EntityType,
            // Its own id, so a record of an export is one row and not a history of something.
            EntityId = Guid.CreateVersion7().ToString(),
            Changes = JsonSerializer.Serialize(detail),
        });
        await db.SaveChangesAsync(ct);
    }

    private static List<string> Sorted(IEnumerable<Guid> ids) =>
        [.. ids.Select(id => id.ToString()).Order(StringComparer.Ordinal)];
}
