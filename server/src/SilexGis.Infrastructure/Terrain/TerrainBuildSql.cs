// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Raw SQL for terrain builds (raw SQL lives only in *Sql.cs files).
/// </summary>
public static class TerrainBuildSql
{
    /// <summary>
    /// The key every terrain submission takes the same lock on. An arbitrary constant: what it
    /// names is "somebody is deciding whether to start a build", and there is only one such
    /// decision to serialise.
    /// </summary>
    private const long SubmissionLockKey = 0x54455252_41494e01L; // "TERRAIN" + 1

    /// <summary>
    /// Holds every other submission back until this transaction ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard that refuses a second build of an area already being built is a read followed by a
    /// write, and the case it exists for — an impatient administrator pressing the button twice —
    /// is exactly the case that slips between the two: both requests read a queue with nothing in
    /// it and both then insert hours of work. Taking one lock first makes the pair indivisible.
    /// </para>
    /// <para>
    /// One key for all submissions rather than one per rectangle, deliberately. Whether two
    /// rectangles are "the same area" is a decision with one home, in the query the guard runs; a
    /// lock key derived from the geometry would be a second, subtly different opinion about it,
    /// and the one that governed would be whichever was coarser. Submissions are rare and the
    /// decision is a single indexed query, so serialising all of them costs nothing worth
    /// measuring — the work itself is serialised by one worker anyway.
    /// </para>
    /// <para>
    /// A transaction-scoped lock, so it is released by the commit or the rollback and there is no
    /// way to leak one: a request that fails between taking it and finishing cannot leave every
    /// later submission waiting for a connection that has gone.
    /// </para>
    /// </remarks>
    public static Task TakeSubmissionLockAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [SubmissionLockKey], ct);

    /// <summary>
    /// The key every change of which build the scene draws takes. One decision, one key.
    /// </summary>
    private const long ActivationLockKey = 0x54455252_41494e02L; // "TERRAIN" + 2

    /// <summary>
    /// Holds every other attempt to change which build the scene draws back until this transaction
    /// ends.
    /// </summary>
    /// <remarks>
    /// At most one build carries that mark, and the database holds the rule with a unique index — so
    /// two people pressing the button at the same instant do not produce two current builds, they
    /// produce one constraint violation, which is the rule working correctly and an answer nobody
    /// can act on. Taking one lock first makes the pair of writes that moves the mark indivisible,
    /// so the second attempt waits, sees where the first left it, and moves it on from there.
    /// </remarks>
    public static Task TakeActivationLockAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [ActivationLockKey], ct);

    /// <summary>
    /// The family every lock over one build's stored raster description belongs to.
    /// </summary>
    /// <remarks>
    /// Two numbers rather than one, because this lock is taken per build: the first names the
    /// decision being serialised and the second names which build it is being made about, so two
    /// builds being described at the same moment do not wait for one another.
    /// </remarks>
    private const int RasterIndexLockFamily = 0x54455203; // "TER" + 3

    /// <summary>
    /// Holds every other attempt to write this build's raster description back until this
    /// transaction ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The description is replaced rather than merged — deleted, then written again — and a build
    /// prepared once is described by whoever asks first, which on a busy installation is several
    /// requests arriving together. Without a lock both delete an empty set, both insert the same
    /// rows, and the second meets the uniqueness rule on the pair of build and path: a failed
    /// request where there was no disagreement about the answer, only about who would write it
    /// down. Taking one lock first makes the pair of writes indivisible, so the second waits, finds
    /// the first one's rows, and replaces them with the same rows.
    /// </para>
    /// <para>
    /// A transaction-scoped lock, released by the commit or the rollback, so a request that fails
    /// midway cannot leave later ones waiting on a connection that has gone.
    /// </para>
    /// </remarks>
    public static Task TakeRasterIndexLockAsync(
        SilexGisDbContext db, Guid buildId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            [RasterIndexLockFamily, RasterIndexLockKey(buildId)],
            ct);

    /// <summary>
    /// One build's identifier folded into the single 32-bit number an advisory lock key is.
    /// </summary>
    /// <remarks>
    /// A fold rather than a mapping: two builds can collide, and a collision costs one of them a
    /// short wait and nothing else, because the lock only orders writers of a table each of them
    /// would otherwise have written correctly on its own. Written out by hand rather than taken
    /// from the runtime's own string hash, which is deliberately not stable between processes and
    /// would therefore stop two servers from agreeing on a key at all.
    /// </remarks>
    private static int RasterIndexLockKey(Guid buildId)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = buildId.TryWriteBytes(bytes);

        var folded = 0u;
        for (var i = 0; i < bytes.Length; i++)
        {
            folded = (folded * 31u) + bytes[i];
        }

        return unchecked((int)folded);
    }
}
