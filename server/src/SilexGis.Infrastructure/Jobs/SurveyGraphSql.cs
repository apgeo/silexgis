// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Raw SQL of the survey reading.</summary>
public static class SurveyGraphSql
{
    /// <summary>Namespace of the per-cave lock, so it cannot collide with another lock's key.</summary>
    private const int CaveCenterlineLockClass = 0x53474331;

    /// <summary>
    /// Serialises the writing of centerlines for one cave until the surrounding transaction ends.
    /// </summary>
    /// <remarks>
    /// Two models of one cave can be read at the same moment, by two workers or by a worker and a
    /// caller. Each decides whether its centerline is the cave's first — and so its default shape —
    /// by looking for a centerline already there, and while neither has committed both see none and
    /// both claim the default, which the one-default-per-cave index then refuses for the loser. Taking
    /// this lock before that look makes the second reader wait, and see what the first committed.
    /// Transaction-scoped, so a reading that fails cannot leave the next one waiting.
    /// </remarks>
    public static Task TakeCaveCenterlineLockAsync(SilexGisDbContext db, Guid caveFeatureId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, hashtext({1}))",
            [CaveCenterlineLockClass, caveFeatureId.ToString("N")],
            ct);
}
