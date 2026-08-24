// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The listing of the trips a caller is on: whose it is, in what order, and what narrows it.
/// </summary>
/// <remarks>
/// <para>
/// The first case is the one this listing exists to get right. Everywhere else in the
/// application, asking which trips a named person was on is refused, because the answer is
/// assembled out of rows the asker may never open and the size of it gives that answer away even
/// when no row comes back. This route sidesteps that by taking no subject at all — so the test
/// that matters is not "the right rows came back", it is "nothing a caller can put in the request
/// moves whose rows they are".
/// </para>
/// <para>
/// Two of the accounts here hold the ordinary editing membership, which reads past visibility at
/// the widest scope. That is deliberate for the ownership cases: it makes readability incapable
/// of doing the work, so a trip missing from somebody's list can only be missing because they are
/// not on it. The visibility case uses a plain reader holding nothing instead, for the opposite
/// reason.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripsOfTheCallerTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient ana = null!;      // Editor; the caller most cases are about
    private HttpClient bogdan = null!;   // Editor; somebody else, with trips of his own
    private HttpClient reader = null!;   // Viewer holding nothing except what a test grants

    private Guid anaId;
    private Guid bogdanId;
    private Guid readerId;

    private Guid anaCaver;
    private Guid bogdanCaver;
    private Guid readerCaver;

    public TripsOfTheCallerTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        anaId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tmine-ana-{suffix}@t.local");
        bogdanId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tmine-bog-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tmine-rdr-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            anaCaver = await CaverOfAsync(db, anaId);
            bogdanCaver = await CaverOfAsync(db, bogdanId);
            readerCaver = await CaverOfAsync(db, readerId);
        }

        ana = await AuthHelper.BearerClientAsync(factory, $"tmine-ana-{suffix}@t.local");
        bogdan = await AuthHelper.BearerClientAsync(factory, $"tmine-bog-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tmine-rdr-{suffix}@t.local");
    }

    /// <summary>
    /// Whose trips these are is worked out from the request's own identity, and nothing a caller
    /// can write into the request moves it. The parameter names tried here are the ones somebody
    /// would reach for first, and the assertion is that each of them changes nothing at all —
    /// which is what proves no such parameter was quietly wired, and what fails loudly the day
    /// one is.
    /// </summary>
    /// <remarks>
    /// Both accounts read past visibility, and the test asserts that first: Ana can open
    /// Bogdan's trip perfectly well. So its absence from her list is not visibility doing the
    /// work by accident — it is absent because she is not on it, which is the only thing this
    /// listing is allowed to be about.
    /// </remarks>
    [Fact]
    public async Task Nothing_in_the_request_can_ask_about_anybody_else()
    {
        var hers = await CreateAsync(ana, "Hers", "2041-04-11", anaCaver);
        var his = await CreateAsync(bogdan, "His", "2041-04-12", bogdanCaver);

        // Readability is not what separates the two lists, and this is the proof of it.
        (await ana.GetAsync($"/api/v1/trip-logs/{his}")).StatusCode
            .ShouldBe(HttpStatusCode.OK, "an editor reads another editor's trip");

        var plain = await MineAsync(ana, "from=2041-01-01&to=2041-12-31&pageSize=200");
        plain.ShouldContain(hers);
        plain.ShouldNotContain(his, "a trip Ana may read but is not on is not one of hers");

        // Every spelling somebody might invent for "ask about that person instead". None of them
        // may shift the subject, and none of them may be answered as a filter either.
        foreach (var name in new[]
        {
            "participantCaverId", "participantId", "caverId", "subjectCaverId",
            "caver", "participant", "userId", "for", "mine", "subject",
        })
        {
            foreach (var value in new[] { bogdanCaver.ToString(), bogdanId.ToString() })
            {
                var probed = await MineAsync(
                    ana, $"from=2041-01-01&to=2041-12-31&pageSize=200&{name}={value}");
                probed.ShouldBe(plain, $"'{name}' moved whose trips the listing answers with");
            }
        }
    }

    /// <summary>
    /// Being asked counts as being on it. Somebody invited to a plan and not yet written onto its
    /// roster has the trip in their diary as much as anybody already named, which is the whole
    /// reason the question is asked of both halves rather than of the roster alone.
    /// </summary>
    [Fact]
    public async Task Somebody_asked_about_a_trip_has_it_on_their_own_list()
    {
        var trip = await CreateAsync(ana, "Asked", "2041-05-20", anaCaver);

        (await MineAsync(bogdan, "from=2041-05-01&to=2041-05-31&pageSize=200"))
            .ShouldNotContain(trip, "nobody is on a trip before they are named or asked");

        var invited = await ana.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/invitations/", new { caverId = bogdanCaver });
        invited.StatusCode.ShouldBe(
            HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());

        (await MineAsync(bogdan, "from=2041-05-01&to=2041-05-31&pageSize=200")).ShouldContain(trip);
    }

    /// <summary>
    /// Saying no takes the trip off the list, and being put on the party anyway puts it back.
    /// </summary>
    /// <remarks>
    /// The listing answers what somebody is going on, and a weekend they turned down is not that.
    /// The failure this guards against is quiet rather than loud: no row carries the reader's own
    /// answer, so a declined trip reads exactly like one they are attending, and a short list —
    /// the five rows a dashboard panel shows — fills with the ones they said no to while the trip
    /// they are actually going on falls off the end. Being written onto the roster regardless is
    /// the organiser overruling the answer, and the roster is what the list then believes.
    /// </remarks>
    [Fact]
    public async Task A_trip_the_caller_declined_is_not_theirs_unless_the_roster_says_otherwise()
    {
        var declined = await CreateAsync(ana, "Declined", "2047-03-08", anaCaver);
        var declinedAnyway = await CreateAsync(ana, "Declined but rostered", "2047-03-09", anaCaver, alsoOnRoster: bogdanCaver);

        await InviteAsync(declined, bogdanCaver);
        await InviteAsync(declinedAnyway, bogdanCaver);

        var window = "from=2047-01-01&to=2047-12-31&pageSize=200";
        var beforeAnswering = await MineAsync(bogdan, window);
        beforeAnswering.ShouldContain(declined, "being asked and not having answered is being on it");

        await AnswerAsync(declined, bogdanCaver, "no");
        await AnswerAsync(declinedAnyway, bogdanCaver, "no");

        var after = await MineAsync(bogdan, window);
        after.ShouldNotContain(declined, "a weekend somebody turned down is not one they are going on");
        after.ShouldContain(
            declinedAnyway, "written onto the party regardless, which overrules the answer");

        // A maybe is a real answer and not a slower no: still undecided is still in the diary.
        await AnswerAsync(declined, bogdanCaver, "maybe");
        (await MineAsync(bogdan, window)).ShouldContain(declined, "an undecided answer is not a no");
    }

    /// <summary>
    /// Soonest first, which is the opposite of every other trip listing and is the point of this
    /// one: the next thing somebody is going on is the row they came for.
    /// </summary>
    [Fact]
    public async Task The_soonest_trip_comes_first()
    {
        var june = await CreateAsync(ana, "June", "2042-06-14", anaCaver);
        var march = await CreateAsync(ana, "March", "2042-03-02", anaCaver);
        var april = await CreateAsync(ana, "April", "2042-04-30", anaCaver);

        var ordered = await MineAsync(ana, "from=2042-01-01&to=2042-12-31&pageSize=200");
        ordered.ShouldBe([march, april, june]);
    }

    /// <summary>
    /// The window bounds the list, and today is the floor when nobody supplies one — a list of
    /// what somebody is going on is useless if it opens on last winter. An explicit window is
    /// honoured as asked, backwards included: every row is a trip the caller is already on and
    /// may already read, so widening it withholds nothing that was being withheld.
    /// </summary>
    [Fact]
    public async Task Today_is_the_floor_and_a_window_moves_it()
    {
        var past = await CreateAsync(ana, "Long over", "2019-08-03", anaCaver);
        var soon = await CreateAsync(ana, "Coming up", "2043-02-09", anaCaver);
        var later = await CreateAsync(ana, "Much later", "2043-11-27", anaCaver);

        var byDefault = await MineAsync(ana, "pageSize=200");
        byDefault.ShouldNotContain(past, "a trip that is over is not something the caller is going on");
        byDefault.ShouldContain(soon);
        byDefault.ShouldContain(later);

        var widened = await MineAsync(ana, "from=2019-01-01&pageSize=200");
        widened.ShouldContain(past, "an explicit window is honoured as asked");

        var bounded = await MineAsync(ana, "from=2043-01-01&to=2043-06-30&pageSize=200");
        bounded.ShouldContain(soon);
        bounded.ShouldNotContain(later);
        bounded.ShouldNotContain(past);
    }

    /// <summary>
    /// A trip that runs across the window's start is still something the caller is on rather than
    /// something they have missed, so the window asks about overlap. A trip with no end date is
    /// one day long — reading the stored end alone would drop every single-day trip out of every
    /// window, silently, which is the failure this asserts against.
    /// </summary>
    [Fact]
    public async Task The_window_holds_a_trip_it_runs_across_and_one_that_lasted_a_day()
    {
        var across = await CreateAsync(ana, "Across", "2044-06-28", anaCaver, end: "2044-07-04");
        var oneDay = await CreateAsync(ana, "One day", "2044-07-02", anaCaver);
        var over = await CreateAsync(ana, "Over", "2044-05-01", anaCaver, end: "2044-05-06");

        var july = await MineAsync(ana, "from=2044-07-01&to=2044-07-31&pageSize=200");
        july.ShouldContain(across, "a trip running across the first of the month overlaps it");
        july.ShouldContain(oneDay, "a trip with no end date ran for one day, not for no days");
        july.ShouldNotContain(over);
    }

    /// <summary>
    /// Every day the trip is out is a day the window can touch. A window meeting only its first
    /// day holds it, so does one meeting only its last, and so does one falling wholly inside it
    /// and touching neither end. A trip with no end date is one day long and is held by the
    /// window over that day alone. The day either side is the control that makes those
    /// assertions about inclusivity rather than about the trip being on the list at all.
    /// </summary>
    [Fact]
    public async Task The_window_touches_the_first_day_the_last_day_and_a_day_in_between()
    {
        var multiDay = await CreateAsync(ana, "Long push", "2052-03-10", anaCaver, end: "2052-03-20");
        var oneDay = await CreateAsync(ana, "Day out", "2052-03-15", anaCaver);

        // The window meets the trip's first day, then its last, then neither end of it.
        (await MineAsync(ana, "from=2052-03-01&to=2052-03-10&pageSize=200")).ShouldBe([multiDay]);
        (await MineAsync(ana, "from=2052-03-20&to=2052-03-31&pageSize=200")).ShouldBe([multiDay]);
        (await MineAsync(ana, "from=2052-03-13&to=2052-03-14&pageSize=200")).ShouldBe([multiDay]);

        // The single day, held by the window over that day alone. Both trips come back, soonest
        // first, so the order the list answers in is asserted along with its membership.
        (await MineAsync(ana, "from=2052-03-15&to=2052-03-15&pageSize=200"))
            .ShouldBe([multiDay, oneDay]);
        (await MineAsync(ana, "from=2052-03-16&to=2052-03-16&pageSize=200")).ShouldBe([multiDay]);

        // One day past either end of the multi-day trip, which is what makes the three edges
        // above assertions about inclusive bounds.
        (await MineAsync(ana, "from=2052-03-21&to=2052-03-31&pageSize=200")).ShouldBeEmpty();
        (await MineAsync(ana, "from=2052-01-01&to=2052-03-09&pageSize=200")).ShouldBeEmpty();
    }

    /// <summary>
    /// The state narrows the list and nothing is excluded from it by default. A trip the caller is
    /// on that has been called off is exactly the thing they most need to see on a list of what is
    /// coming up, so it is shown unless they ask otherwise. A word this application does not have
    /// is refused with a code of its own, rather than quietly meaning "no filter" — a caller who
    /// asked for something that cannot exist wants to be told.
    /// </summary>
    [Fact]
    public async Task The_state_narrows_the_list_and_a_word_a_trip_cannot_hold_is_refused()
    {
        var planned = await CreateAsync(ana, "Organised", "2045-03-11", anaCaver);
        var draft = await CreateAsync(ana, "Still a draft", "2045-03-12", anaCaver);
        var called = await CreateAsync(ana, "Called off", "2045-03-13", anaCaver);

        await MoveAsync(planned, "planned");
        await MoveAsync(called, "cancelled");

        var window = "from=2045-01-01&to=2045-12-31&pageSize=200";

        var all = await MineAsync(ana, window);
        all.ShouldBe([planned, draft, called], "no state is left out unless the caller asks");

        (await MineAsync(ana, $"{window}&state=planned")).ShouldBe([planned]);
        (await MineAsync(ana, $"{window}&state=draft")).ShouldBe([draft]);
        (await MineAsync(ana, $"{window}&state=cancelled")).ShouldBe([called]);

        var nonsense = await ana.GetAsync($"/api/v1/trip-logs/mine?state=underground");
        nonsense.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await nonsense.Content.ReadAsStringAsync()).ShouldContain("trip_log.state_invalid");
    }

    /// <summary>
    /// Being on a trip is not a right to read it. Both trips here name the same plain reader, both
    /// are private, and exactly one of them carries a read for her — so the list holds one and not
    /// the other, and the difference between them is the grant and nothing else.
    /// </summary>
    /// <remarks>
    /// The reader holds no editing membership on purpose. The group every ordinary account is put
    /// in reads past visibility at the widest scope, so an account that "cannot see" a trip while
    /// holding it would prove nothing whatever.
    /// </remarks>
    [Fact]
    public async Task A_trip_the_caller_may_not_read_is_not_on_their_list_even_though_they_are_on_it()
    {
        var open = await CreateAsync(ana, "Readable", "2046-09-04", readerCaver);
        var shut = await CreateAsync(ana, "Unreadable", "2046-09-05", readerCaver);

        await GrantReadAsync(open, readerId);

        // The negative is only worth anything beside the positive, and beside the proof that both
        // trips really do name her.
        (await reader.GetAsync($"/api/v1/trip-logs/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/trip-logs/{shut}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var hers = await MineAsync(reader, "from=2046-01-01&to=2046-12-31&pageSize=200");
        hers.ShouldBe([open]);

        // And the count agrees with the rows. A total taken past the visibility walk would say how
        // many trips she was not shown, which is the same disclosure by a slower road.
        var body = await BodyAsync(reader, "from=2046-01-01&to=2046-12-31&pageSize=200");
        body.GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    /// <summary>Without an account there is nobody for the listing to be about.</summary>
    [Fact]
    public async Task Anonymous_is_refused()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-logs/mine")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        ana?.Dispose();
        bogdan?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }

    private async Task<Guid> CreateAsync(
        HttpClient client, string title, string date, Guid caverId, string? end = null,
        Guid? alsoOnRoster = null)
    {
        var roster = alsoOnRoster is { } second
            ? new[] { new { caverId }, new { caverId = second } }
            : new[] { new { caverId } };

        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = date,
            tripDateEnd = end,
            participants = roster,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task InviteAsync(Guid tripId, Guid caverId)
    {
        var invited = await ana.PostAsJsonAsync(
            $"/api/v1/trip-logs/{tripId}/invitations/", new { caverId });
        invited.StatusCode.ShouldBe(
            HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());
    }

    private async Task AnswerAsync(Guid tripId, Guid caverId, string response)
    {
        var answered = await bogdan.PutAsJsonAsync(
            $"/api/v1/trip-logs/{tripId}/invitations/{caverId}/response", new { response });
        answered.StatusCode.ShouldBe(
            HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
    }

    private async Task MoveAsync(Guid tripId, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> BodyAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/mine?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<List<Guid>> MineAsync(HttpClient client, string query)
    {
        var body = await BodyAsync(client, query);
        var items = body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        // The total is counted over the same narrowed query the rows come from, so a page that
        // holds everything must agree with it. A count that moved while the rows did not is the
        // shape every disclosure in this area takes.
        if (items.Count < body.GetProperty("totalItems").GetInt32())
        {
            throw new InvalidOperationException("the listing paged; widen pageSize in the test");
        }

        return items;
    }

    private async Task GrantReadAsync(Guid tripId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is reserved for
            // the feature-domain foreign key.
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CaverOfAsync(SilexGisDbContext db, Guid userId) =>
        (await db.Cavers.FirstAsync(c => c.UserId == userId)).Id;
}
