// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Tests;

/// <summary>
/// Writing a trip without telling anybody, which is what loading a club's old records has to be
/// able to do.
/// </summary>
/// <remarks>
/// The point of the argument is that there is one creation path rather than two: a bulk load must
/// not send a message per row, and the way that is usually got wrong is a quiet copy of the
/// creation code that slowly stops applying the rules the loud one applies. So the silence is a
/// choice handed to the same code, and both halves are checked here with exactly one thing
/// changed between them — because an assertion that nothing was sent passes just as well when the
/// sending was never wired up at all.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripSilentWriteTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private Guid authorId;
    private Guid companionUserId;
    private Guid companionCaverId;

    public TripSilentWriteTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        authorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tsw-auth-{suffix}@t.local");
        companionUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tsw-comp-{suffix}@t.local");
        companionCaverId = await RosterHelper.CaverIdForAsync(factory, companionUserId);
    }

    /// <summary>
    /// The silent write records exactly what the ordinary one records and tells nobody, and the
    /// ordinary one tells the person it newly named.
    /// </summary>
    [Fact]
    public async Task A_trip_written_silently_still_names_its_people_and_only_the_telling_is_skipped()
    {
        var (silentTold, silentRoster) = await CreateOneAsync(TripWriteNotice.Silent);
        silentTold.ShouldBeEmpty();

        // The positive half, with the one thing that differs changed and nothing else: the same
        // person, the same trip, the same code, and now somebody is told about it. Without this
        // half the assertion above would hold just as well if nothing were ever announced.
        var (announcedTold, announcedRoster) = await CreateOneAsync(TripWriteNotice.Announce);
        announcedTold.ShouldBe([companionUserId]);

        // And the roster work happens either way. Silence is a decision about messages; a write
        // that skipped the reconciliation instead would lose the row, and the difference between
        // those two mistakes is invisible in a count of notifications.
        silentRoster.ShouldBe(1);
        announcedRoster.ShouldBe(1);
    }

    private async Task<(IReadOnlyList<Guid> Told, int RosterRows)> CreateOneAsync(TripWriteNotice notice)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await AccessContextResolver.ResolveAsync(db, authorId);
        var recorder = new RecordingAnnouncer();
        var writes = ActivatorUtilities.CreateInstance<TripLogWriteService>(scope.ServiceProvider, recorder);

        var trip = await writes.CreateAsync(
            TripCreationIntent.Report,
            new TripWriteInput
            {
                Title = $"Silent write {notice}",
                TripDate = new DateOnly(2024, 5, 4),
                Participants = [new TripRosterEntry { CaverId = companionCaverId }],
            },
            ctx,
            authorId,
            notice);
        await db.SaveChangesAsync();

        var rows = db.TripLogParticipants.Count(p => p.TripLogId == trip.Id);
        return (recorder.Told, rows);
    }

    /// <summary>Stands in for the surface that would compose the message, and only records.</summary>
    private sealed class RecordingAnnouncer : ITripRosterAnnouncer
    {
        private readonly List<Guid> told = [];

        public IReadOnlyList<Guid> Told => told;

        public Task AnnounceAsync(TripLog trip, IReadOnlyList<Guid> newlyNamedUserIds, CancellationToken ct)
        {
            told.AddRange(newlyNamedUserIds);
            return Task.CompletedTask;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
