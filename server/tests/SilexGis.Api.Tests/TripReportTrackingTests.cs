// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A trip's tracking journal leaving the system inside its write-up: asked for by one word of a
/// club's own layout, and stating a place only to a reader who may be told it.
/// </summary>
/// <remarks>
/// <para>
/// Every claim about a place is made over the words of a real document, and in pairs: a trip in
/// a cave whose position is protected beside the same trip in a cave that is not, read by the
/// account that may place both and by a member who may read the trips and place neither
/// protected cave. A document that names no station proves nothing unless the one beside it,
/// built the same way, does.
/// </para>
/// <para>
/// The withheld side is always a Viewer. The seeded Editors group reads past visibility by
/// design, so an Editor is the account that is told everything here.
/// </para>
/// </remarks>
public sealed class TripReportTrackingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Layout = "title: {title}\nheading: Followed underground\ntracking\n";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly KeepingConverter converter = new();

    private HttpClient owner = null!;   // Editor — may place every cave here
    private HttpClient reader = null!;  // Viewer — reads the trips, may not place a protected cave
    private HttpClient admin = null!;   // Admin — stores the layout, and keeps a copy on a trip
    private long caveTypeId;
    private Guid layoutId;

    public TripReportTrackingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-tripreport-tracking-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            services =>
            {
                // Workers off: the survey file below is four invented bytes, and the job that
                // reads a survey would fail on it and rewrite the stations these tests place.
                JobWorkers.RemoveFrom(services);
                // A conversion service that keeps what it was handed, so the portable copy can
                // be judged by the document it was made from.
                services.AddSingleton<IDocumentConverter>(converter);
            });
    }

    public async Task InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trj-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"trj-read-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"trj-adm-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"trj-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"trj-read-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"trj-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        using var stored = await admin.PostAsJsonAsync("/api/v1/report-templates/", new
        {
            name = $"With the journal {suffix}", body = Layout, isDefault = false, kind = "trip", tripTypeId = (long?)null,
        });
        var payload = await stored.Content.ReadAsStringAsync();
        stored.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        layoutId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync()
    {
        // The layout is a row of a table every class shares; the trips and caves are named apart.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ReportTemplates.Where(t => t.Id == layoutId).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The copy somebody downloads says what that person may be told: the account that may place
    /// the cave reads the stations, the member who may not reads each report's hour and a word —
    /// and the same member reads the stations of the cave beside it that nothing protects.
    /// </summary>
    [Fact]
    public async Task A_download_names_a_station_to_whoever_may_be_told_it_and_prints_the_word_to_whoever_may_not()
    {
        var guarded = await FollowedTripAsync(locationProtected: true);
        var open = await FollowedTripAsync(locationProtected: false);

        // The account that may place the cave: the whole journal, on both trips.
        foreach (var trip in new[] { guarded, open })
        {
            var mine = await DocumentTextAsync(owner, trip, layoutId);
            mine.ShouldContain("Followed underground");
            // What the block is, in words, before anything it lists.
            mine.ShouldContain("not a callout record");
            mine.ShouldContain("raised no alarm");
            mine.ShouldContain("2026-09-12 10:00 UTC");
            mine.ShouldContain("At a station · cave.upper.2");
            mine.ShouldContain("At a depth · cave.deep.3, 118 m");
            mine.ShouldContain("Rigging the second pitch");
            mine.ShouldContain("Guest 1");
            mine.ShouldNotContain("place withheld");
            mine.ShouldNotContain("Some places are withheld");
            // The report taken off the log is not in the journal: nothing else names its station.
            mine.ShouldNotContain("cave.parallel.2");
        }

        // The member who may read the trip and not place its cave.
        var theirs = await DocumentTextAsync(reader, guarded, layoutId);
        theirs.ShouldNotContain("cave.");
        theirs.ShouldNotContain("118 m");
        theirs.ShouldContain("2026-09-12 10:00 UTC");
        theirs.ShouldContain("At a station · place withheld");
        theirs.ShouldContain("At a depth · place withheld");
        theirs.ShouldContain("Some places are withheld");
        // Everything that is not a place is theirs: who, when, in or out, and the note.
        theirs.ShouldContain("Went in");
        theirs.ShouldContain("Came out");
        theirs.ShouldContain("Rigging the second pitch");
        theirs.ShouldContain("not a callout record");

        // …and the very same member, on the cave nothing protects, is told the stations: the
        // refusal above is about that cave and not about this reader or this layout.
        var theirsOpen = await DocumentTextAsync(reader, open, layoutId);
        theirsOpen.ShouldContain("At a station · cave.upper.2");
        theirsOpen.ShouldContain("cave.deep.3, 118 m");
        theirsOpen.ShouldNotContain("place withheld");

        // The layout the system ships asks for no journal and prints none, to anybody.
        var standard = await DocumentTextAsync(owner, guarded, null);
        standard.ShouldNotContain("not a callout record");
        standard.ShouldNotContain("cave.upper.2");
        standard.ShouldNotContain("Went in");

        // A trip nobody followed prints nothing under the word, and loses the heading over it.
        var plain = (await CreateTripAsync("Never followed", guests: 1)).Trip;
        var unfollowed = await DocumentTextAsync(owner, plain, layoutId);
        unfollowed.ShouldContain("Never followed");
        unfollowed.ShouldNotContain("Followed underground");
        unfollowed.ShouldNotContain("not a callout record");

        // The journal's own words follow the language the page asked in; what they say does not.
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/trip-logs/{guarded}/report?templateId={layoutId}");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ro-RO,ro;q=0.9,en;q=0.8");
        using var answered = await reader.SendAsync(request);
        var romanian = await TextOfAsync(answered);
        romanian.ShouldContain("nu a dat nicio alarmă");
        romanian.ShouldContain("La o stație · loc reținut");
        romanian.ShouldNotContain("cave.");
        romanian.ShouldNotContain("118 m");
        romanian.ShouldNotContain("place withheld");

        // A request that names no language this application writes leaves it to the language the
        // account has stored — the order every line worded for a reader is picked by — and one
        // that does name a language is answered in it whatever the account has stored.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Users.Where(u => u.Email == $"trj-read-{suffix}@t.local")
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.Locale, "ro"))).ShouldBe(1);
        }

        (await DocumentTextAsync(reader, guarded, layoutId, "de-DE,*;q=0.5"))
            .ShouldContain("La o stație · loc reținut");
        (await DocumentTextAsync(reader, guarded, layoutId, acceptLanguage: null))
            .ShouldContain("La o stație · loc reținut");
        var asked = await DocumentTextAsync(reader, guarded, layoutId, "en-GB,en;q=0.9");
        asked.ShouldContain("At a station · place withheld");
        asked.ShouldNotContain("loc reținut");
    }

    /// <summary>
    /// A copy kept on a trip is a file, and a file does not ask again what its readers may be
    /// told. So when the cave the trip was followed in comes under protection, the copy comes off
    /// the trip: nobody who reads the trip finds it there any more or can fetch it, and the copy
    /// made afterwards names no station. A trip followed in another cave keeps its own.
    /// </summary>
    [Fact]
    public async Task Protecting_the_cave_takes_the_kept_copy_off_every_trip_followed_in_it()
    {
        var followed = await FollowedTripAsync(locationProtected: false);
        var elsewhere = await FollowedTripAsync(locationProtected: false);
        foreach (var trip in new[] { followed, elsewhere })
        {
            using var kept = await admin.PostAsync($"/api/v1/trip-logs/{trip}/report?templateId={layoutId}", null);
            kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        }

        // While nothing protects the cave the kept copy names its stations, and the member
        // fetches it: what is withdrawn below was there, and was theirs to read.
        var fileId = (await FiledReportsAsync(reader, followed)).ShouldHaveSingleItem().File;
        (await FiledReportAsync(reader, followed)).Package.ShouldContain("cave.upper.2");

        Guid cave;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            cave = (await db.TripTrackings.Where(t => t.TripLogId == followed)
                .Select(t => t.CaveFeatureId).SingleAsync()).ShouldNotBeNull();
            await scope.ServiceProvider.GetRequiredService<FeatureWriteService>()
                .SetLocationProtectedAsync(cave, true);
            await db.SaveChangesAsync();
        }

        // Off the trip, for the member and for the account that may place the cave alike…
        (await FiledReportsAsync(reader, followed)).ShouldBeEmpty();
        (await FiledReportsAsync(owner, followed)).ShouldBeEmpty();
        // …and asked for by name, the file is not the member's to be handed an address for. (An
        // address already handed out is a decision already taken and runs out by itself.)
        using (var refused = await reader.GetAsync($"/api/v1/files/{fileId}"))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // The trip followed in the cave nothing protects keeps its copy, stations and all.
        (await FiledReportAsync(reader, elsewhere)).Package.ShouldContain("cave.upper.2");

        // Written up again, the trip has a copy once more, and it is read under the cave as it
        // now stands.
        using (var again = await admin.PostAsync($"/api/v1/trip-logs/{followed}/report?templateId={layoutId}", null))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        }

        var (words, package) = await FiledReportAsync(reader, followed);
        words.ShouldContain("At a station · place withheld");
        package.ShouldNotContain("cave.upper");
        package.ShouldNotContain("cave.deep");
    }

    /// <summary>
    /// The copy kept on a trip is opened by everybody who may read the trip, so it states a place
    /// only where any account may be told it — whoever pressed the button. Kept by an
    /// administrator on a trip in a protected cave, it names no station anywhere in the file;
    /// kept by the same administrator on a trip in a cave nothing protects, it names them.
    /// </summary>
    [Fact]
    public async Task The_copy_kept_on_a_trip_in_a_protected_cave_names_no_station_even_when_an_administrator_kept_it()
    {
        var guarded = await FollowedTripAsync(locationProtected: true);
        var open = await FollowedTripAsync(locationProtected: false);

        // The administrator's own download names the stations: they may be told them, so what the
        // kept copy leaves out below is left out for its readers and not for want of rights.
        (await DocumentTextAsync(admin, guarded, layoutId)).ShouldContain("At a station · cave.upper.2");

        foreach (var trip in new[] { guarded, open })
        {
            using var kept = await admin.PostAsync($"/api/v1/trip-logs/{trip}/report?templateId={layoutId}", null);
            kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        }

        // Fetched the way a reader of the trip reaches it, by the member and by the administrator
        // alike: it is one file, and it says the same to both.
        foreach (var client in new[] { reader, admin })
        {
            var (words, package) = await FiledReportAsync(client, guarded);
            words.ShouldContain("Followed underground");
            words.ShouldContain("not a callout record");
            words.ShouldContain("2026-09-12 10:00 UTC");
            words.ShouldContain("At a station · place withheld");
            words.ShouldContain("At a depth · place withheld");
            words.ShouldContain("Some places are withheld");
            words.ShouldContain("Rigging the second pitch");
            words.ShouldContain("Guest 1");
            words.ShouldNotContain("118 m");
            // On the bytes: no part of the package — not the body, not its properties, not a
            // part nobody reads — carries a station of the survey.
            package.ShouldNotContain("cave.upper");
            package.ShouldNotContain("cave.deep");
            package.ShouldNotContain("cave.parallel");
            package.ShouldNotContain("cave.ent");
        }

        // The positive half, searched the same way through the same route: the cave any account
        // may place has its stations in the kept copy, for the member who was refused them above.
        var (openWords, openPackage) = await FiledReportAsync(reader, open);
        openWords.ShouldContain("At a station · cave.upper.2");
        openWords.ShouldContain("At a depth · cave.deep.3, 118 m");
        openWords.ShouldNotContain("place withheld");
        openPackage.ShouldContain("cave.upper.2");
        openPackage.ShouldContain("cave.deep.3");
        // The report taken off the log is in neither copy.
        openPackage.ShouldNotContain("cave.parallel");
    }

    /// <summary>
    /// The portable copy is the same document laid out by the conversion service, so it says what
    /// the document says and needs no rule of its own: what the service is handed for the member
    /// names no station, and what it is handed for the account that may place the cave does.
    /// </summary>
    [Fact]
    public async Task Asked_for_as_a_pdf_the_journal_is_the_same_document_passed_through_the_converter()
    {
        var guarded = await FollowedTripAsync(locationProtected: true);

        using var theirs = await reader.GetAsync($"/api/v1/trip-logs/{guarded}/report?templateId={layoutId}&format=pdf");
        await ShouldBeAPdfAsync(theirs);
        converter.LastName.ShouldNotBeNull().ShouldEndWith(".docx");
        var handedForTheMember = WordsOf(converter.LastSource.ShouldNotBeNull());
        handedForTheMember.ShouldContain("At a station · place withheld");
        handedForTheMember.ShouldContain("not a callout record");
        PackageText(converter.LastSource!).ShouldNotContain("cave.");

        using var mine = await owner.GetAsync($"/api/v1/trip-logs/{guarded}/report?templateId={layoutId}&format=pdf");
        await ShouldBeAPdfAsync(mine);
        var handedForTheOwner = WordsOf(converter.LastSource.ShouldNotBeNull());
        handedForTheOwner.ShouldContain("At a station · cave.upper.2");
        handedForTheOwner.ShouldNotContain("place withheld");

        // The copy kept on the trip stays a word-processor document, with the journal or without.
        using var filed = await admin.PostAsync(
            $"/api/v1/trip-logs/{guarded}/report?templateId={layoutId}&format=pdf", null);
        filed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await filed.Content.ReadAsStringAsync()).ShouldContain("report.pdf_not_filed");
    }

    // ---- fixtures ---------------------------------------------------------------------------

    /// <summary>
    /// A finished watch with a little of everything on its log: both people in, one placed at a
    /// station and one at a depth, a note, an exit — and one report taken off again, at a station
    /// no other report names.
    /// </summary>
    private async Task<Guid> FollowedTripAsync(bool locationProtected)
    {
        var (trip, cavers) = await CreateTripAsync("Followed", guests: 2);
        var cave = await CreateCaveAsync(locationProtected);
        var model = await SeedModelWithStationsAsync(cave);
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await ReportAsync(trip, new { caverIds = cavers, kind = "entered", recordedAt = At(9, 0) });
        await ReportAsync(trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "atStation", stationName = "cave.upper.2", recordedAt = At(10, 0),
        });
        var mistaken = await ReportAsync(trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "atStation", stationName = "cave.parallel.2", recordedAt = At(10, 30),
        });
        await ReportAsync(trip, new { caverIds = new[] { cavers[1] }, kind = "atDepth", depthM = 118, recordedAt = At(11, 0) });
        await ReportAsync(trip, new
        {
            caverIds = new[] { cavers[0] }, kind = "note", note = "Rigging the second pitch", recordedAt = At(11, 30),
        });
        await ReportAsync(trip, new { caverIds = new[] { cavers[0] }, kind = "exited", recordedAt = At(12, 0) });

        (await owner.DeleteAsync($"/api/v1/trip-logs/{trip}/tracking/events/{mistaken[0].GetProperty("id").GetGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        return trip;
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(string title, int guests)
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().OrderBy(c => c).ToListAsync();
        cavers.Count.ShouldBe(guests);
        return (trip, cavers);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Journal Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A survey created the way an upload creates one, then given stations directly: an entrance
    /// at 350 m, two branches at the same level and a deep point 120 m down. Nothing reads the
    /// file — the workers are off.
    /// </summary>
    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "journal.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.1", "cave.upper", 340, SurveyStationFlags.Underground),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.parallel.2", "cave.parallel", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
    private static async Task<HttpResponseMessage> PutConfigAsync(HttpClient client, Guid trip, object body)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }

    /// <summary>The trip's own day, so a report's hour is the one the test states and not the one it was typed at.</summary>
    private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 12, hour, minute, 0, TimeSpan.Zero);

    /// <summary>One report, asserted to have landed; answers with the rows it wrote.</summary>
    private async Task<JsonElement> ReportAsync(Guid trip, object body)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---- reading documents ------------------------------------------------------------------

    private static async Task<string> DocumentTextAsync(HttpClient client, Guid tripId, Guid? templateId, string? acceptLanguage = null)
    {
        var url = templateId is { } id
            ? $"/api/v1/trip-logs/{tripId}/report?templateId={id}"
            : $"/api/v1/trip-logs/{tripId}/report";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request);
        return await TextOfAsync(response);
    }

    /// <summary>The words of a downloaded write-up, as a word processor would read them.</summary>
    private static async Task<string> TextOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        return WordsOf(await response.Content.ReadAsByteArrayAsync());
    }

    private static string WordsOf(byte[] file)
    {
        using var bytes = new MemoryStream(file);
        using var document = WordprocessingDocument.Open(bytes, false);

        // A file a word processor refuses to open is not a write-up, whatever it says inside.
        var faults = new OpenXmlValidator().Validate(document).ToList();
        faults.ShouldBeEmpty(string.Join("; ", faults.Select(f => f.Description)));

        // One paragraph per line, so a phrase asserted here is a phrase of one line of the
        // document and cannot be made of the end of one line and the beginning of the next.
        return string.Join(
            "\n",
            document.MainDocumentPart!.Document!.Body!
                .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
    }

    /// <summary>
    /// Every part of the package, as text. A document is a zip of several files, and a search of
    /// its body alone would say nothing about the others.
    /// </summary>
    private static string PackageText(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        var text = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            using var part = new StreamReader(entry.Open(), Encoding.UTF8);
            text.Append(entry.FullName).Append('\n').Append(part.ReadToEnd()).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// The write-up kept on the trip, fetched the way a reader of the trip reaches it — the
    /// trip's attachments, and the delivery address the listing hands out: its words, and the
    /// text of every part of the file.
    /// </summary>
    private static async Task<(string Words, string Package)> FiledReportAsync(HttpClient client, Guid tripId)
    {
        using var response = await client.GetAsync((await FiledReportsAsync(client, tripId)).Single().Address);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var file = await response.Content.ReadAsByteArrayAsync();
        return (WordsOf(file), PackageText(file));
    }

    /// <summary>
    /// Each write-up kept on the trip and where it is fetched from, as the trip's attachments
    /// tell this reader.
    /// </summary>
    private static async Task<List<(Guid File, string Address)>> FiledReportsAsync(HttpClient client, Guid tripId)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=tripLog&entityId={tripId}");
        return attachments.EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "report")
            .Select(a => a.GetProperty("file"))
            .Select(file => (file.GetProperty("id").GetGuid(), file.GetProperty("contentUrl").GetString()!))
            .ToList();
    }

    private static async Task ShouldBeAPdfAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var name = response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().Trim('"');
        name.ShouldStartWith("trip-report-");
        name.ShouldEndWith(".pdf");
        Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync(), 0, 4).ShouldBe("%PDF");
    }

    /// <summary>
    /// Stands in for the office suite: answers with a portable document, and keeps the document
    /// it was asked to lay out so a test can read what a portable copy was made from.
    /// </summary>
    private sealed class KeepingConverter : IDocumentConverter
    {
        public string? LastName { get; private set; }

        public byte[]? LastSource { get; private set; }

        public bool IsConfigured => true;

        public async Task ConvertToPortableAsync(
            Stream source, string originalName, Stream destination, CancellationToken ct = default)
        {
            using var given = new MemoryStream();
            await source.CopyToAsync(given, ct);
            LastSource = given.ToArray();
            LastName = originalName;
            await destination.WriteAsync("%PDF-1.7\n% a stand-in, not a document\n%%EOF\n"u8.ToArray(), ct);
        }
    }
}
