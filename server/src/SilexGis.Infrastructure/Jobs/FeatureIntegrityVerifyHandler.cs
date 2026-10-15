// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Removes access rows naming rows that no longer exist, then runs the feature integrity
/// verifier and reports what it found. The verifier is read-only: a divergence means some
/// write path bypassed the aggregate write service, and that is a bug to fix, not damage to
/// auto-repair — repairing it silently would destroy the evidence of which path is wrong.
/// </summary>
/// <remarks>
/// <para>
/// The one repair is the access sweep, and it is not silent. A rule whose object is gone has a
/// single correct state — absent — and removing it changes nobody's access, while nothing else
/// can ever remove it. So it is removed, each row through the audit trail and the log, and the
/// pass still fails, naming how many rows of each kind went: the repair is done and the report
/// that a delete path missed them is made all the same.
/// </para>
/// <para>
/// The job fails when problems are found, which is what surfaces them: the row keeps the
/// summary in its error column and the admin jobs list shows it red. Each problem is also
/// logged individually, because the error column holds a summary and an operator chasing
/// one bad feature needs the ids.
/// </para>
/// </remarks>
public sealed class FeatureIntegrityVerifyHandler(
    AccessOrphanSweep accessSweep,
    FeatureIntegrityVerifier verifier,
    ILogger<FeatureIntegrityVerifyHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.FeatureIntegrityVerify;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var removed = await accessSweep.RemoveAsync(ct);
        var problems = await verifier.VerifyAsync(ct);
        if (problems.Count == 0 && removed.Count == 0)
        {
            logger.LogInformation("Feature integrity verified: no problems");
            return;
        }

        var removedSummary = string.Join(", ", removed
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => $"{r.Key}={r.Value}"));
        if (removed.Count > 0)
        {
            logger.LogError(
                "Removed {Count} access row(s) naming rows that no longer exist: {Removed}",
                removed.Values.Sum(), removedSummary);
        }

        if (problems.Count == 0)
        {
            throw new InvalidOperationException(
                $"Removed {removed.Values.Sum()} orphaned access row(s): {removedSummary}");
        }

        foreach (var problem in problems)
        {
            logger.LogError(
                "Feature integrity problem: {Check} on {FeatureId} — {Detail}",
                problem.Check, problem.FeatureId, problem.Detail);
        }

        var byCheck = problems
            .GroupBy(p => p.Check)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}={g.Count()}");
        throw new InvalidOperationException(
            $"{problems.Count} integrity problem(s): {string.Join(", ", byCheck)}"
            + (removed.Count == 0 ? string.Empty : $"; removed {removed.Values.Sum()} orphaned access row(s): {removedSummary}"));
    }
}
