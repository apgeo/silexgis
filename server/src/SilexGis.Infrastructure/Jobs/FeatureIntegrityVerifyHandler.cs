// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Runs the feature integrity verifier and reports what it found. Read-only: a divergence
/// means some write path bypassed the aggregate write service, and that is a bug to fix,
/// not damage to auto-repair — repairing it silently would destroy the evidence of which
/// path is wrong.
/// </summary>
/// <remarks>
/// The job fails when problems are found, which is what surfaces them: the row keeps the
/// summary in its error column and the admin jobs list shows it red. Each problem is also
/// logged individually, because the error column holds a summary and an operator chasing
/// one bad feature needs the ids.
/// </remarks>
public sealed class FeatureIntegrityVerifyHandler(
    FeatureIntegrityVerifier verifier,
    ILogger<FeatureIntegrityVerifyHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.FeatureIntegrityVerify;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var problems = await verifier.VerifyAsync(ct);
        if (problems.Count == 0)
        {
            logger.LogInformation("Feature integrity verified: no problems");
            return;
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
            $"{problems.Count} integrity problem(s): {string.Join(", ", byCheck)}");
    }
}
