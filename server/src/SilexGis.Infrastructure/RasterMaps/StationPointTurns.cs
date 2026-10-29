// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.RasterMaps;

/// <summary>
/// The survey stations marked on a scanned map, carried through a turn recorded on the map's picture.
/// </summary>
/// <remarks>
/// A station point is stored in fractions of the picture as it is drawn, and a turn recorded in the
/// browser changes how it is drawn. Left alone, every point marked before the turn would mark another
/// spot after it; so the points measured on the turned file are turned with it, and still mark the
/// spot they were placed on. A place a phone pinned is redrawn from its stored pixel instead, by the
/// place-pin store.
/// </remarks>
public static class StationPointTurns
{
    /// <summary>The station points measured on these files, turned clockwise by <paramref name="quarterTurns"/>. Tracked, not saved.</summary>
    public static async Task TurnAsync(
        SilexGisDbContext db, IReadOnlyCollection<Guid> fileIds, int quarterTurns, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fileIds);
        if (fileIds.Count == 0 || PhotoOrientation.Normalize(quarterTurns) == 0)
        {
            return;
        }

        var members = await (
                from member in db.ResLinkMembers
                join link in db.ResLinks on member.ResLinkId equals link.Id
                join relation in db.ResLinkRelationTypes on link.RelationTypeId equals (long?)relation.Id
                where relation.Code == ResLinkRelationTypeSeeds.MapStationPointCode
                    && member.AnchorKind == AnchorKind.ImageRegion
                    && member.AnchorFileId != null && fileIds.Contains(member.AnchorFileId.Value)
                select member)
            .ToListAsync(ct);
        foreach (var member in members)
        {
            if (JsonNode.Parse(member.Anchor ?? "null") is not JsonObject anchor
                || anchor["shape"] is not JsonValue shape || !shape.TryGetValue<string>(out var kind) || kind != "point"
                || anchor["x"] is not JsonValue x || !x.TryGetValue<double>(out var fx)
                || anchor["y"] is not JsonValue y || !y.TryGetValue<double>(out var fy))
            {
                continue;
            }

            var (tx, ty) = ImageRegionTurn.TurnPoint(fx, fy, quarterTurns);
            anchor["x"] = tx;
            anchor["y"] = ty;
            member.Anchor = anchor.ToJsonString();
        }
    }
}
