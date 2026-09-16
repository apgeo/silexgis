// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Infrastructure.Jobs;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Takes the queue drains out of a test host.
/// </summary>
/// <remarks>
/// A class owns its own database, so a drain can no longer reach another class's rows — but it
/// still races the class that queued them. The worker claims a job on its own schedule, and a test
/// that then runs the same job by hand is a second writer rather than a retry; work landing under
/// a key its queuer fixed collides, and a sweep silently runs twice. A class that drives its own
/// jobs and wants nothing else touching them takes the drains out here. There is one worker per
/// lane and a class that wants no drain wants all of them gone — removing the general one alone
/// would leave the terrain lane draining, which is the same hazard wearing a different name.
/// A class that must keep a worker (because something it does depends on the queue carrying a job)
/// drives its own jobs through the claiming helper instead.
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
