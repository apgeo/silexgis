// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// The attention flags of a page of trips, read once for the page and shaped for the trip answer.
/// </summary>
/// <remarks>
/// The reasons are read with the same parse the conversion wrote them with, so an unreadable value
/// reads as no reasons rather than failing the trip. Only codes and counts are handed on: the
/// scans' own identifiers locate nothing but are of no use to a reader, and nothing here is a
/// station or a place.
/// </remarks>
internal static class TripAttentionReads
{
    public static async Task<Dictionary<Guid, TripAttentionDto>> ForAsync(
        SilexGisDbContext db,
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken ct)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        var rows = await db.TripAttentions.AsNoTracking()
            .Where(a => tripIds.Contains(a.TripLogId))
            .ToListAsync(ct);
        return rows.ToDictionary(
            a => a.TripLogId,
            a => new TripAttentionDto(
                a.IsRaised,
                a.RaisedAt,
                a.ClearedAt,
                [.. TripRecordingConversionRules.Parse(a.Reasons)
                    .Select(r => new TripAttentionReasonDto(r.Code, r.Count))]));
    }
}
