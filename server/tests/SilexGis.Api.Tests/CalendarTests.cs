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
/// The calendar: the dated records a caller may read, over a window of days.
/// </summary>
/// <remarks>
/// <para>
/// The case this file exists for is the first one. Keeping a draft off a calendar is a decision
/// about what a surface shows, and it must not become a decision about who may read a row —
/// those are two questions, answered in two places, and the day they can disagree the
/// disagreement is a disclosure rather than a bug somebody notices. So every assertion that a
/// draft is absent from the calendar is made beside an assertion that the same account still
/// opens it, still finds it on the trip list, and is refused none of what it was allowed before.
/// </para>
/// <para>
/// The account the negative cases are about is a plain reader holding nothing but what a test
/// grants her. The ordinary editing membership reads past visibility at the widest scope, so an
/// "she cannot see it" assertion made with one of those proves nothing at all.
/// </para>
/// <para>
/// Every window here is far enough out that no other test class's rows fall in it, so the
/// assertions are on exact sets rather than deltas around a baseline: the PostGIS container is
/// shared by the whole collection.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CalendarTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private readonly List<HttpClient> clubmates = [];

    private HttpClient ana = null!;    // Editor; writes the rows
    private HttpClient reader = null!; // Viewer holding nothing except what a test grants
    private Guid readerId;
    private Guid anaId;
    private Guid anaCaver;

    public CalendarTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        anaId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cal-ana-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cal-rdr-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            anaCaver = await db.Cavers.Where(c => c.UserId == anaId).Select(c => c.Id).SingleAsync();
        }

        ana = await AuthHelper.BearerClientAsync(factory, $"cal-ana-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cal-rdr-{suffix}@t.local");
    }

    /// <summary>Without an account there is nobody for the calendar to be about.</summary>
    [Fact]
    public async Task Anonymous_is_refused()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/calendar?from=2051-01-01&to=2051-01-31")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The window is what makes the merge exact and what bounds the answer, so it is required
    /// rather than convenient, and a window that cannot mean anything is refused with a code of
    /// its own rather than clamped. A caller who asked for five years and silently received
    /// fourteen months has an answer that looks whole.
    /// </summary>
    [Fact]
    public async Task The_window_is_required_and_a_backwards_or_oversized_one_is_refused()
    {
        (await CodeAsync(ana, "")).ShouldBe("calendar.window_required");
        (await CodeAsync(ana, "from=2051-01-01")).ShouldBe("calendar.window_required");
        (await CodeAsync(ana, "to=2051-12-31")).ShouldBe("calendar.window_required");
        (await CodeAsync(ana, "from=2051-03-01&to=2051-02-01")).ShouldBe("calendar.window_inverted");
        (await CodeAsync(ana, "from=2051-01-01&to=2053-01-01")).ShouldBe("calendar.window_too_wide");

        // The positive beside them, so a route refusing everything could not pass. A window of a
        // single day is legal, and so is the widest one allowed.
        (await ana.GetAsync("/api/v1/calendar?from=2051-01-01&to=2051-01-01")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await ana.GetAsync("/api/v1/calendar?from=2051-01-01&to=2052-02-03")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The case this file exists for. A draft is kept off the calendar, and that changes nothing
    /// whatever about who may read it: the same account, in the same request, still opens the
    /// draft's own page and still finds it on the trip list. Keeping a row off a grid is a
    /// display rule; deciding who may read it is a permission, and only one of them is answered
    /// by what state a row is in.
    /// </summary>
    /// <remarks>
    /// Deliberately asserted through a plain reader who holds an explicit grant on the draft
    /// rather than through its author. An author sees their own row by owning it, so the two
    /// questions would never be separable; a granted reader is entitled to the row by exactly
    /// one rule, which is the rule the calendar must not be a second copy of.
    /// </remarks>
    [Fact]
    public async Task A_draft_is_off_the_calendar_and_still_read_everywhere_it_was_read_before()
    {
        var window = "from=2052-03-01&to=2052-03-31";

        var draft = await TripAsync("Cal draft", "2052-03-10");
        var planned = await TripAsync("Cal planned", "2052-03-11");
        await MoveAsync(planned, "planned");

        await GrantTripReadAsync(draft);
        await GrantTripReadAsync(planned);

        // She may read both — the grant, and nothing about their states, is what decides that.
        (await reader.GetAsync($"/api/v1/trip-logs/{draft}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/trip-logs/{planned}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The calendar shows one of them and not the other…
        var onTheCalendar = await IdsAsync(reader, window);
        onTheCalendar.ShouldBe([planned]);

        // …and the surfaces that always listed the draft still list it, for the same account in
        // the same state of the world. Without this half, keeping a draft off a grid would be
        // indistinguishable from having quietly made it unreadable.
        var listed = await TripListIdsAsync(reader, "from=2052-03-01&to=2052-03-31&pageSize=200");
        listed.ShouldContain(draft);
        listed.ShouldContain(planned);

        // And nothing about the calendar's rule leaked into the count the list reports either.
        var listBody = await ana.GetFromJsonAsync<JsonElement>(
            "/api/v1/trip-logs?from=2052-03-01&to=2052-03-31&pageSize=200");
        listBody.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ShouldContain(draft);
    }

    /// <summary>
    /// Each state lands where the rule says, and the answer says which half it landed on, so a
    /// grid does not have to work the rule out a second time. A row put back is on the record
    /// with a postponed mark and deliberately not in a day cell: the date it still carries is not
    /// one anybody is going on.
    /// </summary>
    [Fact]
    public async Task Each_state_lands_on_the_half_the_rule_names_and_a_draft_lands_nowhere()
    {
        var window = "from=2053-04-01&to=2053-04-30";

        var draft = await TripAsync("Cal s-draft", "2053-04-01");
        var proposed = await TripAsync("Cal s-proposed", "2053-04-02");
        var planned = await TripAsync("Cal s-planned", "2053-04-03");
        var confirmed = await TripAsync("Cal s-confirmed", "2053-04-04");
        var done = await TripAsync("Cal s-done", "2053-04-05");
        var published = await TripAsync("Cal s-published", "2053-04-06");
        var cancelled = await TripAsync("Cal s-cancelled", "2053-04-07");
        var delayed = await TripAsync("Cal s-delayed", "2053-04-08");

        await MoveAsync(proposed, "proposed");
        await MoveAsync(planned, "planned");
        await MoveAsync(confirmed, "planned");
        await MoveAsync(confirmed, "confirmed");
        await MoveAsync(done, "done");
        await MoveAsync(published, "done");
        await MoveAsync(published, "published");
        await MoveAsync(cancelled, "cancelled");
        await MoveAsync(delayed, "planned");
        await MoveAsync(delayed, "delayed");

        var rows = (await EntriesAsync(ana, window))
            .ToDictionary(x => x.GetProperty("id").GetGuid(), Placement);

        rows.ShouldNotContainKey(draft, "a draft has been shown to nobody");
        rows[proposed].ShouldBe("ahead");
        rows[planned].ShouldBe("ahead");
        rows[confirmed].ShouldBe("ahead");
        rows[done].ShouldBe("behind");
        rows[published].ShouldBe("behind");

        // Shown and marked rather than hidden: the person who was going on it is the reader who
        // most needs to notice, and a calendar that hid it would disagree with the trip's page.
        rows[cancelled].ShouldBe("calledOff");
        rows[delayed].ShouldBe("putBack");

        // …and it can be narrowed away by somebody who wants only what is going ahead.
        (await IdsAsync(ana, $"{window}&includeCancelled=false")).ShouldNotContain(cancelled);
        (await IdsAsync(ana, $"{window}&includeCancelled=false")).ShouldContain(planned);

        // A state a calendar never shows is refused rather than answered with an empty list — a
        // caller who asked for something that cannot appear wants to be told — and so is a word
        // this application does not have.
        (await CodeAsync(ana, $"{window}&state=draft")).ShouldBe("calendar.state_invalid");
        (await CodeAsync(ana, $"{window}&state=underground")).ShouldBe("calendar.state_invalid");
        (await IdsAsync(ana, $"{window}&state=planned")).ShouldBe([planned]);
    }

    /// <summary>
    /// A calendar row names no cave and carries nothing derived from one. Naming a cave is a read
    /// of the cave, decided by a walk of its own that also has to say how many were held back;
    /// a row that names none owes neither. Asserted as an absence rather than assumed, so that
    /// adding such a field later fails a test rather than passing a review.
    /// </summary>
    [Fact]
    public async Task No_cave_derived_field_reaches_a_row_and_no_row_carries_a_position()
    {
        var window = "from=2054-05-01&to=2054-05-31";
        var caveId = await CaveAsync();
        var tripId = await TripAsync("Cal caved", "2054-05-12", caveId);
        await MoveAsync(tripId, "planned");

        // The trip really is linked to the cave, so the absence below is the row's shape and not
        // a trip that happened to have nothing on it.
        var full = await ana.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        full.GetProperty("caveIds").EnumerateArray().Select(x => x.GetGuid()).ShouldContain(caveId);

        var row = (await EntriesAsync(ana, window)).Single(x => x.GetProperty("id").GetGuid() == tripId);

        foreach (var name in new[]
        {
            "caveIds", "caves", "cave", "caveId", "cavesWithheld", "caveNames",
            "participants", "roster", "checklistReadiness", "safety", "sections",
            "geom", "geometry", "meetingGeom", "center", "coordinates", "locationText",
        })
        {
            row.TryGetProperty(name, out _)
                .ShouldBeFalse($"a calendar row must not carry '{name}'");
        }

        // What it does carry about position is whether there is one, never where it is.
        row.GetProperty("hasPosition").GetBoolean().ShouldBeFalse();

        // And the shape is closed, not merely free of the names above. Every member is here
        // because somebody argued a calendar needs it, so a field arriving without that argument
        // fails here rather than passing a review — which is the whole point, since the field
        // most likely to arrive is a cave, and the row that names one owes the cave's own
        // disclosure walk and a count of what that walk held back. Widening this list is the
        // deliberate act of saying the new member has been thought about.
        row.EnumerateObject().Select(p => p.Name).ShouldBe(
            [
                "source", "id", "title", "start", "end", "startTime", "endTime",
                "state", "placement", "cavingGroupId", "hasPosition",
            ],
            ignoreOrder: true,
            "a calendar row carries exactly what a calendar was argued to need");
    }

    /// <summary>
    /// The row's title is the row's own title, as written. Who may read the row was decided
    /// before the row was built, and it is shown to nobody else; rewriting it for a reader
    /// entitled to the row would withhold from them what the row's own page hands over.
    /// </summary>
    /// <remarks>
    /// The fixture is the contested one, deliberately: the trip names a cave whose location is
    /// protected and which this reader may not open at all, and she holds a read on the trip and
    /// on nothing else. That is the only arrangement in which a rule that rewrote a title — to
    /// keep a cave's name out of a calendar — would take effect, so it is the only arrangement in
    /// which this assertion can fail. A version of this test whose trip named no cave would go on
    /// passing while exactly the thing it exists to forbid was added.
    /// </remarks>
    [Fact]
    public async Task A_row_carries_the_title_as_written()
    {
        var window = "from=2055-06-01&to=2055-06-30";
        var title = $"Peștera Neagră recce {Guid.NewGuid():N}";

        var caveId = await CaveAsync(locationProtected: true);
        var tripId = await TripAsync(title, "2055-06-09", caveId, raw: true);
        await MoveAsync(tripId, "planned");
        await GrantTripReadAsync(tripId);

        // The read she was given is over the trip and reaches no further: the cave the trip names
        // is shut to her, which is the state the title question was ever asked about.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode
            .ShouldNotBe(HttpStatusCode.OK, "the reader must not be able to open the cave");

        var row = (await EntriesAsync(reader, window)).Single(x => x.GetProperty("id").GetGuid() == tripId);
        row.GetProperty("title").GetString().ShouldBe(title);
    }

    /// <summary>
    /// The calendar holds what this caller may open and nothing else, and the shortfall it
    /// reports is a count of that same answer. A count taken before the visibility walk would
    /// tell somebody how much they are not being shown, which is the disclosure by a slower road.
    /// </summary>
    /// <remarks>
    /// Both rows here name nothing the reader holds; exactly one of them carries a read for her,
    /// and the difference between the two is that grant and nothing else.
    /// </remarks>
    [Fact]
    public async Task The_calendar_holds_only_what_the_reader_may_open_and_counts_only_that()
    {
        var window = "from=2056-07-01&to=2056-07-31";

        var open = await TripAsync("Cal open", "2056-07-04");
        var shut = await TripAsync("Cal shut", "2056-07-05");
        await MoveAsync(open, "planned");
        await MoveAsync(shut, "planned");
        await GrantTripReadAsync(open);

        // Its author reads both, so the pair really does exist and really is in the window.
        (await IdsAsync(ana, window)).ShouldBe([open, shut]);

        (await reader.GetAsync($"/api/v1/trip-logs/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/trip-logs/{shut}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var hers = await BodyAsync(reader, window);
        hers.GetProperty("entries").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ShouldBe([open]);

        // Nothing was held back by the cap, and the answer says so with a number rather than by
        // leaving the field out — zero here is a real count and not a shrug.
        hers.GetProperty("omitted").GetInt32().ShouldBe(0);
        (await BodyAsync(ana, window)).GetProperty("omitted").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// A camp is a second source rather than something derived from the trips inside it, and the
    /// window takes any row whose span overlaps it rather than only rows that begin inside it.
    /// </summary>
    [Fact]
    public async Task Camps_are_a_second_source_and_a_span_across_the_edge_is_in_the_window()
    {
        var window = "from=2057-08-10&to=2057-08-20";

        var camp = await CampAsync("Cal camp", "2057-08-05", "2057-08-14");
        await MoveCampAsync(camp, "planned");
        var trip = await TripAsync("Cal camp trip", "2057-08-12");
        await MoveAsync(trip, "planned");

        var rows = await EntriesAsync(ana, window);
        rows.Select(x => x.GetProperty("id").GetGuid()).ShouldBe([camp, trip], ignoreOrder: true);
        rows.Single(x => x.GetProperty("id").GetGuid() == camp)
            .GetProperty("source").GetString().ShouldBe("expedition");
        rows.Single(x => x.GetProperty("id").GetGuid() == camp)
            .GetProperty("end").GetString().ShouldBe("2057-08-14");

        // A camp that ended before the window opened is not in it, which is what proves the
        // overlap above was the rule working rather than the window catching everything.
        var earlier = await CampAsync("Cal earlier camp", "2057-07-01", "2057-07-08");
        await MoveCampAsync(earlier, "planned");
        (await IdsAsync(ana, window)).ShouldNotContain(earlier);

        // Narrowing to one family answers with that family alone.
        (await IdsAsync(ana, $"{window}&source=expedition")).ShouldBe([camp]);
        (await IdsAsync(ana, $"{window}&source=tripLog")).ShouldBe([trip]);
        (await CodeAsync(ana, $"{window}&source=meetings")).ShouldBe("calendar.source_invalid");
    }

    /// <summary>
    /// The order is asked for by name from a fixed set, and an order this code does not know
    /// falls back to the calendar's own rather than being refused: nothing is at stake in the
    /// arrangement of rows the caller may already read.
    /// </summary>
    [Fact]
    public async Task The_order_is_one_of_a_fixed_set_and_an_unknown_one_falls_back()
    {
        var window = "from=2058-09-01&to=2058-09-30";
        var marker = Guid.NewGuid().ToString("N")[..6];

        var first = await TripAsync($"AAA {marker}", "2058-09-03", raw: true);
        var second = await TripAsync($"ZZZ {marker}", "2058-09-02", raw: true);
        await MoveAsync(first, "planned");
        await MoveAsync(second, "planned");

        (await IdsAsync(ana, window)).ShouldBe([second, first]);
        (await IdsAsync(ana, $"{window}&sort=-start")).ShouldBe([first, second]);
        (await IdsAsync(ana, $"{window}&sort=title")).ShouldBe([first, second]);
        (await IdsAsync(ana, $"{window}&sort=-title")).ShouldBe([second, first]);
        (await IdsAsync(ana, $"{window}&sort=cavesWithheld")).ShouldBe([second, first]);
    }

    /// <summary>
    /// "Mine" is worked out from the request's own account and takes no argument naming a person,
    /// so there is no way to ask this surface where somebody else has been. A camp records nobody
    /// as being on it, so the narrowing empties that source rather than guessing from the trips
    /// inside it — which is stated here rather than left to be found.
    /// </summary>
    [Fact]
    public async Task Mine_narrows_to_the_trips_the_caller_is_on_and_names_nobody()
    {
        var window = "from=2060-10-01&to=2060-10-31";

        var hers = await TripAsync("Cal mine", "2060-10-04");
        var notHers = await TripAsync("Cal not mine", "2060-10-05", onIt: false);
        var camp = await CampAsync("Cal mine camp", "2060-10-06", "2060-10-07");
        await MoveAsync(hers, "planned");
        await MoveAsync(notHers, "planned");
        await MoveCampAsync(camp, "planned");

        // She reads all three — the narrowing below is about being on them, not about reading them.
        (await IdsAsync(ana, window)).ShouldBe([hers, notHers, camp]);

        (await IdsAsync(ana, $"{window}&mine=true")).ShouldBe([hers]);
    }

    /// <summary>
    /// A group's calendar is a filter laid on top of the reader's own visibility, never a second
    /// way in. Naming a group can only ever take rows away from what that reader could already
    /// read: a trip the group is running whose audience does not admit her stays out, and asking
    /// for the group by name does not change that. The positive half is asserted in the same
    /// test, because an endpoint that answered nothing at all would satisfy every negative here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two accounts are both plain readers — one in the group, one in nothing — and neither
    /// holds an editing membership, which reads past visibility at the widest scope by design and
    /// would make either answer meaningless.
    /// </para>
    /// <para>
    /// It also settles which of a trip's two group columns a group calendar asks. A trip records
    /// separately who is running it and who may read it, and they can be different groups. "The
    /// club's calendar" means the trips the club is running, so it is the organising column that
    /// answers — a trip somebody else is running that the club merely happens to be allowed to
    /// read is on that other group's calendar, not on the club's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_group_calendar_narrows_what_a_reader_may_already_read_and_never_widens_it()
    {
        var window = "from=2061-11-01&to=2061-11-30";
        var club = await ClubAsync("Cal club");
        var otherClub = await ClubAsync("Cal other club");
        var clubmate = await ClubmateAsync(club);

        // Run by the club and readable by its members.
        var theirs = await TripAsync("Cal club trip", "2061-11-04", runBy: club, readableBy: club,
            visibility: "cavingGroup");

        // Run by the club and readable by nobody but the account that wrote it. This is the row
        // the whole test turns on: if the group parameter were answering who may read a row
        // rather than narrowing rows already read, this is what would appear.
        var theirsAndShut = await TripAsync("Cal club shut", "2061-11-05", runBy: club);

        // Readable by every account and run by nobody in particular, so it shows the parameter
        // taking a row away rather than merely rearranging the answer.
        var everyones = await TripAsync("Cal open", "2061-11-06", visibility: "authenticated");

        // Run by the other club but readable by this one's members — the pair that decides which
        // of the two columns a group calendar asks.
        var runElsewhere = await TripAsync("Cal elsewhere", "2061-11-07", runBy: otherClub,
            readableBy: club, visibility: "cavingGroup");

        var theirCamp = await CampAsync("Cal club camp", "2061-11-08", "2061-11-09", club,
            visibility: "cavingGroup");

        foreach (var id in new[] { theirs, theirsAndShut, everyones, runElsewhere })
        {
            await MoveAsync(id, "planned");
        }

        await MoveCampAsync(theirCamp, "planned");

        // What the member may read at all, before any group is named: everything bound to her
        // club and the one open to every account. Not the trip her club is running privately.
        (await IdsAsync(clubmate, window))
            .ShouldBe([theirs, everyones, runElsewhere, theirCamp], ignoreOrder: true);
        (await clubmate.GetAsync($"/api/v1/trip-logs/{theirsAndShut}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // The club's calendar is that same answer narrowed to what the club is running. The one
        // open to everybody has gone, which is the narrowing; the private one is still absent,
        // which is the rule that naming a group opens nothing; and the trip the other club is
        // running has gone too, because a group's calendar is the trips it runs and not the trips
        // it is allowed to read.
        var hers = await BodyAsync(clubmate, $"{window}&cavingGroupId={club}");
        hers.GetProperty("entries").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())
            .ShouldBe([theirs, theirCamp], ignoreOrder: true);
        hers.GetProperty("omitted").GetInt32().ShouldBe(0);

        (await IdsAsync(clubmate, $"{window}&cavingGroupId={otherClub}")).ShouldBe([runElsewhere]);

        // And the same request from an account that belongs to no group and holds no grant on any
        // of these rows answers with nothing of the club's — while still answering, so this is a
        // narrowing of her own answer and not a refusal of the surface.
        (await IdsAsync(reader, window)).ShouldBe([everyones]);
        (await IdsAsync(reader, $"{window}&cavingGroupId={club}")).ShouldBeEmpty();
        (await reader.GetAsync($"/api/v1/trip-logs/{theirs}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // The author reads all of it, so every row above really exists and really falls in the
        // window: the absences are the walk and the filter working, not an empty month.
        (await IdsAsync(ana, window))
            .ShouldBe([theirs, theirsAndShut, everyones, runElsewhere, theirCamp], ignoreOrder: true);

        // A group nobody has ever created narrows to nothing rather than being refused. There is
        // nothing to tell apart: an identifier for a group that does not exist and one for a
        // group holding nothing this caller may read are the same empty answer, and answering
        // them differently would say which groups exist to somebody who may not be told.
        (await IdsAsync(ana, $"{window}&cavingGroupId={Guid.CreateVersion7()}")).ShouldBeEmpty();
    }

    /// <summary>
    /// Asking for only what is still to come narrows the window to begin today rather than
    /// filtering on state, because whether a date has passed is a question about the date. Days
    /// are the rows' own calendar days read in UTC, which is the only clock this application
    /// stores.
    /// </summary>
    [Fact]
    public async Task What_is_still_to_come_is_the_window_narrowed_rather_than_the_states_filtered()
    {
        var lastYear = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-200);
        var window = $"from={lastYear:yyyy-MM-dd}&to={lastYear.AddDays(3):yyyy-MM-dd}";

        var past = await TripAsync("Cal past", $"{lastYear.AddDays(1):yyyy-MM-dd}");
        await MoveAsync(past, "planned");

        (await IdsAsync(ana, window)).ShouldContain(past);
        (await IdsAsync(ana, $"{window}&includePast=false")).ShouldBeEmpty();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        ana?.Dispose();
        reader?.Dispose();
        foreach (var clubmate in clubmates)
        {
            clubmate.Dispose();
        }

        factory.Dispose();
    }

    private static string Placement(JsonElement row) => row.GetProperty("placement").GetString()!;

    private static async Task<JsonElement> BodyAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/calendar?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<List<JsonElement>> EntriesAsync(HttpClient client, string query) =>
        [.. (await BodyAsync(client, query)).GetProperty("entries").EnumerateArray()];

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string query) =>
        [.. (await EntriesAsync(client, query)).Select(x => x.GetProperty("id").GetGuid())];

    private static async Task<string?> CodeAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/calendar?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString();
    }

    private static async Task<List<Guid>> TripListIdsAsync(HttpClient client, string query)
    {
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs?{query}");
        return [.. body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    /// <summary>
    /// A trip written by the editing account. The two group arguments are separate because a
    /// trip's two group columns mean different things: one records who is running it, the other
    /// who may read it, and a test about group calendars has to be able to set them apart.
    /// </summary>
    private async Task<Guid> TripAsync(
        string title,
        string date,
        Guid? caveId = null,
        bool raw = false,
        bool onIt = true,
        Guid? runBy = null,
        Guid? readableBy = null,
        string visibility = "private")
    {
        var response = await ana.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = raw ? title : $"{title} {Guid.NewGuid():N}",
            tripDate = date,
            caveIds = caveId is { } cave ? new[] { cave } : [],
            participants = onIt ? new[] { new { caverId = anaCaver } } : [],
            organizingCavingGroupId = runBy,
            cavingGroupId = readableBy,
            visibility,
        });
        return await CreatedIdAsync(response);
    }

    /// <summary>
    /// A caving group whose only member is the account that writes the rows. It has to be a
    /// member: binding a row to a group hands that group's members whatever their rules grant
    /// over the group's rows, so the application refuses a binding from somebody outside it —
    /// which is exactly how a club's own trips come to be written, by one of its own. Readers
    /// are added one at a time by the tests that need them, so an account's reach is what the
    /// test wrote and never what a fixture left.
    /// </summary>
    private async Task<Guid> ClubAsync(string name)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var club = new CavingGroup { Name = $"{name} {suffix}", Slug = $"cal-club-{suffix}" };
        db.CavingGroups.Add(club);
        await db.SaveChangesAsync();
        await RosterHelper.AddMemberAsync(db, club.Id, anaId);
        return club.Id;
    }

    /// <summary>
    /// A plain account belonging to a caving group. A Viewer deliberately: the ordinary editing
    /// membership reads past visibility at the widest scope by design, so a test using one to
    /// show what a group's members may see would be showing what an editor may see.
    /// </summary>
    private async Task<HttpClient> ClubmateAsync(Guid clubId)
    {
        var email = $"cal-mate-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await RosterHelper.AddMemberAsync(db, clubId, userId);
        }

        var client = await AuthHelper.BearerClientAsync(factory, email);
        clubmates.Add(client);
        return client;
    }

    /// <summary>
    /// A camp written by the editing account. A camp carries one group column, which is both
    /// whose camp it is and who may read it, so there is only one argument here to a trip's two.
    /// </summary>
    private async Task<Guid> CampAsync(
        string name, string start, string end, Guid? club = null, string visibility = "private")
    {
        var response = await ana.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "A camp.",
            startDate = start,
            endDate = end,
            geom = (object?)null,
            cavingGroupId = club,
            visibility,
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CaveAsync(bool locationProtected = false)
    {
        long caveTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
        }

        var response = await ana.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cal cave {Guid.NewGuid():N}",
            caveTypeId,
            visibility = "private",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(response);
    }

    private async Task MoveAsync(Guid tripId, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task MoveCampAsync(Guid campId, string state)
    {
        var moved = await ana.PostWithIfMatchAsync($"/api/v1/expeditions/{campId}/state", new { state });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    private async Task GrantTripReadAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = readerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }
}
