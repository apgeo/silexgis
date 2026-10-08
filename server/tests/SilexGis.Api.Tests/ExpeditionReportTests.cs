// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
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
/// A camp's write-up: one document over a fortnight, where a trip's is one over an afternoon.
///
/// It is the widest single thing this application produces — twenty trips, their caves, their
/// people, their photographs, in one file that then leaves and is forwarded and opened long after
/// the rules that produced it changed. And a camp is routinely readable by a wider audience than
/// some of the trips gathered into it, which is the case that has no trip analogue at all: the
/// document must state the sum of what its producer may read and nothing more, and a filed copy —
/// which every reader of the camp reaches — must state the sum of what any account may read.
///
/// So each case here is stated over the bytes of the produced document rather than over the code
/// that produced them, and each asserts both halves over one fixture: what the entitled caller's
/// copy carries and what the same request hands somebody not entitled to it. A document that had
/// quietly stopped carrying anything would satisfy a "does not contain" assertion perfectly.
///
/// The withheld side is always a Viewer with no grant. The seeded Editors group reads past
/// visibility by design, so an Editor who cannot see something proves nothing about the rule.
/// </summary>
public sealed class ExpeditionReportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;   // Editor — creates the camp and its trips
    private HttpClient reader = null!;  // Viewer — reads only what it is given
    private HttpClient admin = null!;   // writes the club's own layouts
    private Guid readerId;

    public ExpeditionReportTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-camprep-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cmp-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"cmp-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cmp-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cmp-read-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"cmp-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"cmp-adm-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    /// <summary>
    /// The whole point of the feature: a fortnight comes out as one document that reads day by
    /// day, not as twenty write-ups stapled together.
    /// </summary>
    [Fact]
    public async Task A_camp_comes_out_as_one_document_that_reads_day_by_day()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var first = await CreateTripAsync(owner, "Morning shaft", new DateOnly(2026, 7, 3), Visibility.Authenticated);
        var second = await CreateTripAsync(owner, "Sump push", new DateOnly(2026, 7, 5), Visibility.Authenticated);
        await JoinAsync(owner, campId, first);
        await JoinAsync(owner, campId, second);

        var text = await DocumentTextAsync(owner, campId);

        text.ShouldContain($"Fortnight {suffix}");
        text.ShouldContain("Morning shaft");
        text.ShouldContain("Sump push");
        text.ShouldContain("2 trips");

        // Day by day, which is the shape a camp has and a trip does not: each day something ran
        // on is its own line, and a day nothing ran on is not printed at all.
        text.ShouldContain("2026-07-03");
        text.ShouldContain("2026-07-05");
        text.ShouldNotContain("2026-07-04");
    }

    /// <summary>
    /// A camp is readable by more people than the trips inside it are, and the document is the sum
    /// of what its producer may read. A trip a reader may not open contributes nothing to it — not
    /// its title, not its date, and not a place in the count.
    /// </summary>
    [Fact]
    public async Task A_trip_the_reader_may_not_open_contributes_nothing_to_their_copy()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var open = await CreateTripAsync(owner, "Open survey", new DateOnly(2026, 7, 3), Visibility.Authenticated);
        var closed = await CreateTripAsync(owner, "Private dig", new DateOnly(2026, 7, 4), Visibility.Private);
        await JoinAsync(owner, campId, open);
        await JoinAsync(owner, campId, closed);

        // The withheld side: a Viewer holding nothing over the private trip. Its own owner's copy
        // is asserted below over the same fixture, so this is a statement about the caller and not
        // about a document that came out empty.
        var withheld = await DocumentTextAsync(reader, campId);
        withheld.ShouldContain("Open survey");
        withheld.ShouldNotContain("Private dig");
        withheld.ShouldContain("1 trip");
        withheld.ShouldNotContain("2 trips");

        var entitled = await DocumentTextAsync(owner, campId);
        entitled.ShouldContain("Open survey");
        entitled.ShouldContain("Private dig");
        entitled.ShouldContain("2 trips");
    }

    /// <summary>
    /// A cave whose position is protected is not named on the write-up of a caller who may not be
    /// told where it is — the same withholding the trip's own page, the camp's map and the leads
    /// board apply to the same caller.
    /// </summary>
    /// <remarks>
    /// Being able to read the cave is not enough. "This trip reached that cave" places the cave by
    /// proximity, because the trip carries its own exact geometry and its own page is open to the
    /// same reader; so the pairing is withheld wherever the position would be. A document is the
    /// worst place to get this wrong: it is forwarded and opened long after the rules that made it.
    ///
    /// The withheld side is a Viewer with the cave read granted and nothing else, so the absence
    /// below is the protection rule and not a missing link or a cave nobody may read.
    /// </remarks>
    [Fact]
    public async Task A_cave_the_reader_may_not_place_is_not_named_on_their_copy()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);

        var guardedName = $"Guarded sump cave {suffix}";
        var openName = $"Ordinary cave {suffix}";
        var guarded = await CreateCaveAsync(guardedName, locationProtected: true);
        var ordinary = await CreateCaveAsync(openName, locationProtected: false);
        var tripId = await CreateTripAboutCavesAsync(
            "Sump push", new DateOnly(2026, 7, 3), guarded, ordinary);
        await JoinAsync(owner, campId, tripId);

        // A cave nobody protected is named as it always was, so the absence below is the
        // protection rule rather than a link the document never resolved.
        var withheld = await DocumentTextAsync(reader, campId);
        withheld.ShouldContain(openName);
        withheld.ShouldNotContain(guardedName);

        // The entitled half, over the same fixture: told where it is, told which trip reached it.
        await GrantAsync("feature", guarded, "Read,ViewExactLocation");
        (await DocumentTextAsync(reader, campId)).ShouldContain(guardedName);
        (await DocumentTextAsync(owner, campId)).ShouldContain(guardedName);
    }

    /// <summary>
    /// A camp's totals are the camp's: the people are counted distinctly across its trips, and the
    /// hours are worked out by the rule that knows a trip can end the following morning.
    /// </summary>
    /// <remarks>
    /// Both figures have a plausible wrong answer that looks right. The largest single party is a
    /// count of one afternoon, not of the camp; and an exit time earlier than the entry time reads
    /// as a negative unless something knows it means the next morning, which turns a fortnight of
    /// night pushes into no hours at all — and a dropped figure reads as "no times were recorded"
    /// rather than as a number anybody would question.
    /// </remarks>
    [Fact]
    public async Task The_totals_count_the_camp_rather_than_its_largest_afternoon()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var night = await CreateTripAsync(
            owner, "Night push", new DateOnly(2026, 7, 3), Visibility.Authenticated,
            entryTime: "22:00", exitTime: "04:00");
        var day = await CreateTripAsync(
            owner, "Day push", new DateOnly(2026, 7, 4), Visibility.Authenticated,
            entryTime: "09:00", exitTime: "11:00");
        await JoinAsync(owner, campId, night);
        await JoinAsync(owner, campId, day);

        var text = await DocumentTextAsync(owner, campId);

        // Six hours over the midnight, two hours the next day: eight, not the two the naive
        // subtraction leaves once the night has been thrown away.
        text.ShouldContain("8 h");
    }

    /// <summary>
    /// A club's own layout may print the time underground that the member trips' tracking logs
    /// come to, on a line of its own. Three things hold it in place: it is counted only over the
    /// trips this reader may open; a log with an entry nobody closed prints nothing rather than
    /// "0 h"; and the layout the application ships does not print it at all, so no existing
    /// write-up changes.
    /// </summary>
    /// <remarks>
    /// The withheld side is a Viewer holding nothing over the private trip, and the owner's copy
    /// of the same camp through the same layout carries the larger figure — so the smaller one is
    /// the filter at work and not a log that was never written.
    /// </remarks>
    [Fact]
    public async Task A_layout_may_print_the_hours_from_tracking_and_the_shipped_layout_does_not()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var (open, cavers) = await CreateTripWithPeopleAsync(
            "Tracked openly", new DateOnly(2026, 7, 3), ["Ana Pop", "Barbu Ilie"]);
        var closed = await CreateTripAsync(owner, "Tracked privately", new DateOnly(2026, 7, 4), Visibility.Private);
        await JoinAsync(owner, campId, open);
        await JoinAsync(owner, campId, closed);
        var layoutId = await CreateLayoutAsync(
            "expedition", "title: {title}\nfield: Trips = {trips}\nfield: By tracking = {watchhours}\n");

        // An entry and nothing after it: the log exists and says nothing usable about time.
        await ReportAsync(open, cavers[1], TripPositionEventKind.Entered, new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero));
        (await DocumentTextAsync(owner, campId, layoutId)).ShouldNotContain("By tracking");

        // Three and a half hours on the trip anybody may read, five on the private one.
        await ReportAsync(open, cavers[0], TripPositionEventKind.Entered, new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero));
        await ReportAsync(open, cavers[0], TripPositionEventKind.Exited, new DateTimeOffset(2026, 7, 3, 12, 30, 0, TimeSpan.Zero));
        await ReportAsync(closed, cavers[0], TripPositionEventKind.Entered, new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero));
        await ReportAsync(closed, cavers[0], TripPositionEventKind.Exited, new DateTimeOffset(2026, 7, 4, 15, 0, 0, TimeSpan.Zero));

        var entitled = await DocumentTextAsync(owner, campId, layoutId);
        entitled.ShouldContain("By tracking");
        entitled.ShouldContain("8.5 h");

        var withheld = await DocumentTextAsync(reader, campId, layoutId);
        withheld.ShouldContain("By tracking");
        withheld.ShouldContain("3.5 h");
        withheld.ShouldNotContain("8.5 h");

        // The shipped layout, for the caller who may read everything: no such line, no such figure.
        var shipped = await DocumentTextAsync(owner, campId);
        shipped.ShouldContain("2 trips");
        shipped.ShouldNotContain("By tracking");
        shipped.ShouldNotContain("8.5 h");
    }

    /// <summary>
    /// The people on a camp are counted distinctly across its trips, which is what the camp's own
    /// roll-up counts — not the biggest single party, and not the parties added up.
    /// </summary>
    [Fact]
    public async Task The_people_on_a_camp_are_counted_once_each_across_its_trips()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var (first, cavers) = await CreateTripWithPeopleAsync(
            "First push", new DateOnly(2026, 7, 3), ["Ana Pop", "Barbu Ilie", "Corina Radu"]);

        // One of the three again, plus somebody new. That makes the three candidate answers all
        // different — four distinct people, five party places, three in the largest single party —
        // so only the right one satisfies this.
        var second = await CreateTripSharingPeopleAsync(
            "Second push", new DateOnly(2026, 7, 4), [cavers[0]], "Dan Vlad");

        await JoinAsync(owner, campId, first);
        await JoinAsync(owner, campId, second);

        var text = await DocumentTextAsync(owner, campId);
        text.ShouldContain("4 people");
        text.ShouldNotContain("5 people");
    }

    /// <summary>
    /// The camp's own read decides whether there is a document at all, and the refusal is the one
    /// the page gives: a camp somebody may not read is not there.
    /// </summary>
    [Fact]
    public async Task A_camp_the_caller_may_not_read_has_no_write_up()
    {
        var campId = await CreateCampAsync(owner, Visibility.Private);

        using var refused = await reader.GetAsync($"/api/v1/expeditions/{campId}/report");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // …and the owner's own copy is produced, so the refusal is about the caller rather than
        // about a route that answers nothing to anybody.
        (await DocumentTextAsync(owner, campId)).ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A layout is written for one kind of thing. Naming a camp's layout when writing up a trip —
    /// or the other way round — is a refusal, never a quiet fall back to some other layout, because
    /// somebody handed a document in a layout they did not ask for would not be told.
    /// </summary>
    [Fact]
    public async Task A_layout_written_for_the_other_kind_is_refused_rather_than_swapped()
    {
        var campLayoutId = await CreateTemplateAsync("expedition");
        var tripLayoutId = await CreateTemplateAsync("trip");
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var tripId = await CreateTripAsync(owner, "Ordinary", new DateOnly(2026, 7, 3), Visibility.Authenticated);

        using var wrongKind = await owner.GetAsync(
            $"/api/v1/expeditions/{campId}/report?templateId={tripLayoutId}");
        wrongKind.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var wrongKindOnTrip = await owner.GetAsync(
            $"/api/v1/trip-logs/{tripId}/report?templateId={campLayoutId}");
        wrongKindOnTrip.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The right kind on the right thing is produced, so the two refusals are about the kind
        // and not about a layout nothing can use.
        using var rightKind = await owner.GetAsync(
            $"/api/v1/expeditions/{campId}/report?templateId={campLayoutId}");
        rightKind.StatusCode.ShouldBe(HttpStatusCode.OK, await rightKind.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A club chooses a camp layout and a trip layout independently: one mark across both would
    /// make choosing one silently unchoose the other, which the database now refuses to allow.
    /// </summary>
    [Fact]
    public async Task Each_kind_has_its_own_chosen_layout()
    {
        await CreateTemplateAsync("trip", isDefault: true);
        await CreateTemplateAsync("expedition", isDefault: true);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var chosen = await db.ReportTemplates.AsNoTracking()
            .Where(x => x.IsDefault)
            .Select(x => x.Kind)
            .ToListAsync();

        chosen.ShouldContain(SilexGis.Domain.Trips.ReportTemplateKind.Trip);
        chosen.ShouldContain(SilexGis.Domain.Trips.ReportTemplateKind.Expedition);
    }

    /// <summary>
    /// Filing the write-up answers to who may change the camp, and what is filed is built from the
    /// reading any account has — because everybody who may read the camp reaches what is filed on
    /// it. Regenerating it supersedes only what this route produced before.
    /// </summary>
    [Fact]
    public async Task Filing_a_write_up_needs_the_right_to_change_the_camp_and_supersedes_its_own()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var tripId = await CreateTripAsync(owner, "Filed trip", new DateOnly(2026, 7, 3), Visibility.Authenticated);
        await JoinAsync(owner, campId, tripId);

        using var refused = await reader.PostAsync($"/api/v1/expeditions/{campId}/report", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var kept = await owner.PostAsync($"/api/v1/expeditions/{campId}/report", null);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());

        using var again = await owner.PostAsync($"/api/v1/expeditions/{campId}/report", null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());

        var filed = await ReportsOnCampAsync(owner, campId);
        filed.Count.ShouldBe(1, string.Join(", ", filed));
        filed[0].ShouldStartWith($"expedition-report-{campId.ToString("N")[..8]}-");
    }

    /// <summary>
    /// A camp's write-up carries what each trip it gathered wrote about itself — its account, its
    /// results and the answers on its form — under the trip's own heading, and says nothing under
    /// a heading for a trip that wrote nothing.
    /// </summary>
    [Fact]
    public async Task A_camp_write_up_carries_what_each_trip_its_reader_may_open_wrote_about_itself()
    {
        var typeId = await CreateTripTypeAsync();
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var written = await CreateWrittenTripAsync("Sump recce", "2026-07-03", Visibility.Authenticated, body =>
        {
            body["tripTypeId"] = typeId;
            body["description"] = "The sump was open after the dry week.";
            body["results"] = "Sixty metres of new passage beyond it.";
            body["fieldData"] = JsonSerializer.SerializeToElement(new { water_level = "Lower than in May" });
        });
        var silent = await CreateTripAsync(owner, "Carry day", new DateOnly(2026, 7, 4), Visibility.Authenticated);
        await JoinAsync(owner, campId, written);
        await JoinAsync(owner, campId, silent);

        // Both callers read the trip, so both are handed its words: this is not a part of the
        // record that answers to a narrower audience than the trip itself.
        foreach (var caller in new[] { owner, reader })
        {
            var text = await DocumentTextAsync(caller, campId);
            text.ShouldContain("The sump was open after the dry week.");
            text.ShouldContain("Sixty metres of new passage beyond it.");

            // Under the name the form gave the question, as the trip's own write-up prints it.
            text.ShouldContain("Water level");
            text.ShouldContain("Lower than in May");

            // A trip that wrote something is named twice — once in the list of trips, once above
            // its own words. A trip that wrote nothing is named once: no heading with nothing
            // under it, which on paper reads as an account somebody removed.
            Occurrences(text, "2026-07-03 · Sump recce").ShouldBe(2);
            Occurrences(text, "2026-07-04 · Carry day").ShouldBe(1);
        }
    }

    /// <summary>
    /// What a trip's own write-up withholds from a reader, the camp's write-up withholds from the
    /// same reader — and hands to the same reader what the trip's own hands them.
    /// </summary>
    /// <remarks>
    /// The account of what went wrong goes to whoever may change the trip and to nobody else. The
    /// camp is the wider audience here, which is the dangerous direction: a rule restated for the
    /// camp's document would be a second copy, and the day the two differed a fortnight's
    /// write-up would print what each of its trips' own write-ups refuses to. So both documents
    /// are produced for both callers over one fixture and compared.
    /// </remarks>
    [Fact]
    public async Task A_camp_write_up_withholds_from_a_reader_what_the_trips_own_write_up_withholds()
    {
        const string Account = "Ana ran out of light below the third pitch; the spare was at camp.";
        var typeId = await CreateTripTypeAsync();
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var tripId = await CreateWrittenTripAsync("Third pitch", "2026-07-03", Visibility.Authenticated, body =>
        {
            body["tripTypeId"] = typeId;
            body["description"] = "Rigged to the third pitch and turned round.";
            body["hadIncident"] = true;
            body["safety"] = JsonSerializer.SerializeToElement(new { incident_account = Account });
        });
        await JoinAsync(owner, campId, tripId);

        // Whoever may change the trip: both documents carry the account.
        (await TripDocumentTextAsync(owner, tripId)).ShouldContain(Account);
        var writersCamp = await DocumentTextAsync(owner, campId);
        writersCamp.ShouldContain(Account);
        writersCamp.ShouldContain("What happened");

        // Whoever may only read it: neither does, and neither says a part was left out. The
        // trip's ordinary words are still there, so this is the rule and not an empty document.
        (await TripDocumentTextAsync(reader, tripId)).ShouldNotContain("third pitch;");
        var readersCamp = await DocumentTextAsync(reader, campId);
        readersCamp.ShouldContain("Rigged to the third pitch and turned round.");
        readersCamp.ShouldNotContain("third pitch;");
        readersCamp.ShouldNotContain("What happened");
        readersCamp.ShouldNotContain("Safety");
    }

    /// <summary>
    /// A trip the reader may not open leaves none of its words on their copy, and no heading
    /// where they would have been.
    /// </summary>
    [Fact]
    public async Task A_trip_the_reader_may_not_open_leaves_none_of_its_words_on_their_copy()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var open = await CreateWrittenTripAsync("Open survey", "2026-07-03", Visibility.Authenticated, body =>
            body["description"] = "Surveyed the stream passage to the choke.");
        var closed = await CreateWrittenTripAsync("Private dig", "2026-07-04", Visibility.Private, body =>
        {
            body["description"] = "Dug the draughting hole above the bedding plane.";
            body["results"] = "Two metres gained in the dig.";
        });
        await JoinAsync(owner, campId, open);
        await JoinAsync(owner, campId, closed);

        var withheld = await DocumentTextAsync(reader, campId);
        withheld.ShouldContain("Surveyed the stream passage to the choke.");
        withheld.ShouldNotContain("draughting hole");
        withheld.ShouldNotContain("Two metres gained");
        withheld.ShouldNotContain("Private dig");

        var entitled = await DocumentTextAsync(owner, campId);
        entitled.ShouldContain("Surveyed the stream passage to the choke.");
        entitled.ShouldContain("Dug the draughting hole above the bedding plane.");
        entitled.ShouldContain("Two metres gained in the dig.");
    }

    /// <summary>
    /// The copy filed against the camp carries each trip's words as any account reads them — the
    /// reading a trip's own filed write-up is built from — and never as the person filing it does.
    /// </summary>
    /// <remarks>
    /// Everybody who may read the camp can open what is filed on it. The filer here may change
    /// both trips, so their own download carries the account of what went wrong on one and the
    /// whole of the other, which nobody else may read; a filed copy built from their reading
    /// would put both where the camp's whole audience collects them. Both halves over one
    /// fixture: a filed copy that had stopped carrying anything would pass the second half alone.
    /// </remarks>
    [Fact]
    public async Task The_filed_copy_carries_each_trips_words_as_any_account_reads_them()
    {
        const string Account = "Vlad slipped on the traverse; the deviation had been left off the rig.";
        var typeId = await CreateTripTypeAsync();
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var open = await CreateWrittenTripAsync("Traverse day", "2026-07-03", Visibility.Authenticated, body =>
        {
            body["tripTypeId"] = typeId;
            body["description"] = "Crossed the traverse and rigged the far pitch.";
            body["hadIncident"] = true;
            body["safety"] = JsonSerializer.SerializeToElement(new { incident_account = Account });
        });
        var closed = await CreateWrittenTripAsync("Quiet lead", "2026-07-04", Visibility.Private, body =>
            body["description"] = "Pushed the lead nobody has been told about yet.");
        await JoinAsync(owner, campId, open);
        await JoinAsync(owner, campId, closed);

        var filersCopy = await DocumentTextAsync(owner, campId);
        filersCopy.ShouldContain("Crossed the traverse and rigged the far pitch.");
        filersCopy.ShouldContain(Account);
        filersCopy.ShouldContain("Pushed the lead nobody has been told about yet.");

        using var kept = await owner.PostAsync($"/api/v1/expeditions/{campId}/report", null);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());

        var filed = await FiledReportTextAsync(reader, "expedition", campId);
        filed.ShouldContain("Crossed the traverse and rigged the far pitch.");
        filed.ShouldNotContain("slipped on the traverse");
        filed.ShouldNotContain("Pushed the lead");
        filed.ShouldNotContain("Quiet lead");

        // The same trip's own filed write-up, for the same words: the two filed documents are
        // built from one reading and say the same thing about this trip.
        using var keptTrip = await owner.PostAsync($"/api/v1/trip-logs/{open}/report", null);
        keptTrip.StatusCode.ShouldBe(HttpStatusCode.OK, await keptTrip.Content.ReadAsStringAsync());
        var filedTrip = await FiledReportTextAsync(reader, "tripLog", open);
        filedTrip.ShouldContain("Crossed the traverse and rigged the far pitch.");
        filedTrip.ShouldNotContain("slipped on the traverse");
    }

    /// <summary>
    /// A trip that has been deleted is not in the camp's write-up: not its line, not its words.
    /// </summary>
    [Fact]
    public async Task A_deleted_trip_leaves_the_camps_write_up_with_its_words()
    {
        var campId = await CreateCampAsync(owner, Visibility.Authenticated);
        var kept = await CreateWrittenTripAsync("Kept survey", "2026-07-03", Visibility.Authenticated, body =>
            body["description"] = "Closed the loop through the upper series.");
        var gone = await CreateWrittenTripAsync("Withdrawn trip", "2026-07-04", Visibility.Authenticated, body =>
            body["description"] = "Entered by mistake against the wrong camp.");
        await JoinAsync(owner, campId, kept);
        await JoinAsync(owner, campId, gone);

        var before = await DocumentTextAsync(owner, campId);
        before.ShouldContain("Entered by mistake against the wrong camp.");
        before.ShouldContain("2 trips");

        (await owner.DeleteAsync($"/api/v1/trip-logs/{gone}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await DocumentTextAsync(owner, campId);
        after.ShouldContain("Closed the loop through the upper series.");
        after.ShouldNotContain("Entered by mistake");
        after.ShouldNotContain("Withdrawn trip");
        after.ShouldContain("1 trip");
        after.ShouldNotContain("2 trips");
    }

    private static int Occurrences(string text, string of)
    {
        var count = 0;
        for (var at = text.IndexOf(of, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(of, at + of.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The words of a trip's own write-up, as the same caller would download it.</summary>
    private static async Task<string> TripDocumentTextAsync(HttpClient client, Guid tripId)
    {
        using var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);
        return document.MainDocumentPart!.Document!.InnerText;
    }

    /// <summary>
    /// The words of the write-up filed against a camp or a trip, fetched the way a reader of it
    /// reaches the file: its attachments, and the delivery address the listing hands out.
    /// </summary>
    private static async Task<string> FiledReportTextAsync(HttpClient client, string entityType, Guid entityId)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType={entityType}&entityId={entityId}");
        var report = attachments.EnumerateArray()
            .Single(a => a.GetProperty("role").GetString() == "report");

        using var response = await client.GetAsync(
            report.GetProperty("file").GetProperty("contentUrl").GetString());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);
        return document.MainDocumentPart!.Document!.InnerText;
    }

    /// <summary>A trip that wrote something about itself, shaped by the caller of this helper.</summary>
    private async Task<Guid> CreateWrittenTripAsync(
        string title, string date, Visibility visibility, Action<Dictionary<string, object?>> shape)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["tripDate"] = date,
            ["participants"] = Array.Empty<object>(),
            ["visibility"] = visibility.ToString(),
        };
        shape(body);

        using var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A club purpose whose form asks one thing about what was seen and one about what went wrong.
    /// </summary>
    private async Task<long> CreateTripTypeAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        using var response = await admin.PostAsJsonAsync("/api/v1/trip-types", new
        {
            code = $"camprep_{code}",
            name = $"Camp report {code}",
            description = (string?)null,
            sortOrder = 0,
            fieldDataSchema =
                """{"type":"object","properties":{"water_level":{"type":"string","title":"Water level"}}}""",
            logisticsSchema = (string?)null,
            safetySchema =
                """{"type":"object","properties":{"incident_account":{"type":"string","title":"What happened"}}}""",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetInt64();
    }

    /// <summary>The words of a generated camp document, as a word processor would read them.</summary>
    private static async Task<string> DocumentTextAsync(HttpClient client, Guid campId, Guid? templateId = null)
    {
        using var response = await client.GetAsync(
            templateId is { } layout
                ? $"/api/v1/expeditions/{campId}/report?templateId={layout}"
                : $"/api/v1/expeditions/{campId}/report");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        response.Content.Headers.ContentDisposition!.FileName
            .ShouldNotBeNull().ShouldContain("expedition-report");

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);

        // A file a word processor refuses to open is not a report. Checked here rather than in a
        // case of its own so every assertion below is stated over a document that opens — and
        // because the layout is written with fixed metrics and nothing here measures text, which
        // is what a runtime with no fonts installed would otherwise turn into a failure only
        // after deployment.
        var faults = new OpenXmlValidator().Validate(document).ToList();
        faults.ShouldBeEmpty(string.Join("; ", faults.Select(f => f.Description)));

        return document.MainDocumentPart!.Document!.InnerText;
    }

    private static async Task<List<string>> ReportsOnCampAsync(HttpClient client, Guid campId)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=expedition&entityId={campId}");
        return [.. attachments.EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "report")
            .Select(a => a.GetProperty("file").GetProperty("originalName").GetString() ?? string.Empty)];
    }

    private async Task<Guid> CreateCampAsync(HttpClient client, Visibility visibility)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Fortnight {suffix}",
            description = "Two weeks on the plateau.",
            startDate = "2026-07-01",
            endDate = "2026-07-14",
            visibility = visibility.ToString(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTripAsync(
        HttpClient client,
        string title,
        DateOnly date,
        Visibility visibility,
        string? entryTime = null,
        string? exitTime = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = date.ToString("yyyy-MM-dd"),
            entryTime,
            exitTime,
            participants = Array.Empty<object>(),
            visibility = visibility.ToString(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>A trip with a party of newly named people, and the roster ids they became.</summary>
    private async Task<(Guid TripId, List<Guid> CaverIds)> CreateTripWithPeopleAsync(
        string title, DateOnly date, string[] names)
    {
        using var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = date.ToString("yyyy-MM-dd"),
            participants = names
                .Select(name => new { caverId = (Guid?)null, newCaverName = $"{name} {suffix}" })
                .ToArray(),
            visibility = Visibility.Authenticated.ToString(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("id").GetGuid(),
            [.. body.GetProperty("participants").EnumerateArray()
                .Select(p => p.GetProperty("caverId").GetGuid())]);
    }

    /// <summary>A trip carrying people already on the roster, plus one who is not.</summary>
    private async Task<Guid> CreateTripSharingPeopleAsync(
        string title, DateOnly date, IReadOnlyList<Guid> caverIds, string alsoNamed)
    {
        var party = caverIds
            .Select(id => new { caverId = (Guid?)id, newCaverName = (string?)null })
            .Append(new { caverId = (Guid?)null, newCaverName = (string?)$"{alsoNamed} {suffix}" })
            .ToArray();

        using var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = date.ToString("yyyy-MM-dd"),
            participants = party,
            visibility = Visibility.Authenticated.ToString(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string name, bool locationProtected)
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        using var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = Visibility.Authenticated.ToString().ToLowerInvariant(),
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>A trip recorded as being about the given caves.</summary>
    private async Task<Guid> CreateTripAboutCavesAsync(string title, DateOnly date, params Guid[] caveIds)
    {
        using var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = date.ToString("yyyy-MM-dd"),
            caveIds,
            participants = Array.Empty<object>(),
            visibility = Visibility.Authenticated.ToString(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Gives the Viewer exactly the named actions on one object, replacing what it held.</summary>
    private async Task GrantAsync(string routeType, Guid entityId, string actions)
    {
        using var response = await owner.PutAsJsonAsync($"/api/v1/objects/{routeType}/{entityId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions,
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }


    private static async Task JoinAsync(HttpClient client, Guid campId, Guid tripId)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/trips", new { tripLogId = tripId });
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A layout a club wrote itself, stored under a name of its own.</summary>
    private async Task<Guid> CreateLayoutAsync(string kind, string body)
    {
        using var response = await admin.PostAsJsonAsync("/api/v1/report-templates/", new
        {
            name = $"{kind} layout {Guid.NewGuid():N}",
            body,
            isDefault = false,
            kind,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// One line in a trip's tracking log, written straight in: what is under test is what a log
    /// that exists adds up to in a document, not how a report comes to be recorded.
    /// </summary>
    private async Task ReportAsync(Guid tripId, Guid caverId, TripPositionEventKind kind, DateTimeOffset at)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripPositionEvents.Add(new TripPositionEvent
        {
            TripLogId = tripId,
            CaverId = caverId,
            Kind = kind,
            RecordedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateTemplateAsync(string kind, bool isDefault = false)
    {
        // The shipped layout for that kind, stored back unchanged: what is under test is which
        // kind a layout belongs to, not what a club chose to write in it.
        using var shipped = await admin.GetAsync($"/api/v1/report-templates/default?kind={kind}");
        shipped.StatusCode.ShouldBe(HttpStatusCode.OK, await shipped.Content.ReadAsStringAsync());
        var body = await shipped.Content.ReadAsStringAsync();

        using var response = await admin.PostAsJsonAsync("/api/v1/report-templates/", new
        {
            name = $"{kind} layout {Guid.NewGuid():N}",
            body = body.TrimStart('﻿'),
            isDefault,
            kind,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }
}
