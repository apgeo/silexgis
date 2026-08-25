// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Infrastructure.Jobs;

namespace SilexGis.Api.Tests.Support;

/// <summary>Alterations to a test host that more than one test class needs.</summary>
public static class TestHostTweaks
{
    /// <summary>
    /// Stops the background queue drain for this host, so a queued job runs only when a test runs
    /// it by hand.
    /// </summary>
    /// <remarks>
    /// Every test class shares one database, and this worker claims anything claimable in it every
    /// couple of seconds. Left running, a tick landing between a request and the assertions a few
    /// milliseconds later turns "nothing has been handed out yet" into an intermittent failure
    /// about code that is behaving correctly. The notification worker is stopped for the same
    /// reason, by a setting; this one has no setting, so it is removed.
    /// </remarks>
    public static void WithoutJobWorker(IServiceCollection services)
    {
        var worker = services.Single(d => d.ImplementationType == typeof(ProcessingJobWorker));
        services.Remove(worker);
    }
}
