// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Infrastructure.Jobs;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Takes the queue drains out of a test host.
/// </summary>
/// <remarks>
/// Every test class shares one PostGIS container and the queue lives in it, so a drain started by
/// one class claims work another class queued and fails it against storage this class does not
/// have. There is one worker per lane, and a class that wants no drain wants all of them gone —
/// removing the general one alone would leave the terrain lane draining, which is the same hazard
/// wearing a different name.
/// </remarks>
internal static class JobWorkers
{
    public static void RemoveFrom(IServiceCollection services)
    {
        var workers = services
            .Where(s => s.ImplementationType is { } type
                && typeof(ProcessingJobWorkerBase).IsAssignableFrom(type))
            .ToList();

        foreach (var worker in workers)
        {
            services.Remove(worker);
        }
    }
}
