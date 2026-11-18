// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Holds one row locked from another connection, so an upload that reaches it parks half way
/// through its batch with everything it wrote before that row still uncommitted.
/// </summary>
/// <remarks>
/// A race between two uploads is decided by what the second one reads while the first is inside
/// its transaction. Sent one after the other, every arbitration passes; only a test that keeps the
/// first upload inside that window — by making it wait on a row this connection holds — can tell an
/// arbitration that holds from one that merely looks right. Each step waits until the database
/// reports the expected number of statements parked on a lock, so an upload that answered without
/// ever waiting fails the test instead of passing it for the wrong reason.
/// </remarks>
public sealed class RowStall : IAsyncDisposable
{
    private readonly AsyncServiceScope scope;
    private readonly SilexGisDbContext db;
    private readonly IDbContextTransaction transaction;

    private RowStall(AsyncServiceScope scope, SilexGisDbContext db, IDbContextTransaction transaction)
    {
        this.scope = scope;
        this.db = db;
        this.transaction = transaction;
    }

    /// <summary>Locks the row <paramref name="id"/> of <paramref name="table"/> until released.</summary>
    public static async Task<RowStall> HoldAsync(SilexGisApiFactory factory, string table, Guid id)
    {
        var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        var sql = table switch
        {
            "features" => "SELECT 1 AS \"Value\" FROM features WHERE id = {0} FOR UPDATE",
            "trip_recordings" => "SELECT 1 AS \"Value\" FROM trip_recordings WHERE id = {0} FOR UPDATE",
            "documents" => "SELECT 1 AS \"Value\" FROM documents WHERE id = {0} FOR UPDATE",
            "raster_maps" => "SELECT 1 AS \"Value\" FROM raster_maps WHERE id = {0} FOR UPDATE",
            "res_links" => "SELECT 1 AS \"Value\" FROM res_links WHERE id = {0} FOR UPDATE",
            "survey_uploads" => "SELECT 1 AS \"Value\" FROM survey_uploads WHERE id = {0} FOR UPDATE",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table a phone writes to."),
        };
        (await db.Database.SqlQueryRaw<int>(sql, id).ToListAsync())
            .Count.ShouldBe(1, $"the stall row of {table} must exist and be locked");
        return new RowStall(scope, db, transaction);
    }

    /// <summary>
    /// Holds the whole feature table against writes until released, while leaving it readable,
    /// so that requests which look before they write each look, and then park at their first
    /// write with what they saw still in hand.
    /// </summary>
    public static async Task<RowStall> HoldFeatureWritesAsync(SilexGisApiFactory factory)
    {
        var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE features IN SHARE MODE");
        return new RowStall(scope, db, transaction);
    }

    /// <summary>
    /// Takes the lock under which a cave's survey readings decide which centerline is its first,
    /// until released, so that readings of that cave's models park before they look.
    /// </summary>
    public static async Task<RowStall> HoldCaveCenterlinesAsync(SilexGisApiFactory factory, Guid caveFeatureId)
    {
        var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        await SurveyGraphSql.TakeCaveCenterlineLockAsync(db, caveFeatureId, CancellationToken.None);
        return new RowStall(scope, db, transaction);
    }

    /// <summary>
    /// Waits until <paramref name="count"/> statements of this database are parked on a lock,
    /// failing as soon as <paramref name="pending"/> answers instead.
    /// </summary>
    public Task WaitForWaitersAsync(int count, Task pending) =>
        LockWaits.UntilParkedAsync(db, count, pending, "an upload in the race");

    /// <summary>Lets the parked upload go on.</summary>
    public Task ReleaseAsync() => transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await scope.DisposeAsync();
    }
}
