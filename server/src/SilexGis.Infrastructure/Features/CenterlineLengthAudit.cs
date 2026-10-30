// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// How many uploaded centrelines carry a stored length that is not the length of their passage.
/// Numbers only: the operator runs this against an installation that holds real data.
/// </summary>
/// <param name="Examined">Uploaded centrelines that have a stored length and a line geometry.</param>
/// <param name="Inflated">Stored length longer than the passage by more than the tolerance.</param>
/// <param name="Shorter">Stored length shorter than the passage by more than the tolerance.</param>
/// <param name="Agreeing">Stored length within the tolerance of the passage.</param>
/// <param name="NotMeasurable">Uploaded centrelines with no stored length or no line geometry.</param>
public sealed record CenterlineLengthCount(int Examined, int Inflated, int Shorter, int Agreeing, int NotMeasurable);

/// <summary>
/// Counts uploaded centrelines whose stored length was measured over the whole uploaded geometry
/// rather than over the passage it traces.
///
/// <para>
/// A survey export is mostly wall shots, and a length measured over every line in it reports a
/// cave several times as long as it is. Uploads now measure the inferred passage when the line
/// work arrives, but rows stored before that change keep the old figure until they are uploaded
/// again. This measures each one again the way an upload now does and compares; it changes
/// nothing. A centreline read out of a survey model is left out: those were always measured
/// over the traverse.
/// </para>
/// </summary>
public sealed class CenterlineLengthAudit(SilexGisDbContext db)
{
    /// <summary>
    /// The difference that counts as a different figure: half a metre, or one part in a hundred of
    /// the passage, whichever is larger. The stored figure is rounded to the centimetre and the two
    /// measurements are the same function over the same line, so anything within this is rounding.
    /// </summary>
    public static bool Differs(decimal stored, decimal measured) =>
        Math.Abs(stored - measured) > Math.Max(0.5m, measured * 0.01m);

    public async Task<CenterlineLengthCount> CountAsync(CancellationToken ct = default)
    {
        // Ids first and one geometry at a time after: a whole-system survey's geometry can run to
        // megabytes, and an installation can hold many.
        var ids = await db.Centerlines.IgnoreQueryFilters()
            .Where(c => c.Source == CenterlineSource.Uploaded)
            .Select(c => c.Id)
            .ToListAsync(ct);

        int inflated = 0, shorter = 0, agreeing = 0, notMeasurable = 0;
        foreach (var id in ids)
        {
            var row = await db.Centerlines.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.LengthM, c.Feature.Geom })
                .SingleAsync(ct);
            if (row.LengthM is not { } stored || row.Geom is not MultiLineString lines || lines.IsEmpty)
            {
                notMeasurable++;
                continue;
            }

            var measured = await CenterlineSql.GeodesicLengthMetersAsync(db, CenterlineSkeleton.Build3D(lines), ct);
            if (!Differs(stored, measured))
            {
                agreeing++;
            }
            else if (stored > measured)
            {
                inflated++;
            }
            else
            {
                shorter++;
            }
        }

        return new CenterlineLengthCount(inflated + shorter + agreeing, inflated, shorter, agreeing, notMeasurable);
    }
}
