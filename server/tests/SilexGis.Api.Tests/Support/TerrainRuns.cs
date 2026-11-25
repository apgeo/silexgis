// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Jobs;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Runs a terrain build's handler directly rather than waiting for a worker, so what a test
/// asserts is what the handler did and not how quickly something got to it.
/// </summary>
public static class TerrainRuns
{
    /// <summary>
    /// Runs the build once. A run that stops on a failure it recorded throws a bounded stand-in so
    /// the queue sees a failed job; the reason is on the build row before it is thrown, and the row
    /// is what the tests read, so that stand-in is let through. Anything else is rethrown: a
    /// defect that surfaced as some other exception — or as the handler's own "unexpected" verdict
    /// — would otherwise end the run early and leave a test that asserts something did not happen
    /// passing for the wrong reason.
    /// </summary>
    /// <param name="allowUnexpected">
    /// True only for a test that provokes the handler's catch-all on purpose.
    /// </param>
    public static async Task RunAsync(
        SilexGisApiFactory factory, Guid buildId, int attempts = 1, Guid? requestedBy = null,
        bool allowUnexpected = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TerrainBuild);

        try
        {
            await handler.ExecuteAsync(
                new ProcessingJob
                {
                    Kind = ProcessingJobKinds.TerrainBuild,
                    Attempts = attempts,
                    RequestedBy = requestedBy,
                    Payload = JsonSerializer.Serialize(new TerrainBuildPayload(buildId), JsonSerializerOptions.Web),
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException e) when (IsRecordedFailure(e, buildId, allowUnexpected))
        {
        }
    }

    /// <summary>
    /// Whether <paramref name="e"/> is the handler's stand-in for a failure of this build that it
    /// recorded on the row: its message is "Terrain build {id} failed: {code}.".
    /// </summary>
    private static bool IsRecordedFailure(InvalidOperationException e, Guid buildId, bool allowUnexpected)
    {
        var prefix = $"Terrain build {buildId} failed: ";
        if (!e.Message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var code = e.Message[prefix.Length..].TrimEnd('.');
        return allowUnexpected || code != TerrainBuildFailures.Unexpected;
    }
}
