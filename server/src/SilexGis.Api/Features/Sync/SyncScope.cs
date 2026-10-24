// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// What a sync set holds for one caller: the single answer every download stream reads its rows
/// against. The feature stream hands out exactly <see cref="HandedAsync"/>; every other stream
/// carries a row only when the feature it hangs on is in that same answer.
/// </summary>
/// <remarks>
/// <para>
/// A stream that decided this for itself would be a second answer to the question, and the two
/// would drift: a photo, a document link or a pin could then reach a device beside a feature the
/// device is not given — and a row hanging on a withheld feature tells the device where to look
/// for it. So the streams ask here and do not restate any part of it.
/// </para>
/// <para>
/// Three things decide it, in this order: the row is contained in one of the set's roots (read off
/// the row's own ancestry, which carries the row itself, so a root is in its own set); the caller
/// may read it; and it is not withheld from this channel (<see cref="SyncWithhold"/>, which is the
/// only place that says so).
/// </para>
/// </remarks>
public static class SyncScope
{
    /// <summary>
    /// The live features of the set the caller may read, before anything is withheld. A query
    /// still to be narrowed, never an answer on its own: what a stream hands out is what
    /// <see cref="WithoutWithheldAsync"/> leaves of it.
    /// </summary>
    public static IQueryable<Feature> Readable(
        SilexGisDbContext db, AccessContext ctx, IReadOnlyCollection<Guid> roots)
    {
        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        return visible.Where(f => f.AncestorIds.Any(a => roots.Contains(a)));
    }

    /// <summary>
    /// The features of the set that have gone and whose going the caller may be told of — the
    /// feature stream's tombstones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place the streams look past the filter that hides deleted rows, and the reason they
    /// do: a delete that never reaches a device is a delete that undoes itself the next time the
    /// device uploads, putting back the cave the club removed. The carve-out is kept as narrow as
    /// it can be — restricted to rows that are actually gone; the stream projects them to an
    /// identifier and a time before they leave the database, so nothing else can ride along.
    /// </para>
    /// <para>
    /// Their ancestry is read unfiltered, and only their ancestry. A row inherits its audience from
    /// the rows containing it, so asking that question against live rows alone would ask who may
    /// read a deleted cave and find that the cave itself is no longer there to answer — every
    /// deletion of a whole cave would then produce no record at all, which is exactly the case the
    /// tombstone exists for. Reading the ancestry unfiltered cannot widen the live half in return,
    /// because deletion is stamped over the whole containment subtree: nothing live sits under
    /// something deleted.
    /// </para>
    /// <para>
    /// Nothing here is withheld. A stub is an identifier and a moment, and neither locates
    /// anything — while a delete that does not propagate is permanent: a device that held the row
    /// from before it was guarded keeps it for ever and puts it back on its next upload. The rows
    /// are already limited to ones this account may read, and it reads those same rows through the
    /// rest of this API, grid-snapped, whenever it likes; withholding the stub would only lose the
    /// deletion.
    /// </para>
    /// </remarks>
    public static IQueryable<Feature> Gone(
        SilexGisDbContext db, AccessContext ctx, IReadOnlyCollection<Guid> roots)
    {
        var goneAncestry = db.Features.AsNoTracking().IgnoreQueryFilters();
        return db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.DeletedAt != null)
            .VisibleTo(ctx, goneAncestry, db.FeatureSetMembers)
            .Where(f => f.AncestorIds.Any(a => roots.Contains(a)));
    }

    /// <summary>
    /// Folds what this caller may not be given out of a query over <see cref="Readable"/>, before
    /// the query is ordered, cut or counted. The feature stream passes its query already advanced
    /// past the device's position, which asks the protection rule about fewer rows and changes no
    /// answer: whether a row is withheld does not depend on which other rows are asked about.
    /// </summary>
    public static async Task<IQueryable<Feature>> WithoutWithheldAsync(
        IQueryable<Feature> readable, FeatureProtection protection, AccessContext ctx, CancellationToken ct)
    {
        var withheld = await SyncWithhold.WithheldIdsAsync(protection, ctx, [readable], ct);
        if (withheld.Count == 0)
        {
            return readable;
        }

        var withheldIds = withheld.ToArray();
        return readable.Where(f => !withheldIds.Contains(f.Id));
    }

    /// <summary>
    /// The features of the set the feature stream would hand this caller right now. A row of any
    /// other stream is carried only when the feature it hangs on is in this answer; one hanging on
    /// a feature outside it is absent, not stubbed.
    /// </summary>
    public static Task<IQueryable<Feature>> HandedAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> roots,
        CancellationToken ct) =>
        WithoutWithheldAsync(Readable(db, ctx, roots), protection, ctx, ct);
}
