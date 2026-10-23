// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>What a feature hit needs to have its context line written.</summary>
/// <param name="Id">The feature.</param>
/// <param name="Kind">Its kind: a cave speaks of its own region, anything else of its cave.</param>
/// <param name="TypeName">The feature type's display name, when it has one.</param>
/// <param name="AncestorIds">The containment ancestry, itself included.</param>
public sealed record FeatureContextRow(Guid Id, FeatureKind Kind, string? TypeName, Guid[] AncestorIds);

/// <summary>
/// The quieter second line under a feature in a suggestion list: what it is, and where it belongs.
/// </summary>
/// <remarks>
/// <para>
/// One home for both the object selector and the map's search box, so the two never disagree about
/// what may be said under the same name.
/// </para>
/// <para>
/// What it may say is narrow on purpose. A cave says its type and its region; anything else says
/// its type and the name and region of the cave it belongs to — but only a cave this reader may
/// read through the ordinary walk, asked for here rather than assumed because the hit itself was
/// readable. A private cave's name under a public entrance is exactly the disclosure the walk
/// exists to prevent. Never a coordinate, an altitude, an address or anything else positional: a
/// region names an area many caves share, which the cave registry shows every reader of the cave
/// whether or not they may place it, while an altitude or a locality narrows a protected cave down
/// to a hillside.
/// </para>
/// </remarks>
public static class FeatureContextLines
{
    private const string Separator = " · ";

    /// <summary>The context line per hit id; a hit with nothing to say has no entry.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ForAsync(
        SilexGisDbContext db, AccessContext caller, IReadOnlyCollection<FeatureContextRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // An entrance names its cave in a column of its own; the containment ancestry covers it
        // too once the hierarchy is written, but a freshly made entrance should not lose its line
        // waiting for that.
        var entranceIds = rows.Where(r => r.Kind == FeatureKind.CaveEntrance).Select(r => r.Id).ToArray();
        // Caves and entrances carry no feature type; what they are is their cave or entrance type.
        var entrances = entranceIds.Length == 0
            ? new Dictionary<Guid, (Guid Cave, string? Type)>()
            : await db.CaveEntrances.AsNoTracking()
                .Where(e => entranceIds.Contains(e.Id))
                .Select(e => new
                {
                    e.Id,
                    e.CaveFeatureId,
                    Type = db.EntranceTypes.Where(t => t.Id == e.EntranceTypeId).Select(t => t.Name).FirstOrDefault(),
                })
                .ToDictionaryAsync(e => e.Id, e => (Cave: e.CaveFeatureId, Type: (string?)e.Type), ct);

        var candidates = rows
            .SelectMany(r => r.AncestorIds.Append(r.Id))
            .Concat(entrances.Values.Select(e => e.Cave))
            .Distinct()
            .ToArray();

        var readableCaves = await db.Features.AsNoTracking()
            .VisibleTo(caller, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Cave && candidates.Contains(f.Id))
            .Select(f => new
            {
                f.Id,
                f.Name,
                Region = f.Cave != null ? f.Cave.Region : null,
                Type = db.CaveTypes.Where(t => f.Cave != null && t.Id == f.Cave.CaveTypeId)
                    .Select(t => t.Name).FirstOrDefault(),
            })
            .ToDictionaryAsync(f => f.Id, f => (f.Name, f.Region, f.Type), ct);

        var lines = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            string?[] parts;
            if (row.Kind == FeatureKind.Cave)
            {
                var known = readableCaves.TryGetValue(row.Id, out var own);
                // A cave's own classification ("Pit / Aven") says more than the type every cave
                // carries, so it comes first.
                parts = [(known ? own.Type : null) ?? row.TypeName, known ? own.Region : null];
            }
            else
            {
                var isEntrance = entrances.TryGetValue(row.Id, out var entrance);
                var typeName = row.TypeName ?? (isEntrance ? entrance.Type : null);
                var caveId = isEntrance && readableCaves.ContainsKey(entrance.Cave)
                    ? entrance.Cave
                    : row.AncestorIds
                        .Where(id => id != row.Id && readableCaves.ContainsKey(id))
                        .OrderBy(id => readableCaves[id].Name, StringComparer.Ordinal)
                        .ThenBy(id => id)
                        .Cast<Guid?>()
                        .FirstOrDefault();
                parts = caveId is { } cave
                    ? [typeName, readableCaves[cave].Name, readableCaves[cave].Region]
                    : [typeName];
            }

            var line = string.Join(Separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            if (line.Length > 0)
            {
                lines[row.Id] = line;
            }
        }

        return lines;
    }
}
