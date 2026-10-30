// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <param name="Before">What the count said before anything was rewritten.</param>
/// <param name="Applied">True when the rewrite was committed. A dry run and a refused rewrite are false.</param>
/// <param name="Updated">Stored lengths rewritten inside the transaction (also reported for a dry run, which rolls them back).</param>
/// <param name="After">What the count said inside the transaction once the rewrite was done.</param>
public sealed record CenterlineLengthRepairResult(
    CenterlineLengthCount Before, bool Applied, int Updated, CenterlineLengthCount After);

/// <summary>
/// Rewrites, in place, the stored length of every uploaded centreline whose figure is not the
/// length of the passage it traces (see <see cref="CenterlineLengthAudit"/> for how the two
/// differ). Each row is measured the way an upload now measures it, and only the length column
/// is written.
///
/// <para>
/// Safe to run on an installation that holds real data and safe to run again: no row is added,
/// removed or moved, a row whose figure already agrees is not written, and a second run finds
/// nothing to do. Three kinds of row are left exactly as they are: a centreline read out of a
/// survey model (always measured over the traverse, and its stored geometry holds wall shots), one
/// with no stored length (nothing was claimed, so nothing is wrong) and one with no line geometry
/// to measure. The write is a direct column update, so the rows' own last-changed time and the
/// audit log are not touched: this is a correction of a derived figure, not an edit by somebody.
/// </para>
/// </summary>
public sealed class CenterlineLengthRepair(SilexGisDbContext db, CenterlineLengthAudit audit)
{
    /// <summary>
    /// Counts and rewrites in one serialisable transaction, then counts again inside it. Without
    /// <paramref name="apply"/> the transaction is rolled back, so a dry run reports what would
    /// change and changes nothing. If the second count still finds a differing row the transaction
    /// is rolled back rather than committed half right.
    /// </summary>
    public async Task<CenterlineLengthRepairResult> RepairAsync(bool apply, CancellationToken ct = default)
    {
        // Serialisable so that a centreline uploaded again by somebody else while this runs makes
        // the commit fail instead of being overwritten with a figure measured before their change.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Ids first and one geometry at a time after, as the count does: a whole-system survey's
        // geometry can run to megabytes.
        var ids = await db.Centerlines.IgnoreQueryFilters()
            .Where(c => c.Source == CenterlineSource.Uploaded)
            .Select(c => c.Id)
            .ToListAsync(ct);

        int inflated = 0, shorter = 0, agreeing = 0, notMeasurable = 0, updated = 0;
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
            if (!CenterlineLengthAudit.Differs(stored, measured))
            {
                agreeing++;
                continue;
            }

            if (stored > measured)
            {
                inflated++;
            }
            else
            {
                shorter++;
            }

            updated += await db.Centerlines.IgnoreQueryFilters()
                .Where(c => c.Id == id && c.Source == CenterlineSource.Uploaded)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LengthM, (decimal?)measured), ct);
        }

        var before = new CenterlineLengthCount(inflated + shorter + agreeing, inflated, shorter, agreeing, notMeasurable);
        var after = await audit.CountAsync(ct);
        var applied = apply && after.Inflated == 0 && after.Shorter == 0;
        if (applied)
        {
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return new CenterlineLengthRepairResult(before, applied, updated, after);
    }
}
