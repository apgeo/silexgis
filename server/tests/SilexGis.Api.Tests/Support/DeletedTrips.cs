// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// What a test needs in order to look at, age and remove a deleted trip.
/// </summary>
/// <remarks>
/// A deleted trip is hidden by the model from every ordinary read, a test's own included, so
/// each of these says so in one place rather than leaving every class to remember the escape.
/// </remarks>
internal static class DeletedTrips
{
    /// <summary>The trip's row whether or not it is deleted, or null once it is gone for good.</summary>
    public static async Task<TripLog?> RowAsync(SilexGisApiFactory factory, Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogs.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tripId);
    }

    /// <summary>
    /// Moves a deleted trip's stamp back past the installation's default window. On a host
    /// running the machine's own clock this is the one lever there is for making a row due:
    /// whether a trip is due is a comparison against that clock, and a test cannot wait a month.
    /// </summary>
    public static async Task AgePastTheWindowAsync(SilexGisApiFactory factory, params Guid[] tripIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(TripDeletionRules.DefaultRetentionDays + 1);
        var aged = await db.TripLogs.IgnoreQueryFilters()
            .Where(t => tripIds.Contains(t.Id) && t.DeletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, longAgo));

        // A trip that was never deleted has no stamp to move, and a pass run after this would
        // then prove nothing about it.
        aged.ShouldBe(tripIds.Length);
    }

    /// <summary>
    /// Runs the pass that removes deleted trips past their window, exactly as the queue would:
    /// the registered handler, resolved from a scope of its own.
    /// </summary>
    public static async Task RunPurgeAsync(SilexGisApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TripPurge);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TripPurge }, CancellationToken.None);
    }
}
