// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// One answer-about-coming table, answering about two kinds of thing.
/// <para>
/// Everything asserted here is enforced by the database rather than by whoever writes a row, so
/// it is asserted against a real one: a check constraint that admits exactly one subject, a
/// foreign key and a cascade for each of them, and one standing answer per person per subject
/// rather than per person per trip. Code that got any of these wrong would compile.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripInvitationSubjectTests : IAsyncLifetime, IDisposable
{
    private const string SubjectConstraint = "ck_trip_invitations_one_subject";

    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!; // Editor; writes both subjects
    private Guid ownerId;
    private Guid ownerCaver;
    private Guid otherCaver;
    private string suffix = null!;

    public TripInvitationSubjectTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tis-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tis-own-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        ownerCaver = (await db.Cavers.FirstAsync(c => c.UserId == ownerId)).Id;

        var second = new Caver { FullName = $"Ana Second {suffix}" };
        db.Cavers.Add(second);
        await db.SaveChangesAsync();
        otherCaver = second.Id;
    }

    /// <summary>
    /// Exactly one subject per row, refused at the database and not merely unwritten by the
    /// handlers. Both failures are asserted beside the two rows that are accepted, so the
    /// refusals are about the shape of the row rather than about the table refusing everything.
    /// </summary>
    [Fact]
    public async Task An_answer_is_about_one_subject_and_never_about_two_or_none()
    {
        var tripId = await CreateTripAsync("Both and neither");
        var eventId = await CreateEventAsync("Both and neither");

        var both = await SaveFailureAsync(new TripInvitation
        {
            TripLogId = tripId,
            EventId = eventId,
            CaverId = ownerCaver,
        });
        both.ConstraintName.ShouldBe(SubjectConstraint);

        var neither = await SaveFailureAsync(new TripInvitation { CaverId = ownerCaver });
        neither.ConstraintName.ShouldBe(SubjectConstraint);

        // One of each is accepted, which is what makes the two refusals above mean something.
        await AddAsync(new TripInvitation { TripLogId = tripId, CaverId = ownerCaver });
        await AddAsync(new TripInvitation { EventId = eventId, CaverId = ownerCaver });

        (await CountAsync(x => x.TripLogId == tripId)).ShouldBe(1);
        (await CountAsync(x => x.EventId == eventId)).ShouldBe(1);
    }

    /// <summary>
    /// Deleting a club event takes the answers about it with it. This is the whole reason the
    /// row names its subject through a foreign key rather than through a column saying what kind
    /// of thing an id belongs to: a discriminator would leave these rows behind, pointing at
    /// nothing, with nothing in the schema able to notice.
    /// </summary>
    [Fact]
    public async Task Deleting_an_event_takes_the_answers_about_it_with_it()
    {
        var eventId = await CreateEventAsync("Cascades");
        var keptEventId = await CreateEventAsync("Kept");

        await AddAsync(new TripInvitation { EventId = eventId, CaverId = ownerCaver });
        await AddAsync(new TripInvitation { EventId = eventId, CaverId = otherCaver });
        await AddAsync(new TripInvitation { EventId = keptEventId, CaverId = ownerCaver });

        (await owner.DeleteAsync($"/api/v1/events/{eventId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await CountAsync(x => x.EventId == eventId)).ShouldBe(0);
        // The answers about the event that was not deleted are untouched, so the cascade removed
        // the rows of one subject rather than everything in the table.
        (await CountAsync(x => x.EventId == keptEventId)).ShouldBe(1);
    }

    /// <summary>
    /// And a trip still does what it always did. The second subject was added beside the first
    /// rather than in place of it, so the first one's cascade is the half that could quietly stop
    /// working while every new test went on passing.
    /// </summary>
    [Fact]
    public async Task Deleting_a_trip_still_takes_the_answers_about_it_with_it()
    {
        var tripId = await CreateTripAsync("Cascades");
        var keptTripId = await CreateTripAsync("Kept");

        await AddAsync(new TripInvitation { TripLogId = tripId, CaverId = ownerCaver });
        await AddAsync(new TripInvitation { TripLogId = tripId, CaverId = otherCaver });
        await AddAsync(new TripInvitation { TripLogId = keptTripId, CaverId = ownerCaver });

        (await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await CountAsync(x => x.TripLogId == tripId)).ShouldBe(0);
        (await CountAsync(x => x.TripLogId == keptTripId)).ShouldBe(1);
    }

    /// <summary>
    /// One person, one subject, one standing answer — counted per subject and not per trip. The
    /// same person holding an answer about a trip and about an event at the same time is the
    /// ordinary case and must not collide with itself.
    /// </summary>
    [Fact]
    public async Task One_person_holds_one_answer_about_each_subject_and_two_subjects_do_not_collide()
    {
        var tripId = await CreateTripAsync("One answer");
        var eventId = await CreateEventAsync("One answer");

        await AddAsync(new TripInvitation { TripLogId = tripId, CaverId = ownerCaver });
        await AddAsync(new TripInvitation { EventId = eventId, CaverId = ownerCaver });

        var twiceOnTheTrip = await SaveFailureAsync(
            new TripInvitation { TripLogId = tripId, CaverId = ownerCaver });
        twiceOnTheTrip.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        twiceOnTheTrip.ConstraintName.ShouldBe("ix_trip_invitations_trip_log_id_caver_id");

        var twiceOnTheEvent = await SaveFailureAsync(
            new TripInvitation { EventId = eventId, CaverId = ownerCaver });
        twiceOnTheEvent.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        twiceOnTheEvent.ConstraintName.ShouldBe("ix_trip_invitations_event_id_caver_id");

        // Somebody else answering the same two is not a collision either, so the index holds one
        // person to one answer rather than one subject to one answer.
        await AddAsync(new TripInvitation { TripLogId = tripId, CaverId = otherCaver });
        await AddAsync(new TripInvitation { EventId = eventId, CaverId = otherCaver });

        (await CountAsync(x => x.TripLogId == tripId)).ShouldBe(2);
        (await CountAsync(x => x.EventId == eventId)).ShouldBe(2);
    }

    /// <summary>
    /// Where an answer lands on a timeline, asserted for both subjects.
    /// <para>
    /// The trip half is not the redundant one. Getting this wrong throws nothing and breaks no
    /// page — the row is simply filed under some other object, or under none — so a suite
    /// pinning only the newer subject would go on passing while trip histories quietly stopped
    /// saying who had been asked.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_answer_is_filed_on_the_timeline_of_the_subject_it_is_about()
    {
        var tripId = await CreateTripAsync("Timeline");
        var eventId = await CreateEventAsync("Timeline");

        // The trip half goes through the shipped route, so what is asserted is the path a person
        // actually takes rather than a row written straight into the table.
        var answered = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{tripId}/invitations/{ownerCaver}/response",
            new { response = "yes" });
        answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());

        await AddAsync(new TripInvitation { EventId = eventId, CaverId = ownerCaver });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trail = await db.AuditEntries.AsNoTracking()
                .Where(a => a.EntityType == nameof(TripInvitation))
                .Where(a => (a.RootEntityType == nameof(TripLog) && a.RootEntityId == tripId.ToString())
                    || (a.RootEntityType == nameof(Event) && a.RootEntityId == eventId.ToString()))
                .Select(a => new { a.RootEntityType, a.RootEntityId })
                .ToListAsync();

            // Each subject collected its own answer, and neither collected the other's. Both
            // halves matter: filed under the wrong subject and filed under none look the same
            // from a page, and only the second of them leaves the other subject's trail empty.
            trail.ShouldContain(a => a.RootEntityType == nameof(TripLog) && a.RootEntityId == tripId.ToString());
            trail.ShouldContain(a => a.RootEntityType == nameof(Event) && a.RootEntityId == eventId.ToString());
            trail.Count(a => a.RootEntityType == nameof(TripLog)).ShouldBe(1);
            trail.Count(a => a.RootEntityType == nameof(Event)).ShouldBe(1);
        }

        // And the trip's own timeline still reads the answer back, which is the round trip the
        // database assertions above cannot make on their own.
        var timeline = await owner.GetAsync(
            $"/api/v1/history?entityType=tripLog&entityId={tripId}&pageSize=200");
        var payload = await timeline.Content.ReadAsStringAsync();
        timeline.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray()
            .ShouldContain(e => e.GetProperty("entityType").GetString() == nameof(TripInvitation));
    }

    private async Task<TripInvitation> AddAsync(TripInvitation row)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripInvitations.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>The database's own refusal of a row, on a scope that is thrown away afterwards.</summary>
    private async Task<PostgresException> SaveFailureAsync(TripInvitation row)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripInvitations.Add(row);
        var thrown = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        return thrown.InnerException.ShouldBeOfType<PostgresException>();
    }

    private async Task<int> CountAsync(
        System.Linq.Expressions.Expression<Func<TripInvitation, bool>> where)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripInvitations.AsNoTracking().CountAsync(where);
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2054-09-12",
            participants = Array.Empty<object>(),
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateEventAsync(string title)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/events", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            kind = "clubMeeting",
            startDate = "2054-09-12",
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
    }
}
