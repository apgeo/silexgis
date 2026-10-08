// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The photographs hung on moments of a finished trip, as the four published-trip routes tell them
/// to somebody without an account — which, on an installation as it is installed, is not at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these hold down.</b> An installation that has not switched the setting on sends the
/// list empty with every replay, whatever the trip holds. Switched on, the one read that replays a
/// finished trip carries the photographs that are in the public gallery, hang on a moment inside
/// the replay and are reached through a link naming nothing guarded — each as a moment, an address
/// of a rendering, the gallery's caption and a number in the party or nothing — and the other
/// three reads are what they were.
/// </para>
/// <para>
/// <b>Every "nothing is told" here is read beside a "something is told".</b> Three hosts stand
/// over one database and one clock: one as installed, one that publishes these, and one that
/// publishes these while naming nobody. So an empty list on the first is the setting's doing and
/// not a trip with no photographs, and a search that finds no trace of a person is run over an
/// answer known to carry pictures of them.
/// </para>
/// </remarks>
public sealed class PublicTripMomentPicturesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string PublishKey = "TripPastTracks:PublishMomentPictures";

    // Nobody else in this fixture — no title, no station, no file name, no caption — shares a word
    // with these, so finding one in an answer can only mean a person was named.
    private const string Ana = "Quorvix Dalthane";
    private const string Bogdan = "Ysolde Brannick";
    private const string Leaver = "Thessaly Wrenmoor";

    // Typed beside a picture when it was hung on the trip: one names the person it is of.
    private const string NoteNamingAna = "Quorvix pe coarda Vextral";

    private readonly SilexGisApiFactory asInstalled;
    private readonly SilexGisApiFactory publishing;
    private readonly SilexGisApiFactory publishingUnnamed;
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient curator = null!;
    private HttpClient visitor = null!;
    private HttpClient visitorOfPublishing = null!;
    private HttpClient visitorOfUnnamed = null!;
    private long caveTypeId;

    public PublicTripMomentPicturesTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"trippics-{Guid.NewGuid():N}");
        // The first host is given no word about the setting at all: what it answers is what an
        // installation answers that never heard of it.
        asInstalled = HostWith();
        publishing = HostWith((PublishKey, "true"));
        publishingUnnamed = HostWith((PublishKey, "true"), ("TripTracking:PublishRealNames", "false"));
    }

    /// <summary>
    /// A host over this class's database, file store, signing keys and clock, with the given
    /// settings on top.
    /// </summary>
    private SilexGisApiFactory HostWith(params (string Key, string Value)[] settings)
    {
        var host = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        };
        foreach (var (key, value) in settings) host[key] = value;
        return new SilexGisApiFactory(connectionString, host, services =>
        {
            // Workers off: the survey file below is four bytes, and a worker reading it would fail
            // the model and rewrite the stations seeded beside it.
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(asInstalled, GlobalRoles.Editor, $"pics-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(asInstalled, GlobalRoles.Admin, $"pics-adm-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(asInstalled, $"pics-own-{suffix}@t.local");
        curator = await AuthHelper.BearerClientAsync(asInstalled, $"pics-adm-{suffix}@t.local");
        visitor = asInstalled.CreateClient();
        visitorOfPublishing = publishing.CreateClient();
        visitorOfUnnamed = publishingUnnamed.CreateClient();

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        publishingUnnamed.Dispose();
        publishing.Dispose();
        asInstalled.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// <b>The load-bearing one.</b> A finished trip holds seven photographs on its moments: of one
    /// person, of the party, of somebody later taken off the trip, one nobody put in the gallery,
    /// one whose moment is days before the trip, one on a link that comes to name a guarded cave —
    /// and the trip being followed now holds one too. As installed no route carries any of them;
    /// with the setting on, the replay alone carries exactly the ones every gate lets through, and
    /// the rest of every answer is what it was.
    /// </summary>
    [Fact]
    public async Task Photographs_at_their_moment_are_told_only_where_the_installation_publishes_them_and_only_through_every_gate()
    {
        var cave = await PublishedCaveAsync();
        var then = cave.Then.Trip;
        var people = await PeopleOfAsync(then);
        var (ana, bogdan, leaver) = (people[Ana], people[Bogdan], people[Leaver]);

        var start = Stamp(clock.Now.AddDays(-6));
        var (first, second, third) = (start.AddHours(2), start.AddHours(3), start.AddHours(4));

        var ofAna = await PhotographAsync("of-one");
        var ofTheParty = await PhotographAsync("of-all");
        var unpublished = await PhotographAsync("kept");
        var ofTheLeaver = await PhotographAsync("of-gone");
        var beforeTheTrip = await PhotographAsync("early");
        var entangled = await PhotographAsync("tangled");
        var ofTonight = await PhotographAsync("tonight");
        foreach (var photograph in new[] { ofAna, ofTheParty, ofTheLeaver, beforeTheTrip, entangled, ofTonight })
        {
            await PutInGalleryAsync(photograph.Document);
        }

        await AttachAsync(then,
            new { documentId = ofAna.Document, at = first, caverId = (Guid?)ana, caption = NoteNamingAna },
            new { documentId = ofTheParty.Document, at = first, caverId = (Guid?)null, caption = (string?)null },
            new { documentId = unpublished.Document, at = second, caverId = (Guid?)bogdan, caption = (string?)null },
            new { documentId = ofTheLeaver.Document, at = second, caverId = (Guid?)leaver, caption = (string?)null },
            new { documentId = beforeTheTrip.Document, at = start.AddDays(-3), caverId = (Guid?)ana, caption = (string?)null },
            new { documentId = entangled.Document, at = third, caverId = (Guid?)bogdan, caption = (string?)null });
        // The party still underground has a photograph on a moment as well, which no page of a
        // trip being followed is to carry whichever way the setting stands.
        await AttachAsync(cave.Now.Trip,
            new { documentId = ofTonight.Document, at = Stamp(clock.Now.AddMinutes(-5)), caverId = (Guid?)null, caption = (string?)null });

        await FinishAsync(then, start, start.AddHours(8));

        // ---- an installation that publishes them, before anything is taken back ---------------
        var whole = await ReadAsync(visitorOfPublishing, cave.PastTrip);
        var numberOf = NumbersByLabel(whole);
        PicturesOf(whole).ShouldBe(Expecting(
            (first, null, ofTheParty.File),
            (first, numberOf[Ana], ofAna.File),
            (second, numberOf[Leaver], ofTheLeaver.File),
            (third, numberOf[Bogdan], entangled.File)));

        // Somebody is taken off the trip, and a link comes to name a cave whose position is kept.
        await TakeOffTheTripAsync(then, leaver);
        await AlsoNamingAsync(entangled.Document, await CaveAsync(locationProtected: true));

        // ---- as installed ------------------------------------------------------------------
        var everyPhotograph = new[] { ofAna, ofTheParty, unpublished, ofTheLeaver, beforeTheTrip, entangled, ofTonight };
        var off = new Dictionary<string, Answer>();
        foreach (var route in cave.Routes)
        {
            off[route] = await ReadAsync(visitor, route);
            off[route].Status.ShouldBe(HttpStatusCode.OK, route);
            foreach (var photograph in everyPhotograph)
            {
                ShouldNotHold(off[route].Body, photograph.File, route);
                ShouldNotHold(off[route].Body, photograph.Document, route);
            }
        }

        // Present and empty on the replay, so a page reads "no photographs" and not "an older
        // server"; and not a member of the other three answers at all.
        PicturesOf(off[cave.PastTrip]).ShouldBeEmpty();
        foreach (var route in new[] { cave.Follow, cave.Live, cave.Past })
        {
            Json(off[route]).TryGetProperty("pictures", out _).ShouldBeFalse(route);
        }

        // ---- the same trips, the same instant, an installation that publishes them -----------
        var on = new Dictionary<string, Answer>();
        foreach (var route in cave.Routes)
        {
            on[route] = await ReadAsync(visitorOfPublishing, route);
            on[route].Status.ShouldBe(HttpStatusCode.OK, route);
        }

        // Oldest moment first; the party's before a person's. The one of somebody no longer on the
        // trip stays a picture of its moment and is about nobody; the unpublished one, the one
        // from before the trip and the one whose link names a guarded cave are not there.
        PicturesOf(on[cave.PastTrip]).ShouldBe(Expecting(
            (first, null, ofTheParty.File),
            (first, numberOf[Ana], ofAna.File),
            (second, null, ofTheLeaver.File)));
        foreach (var kept in new[] { unpublished, beforeTheTrip, entangled, ofTonight })
        {
            ShouldNotHold(on[cave.PastTrip].Body, kept.File, "replay");
        }

        // No identifier of a person, of a photograph's document or of anything else about how it
        // was hung; and not the words typed beside it, which named somebody.
        foreach (var route in cave.Routes)
        {
            foreach (var person in new[] { ana, bogdan, leaver }) ShouldNotHold(on[route].Body, person, route);
            foreach (var photograph in everyPhotograph) ShouldNotHold(on[route].Body, photograph.Document, route);
            on[route].Body.ShouldNotContain("Vextral", Case.Insensitive, route);
            on[route].Body.ShouldNotContain("coarda", Case.Insensitive, route);
        }

        // ---- and that is the whole of the difference -----------------------------------------
        // The two lists hand out no signed address: the same bytes on both hosts.
        on[cave.Live].Body.ShouldBe(off[cave.Live].Body);
        on[cave.Past].Body.ShouldBe(off[cave.Past].Body);
        // The followed page signs its survey's address afresh on every read, and so does the
        // replay, which is compared with its one new list left out.
        Comparable(Json(on[cave.Follow]), null).ShouldBe(Comparable(Json(off[cave.Follow]), null));
        Comparable(Json(on[cave.PastTrip]), "pictures").ShouldBe(Comparable(Json(off[cave.PastTrip]), "pictures"));

        // ---- a reader holding the installed answer is not told "nothing changed" -------------
        on[cave.PastTrip].Tag.ShouldNotBe(off[cave.PastTrip].Tag);
        (await ReadAsync(visitor, cave.PastTrip, off[cave.PastTrip].Tag)).Status.ShouldBe(HttpStatusCode.NotModified);
        (await ReadAsync(visitorOfPublishing, cave.PastTrip, off[cave.PastTrip].Tag)).Status.ShouldBe(HttpStatusCode.OK);
        // Where the setting changes nothing it changes no validator.
        foreach (var route in new[] { cave.Follow, cave.Live, cave.Past })
        {
            on[route].Tag.ShouldBe(off[route].Tag, route);
        }

        // ---- the address opens a rendering and never the upload ------------------------------
        var address = Json(on[cave.PastTrip]).GetProperty("pictures")[0].GetProperty("thumbnailUrl").GetString()!;
        address.ShouldStartWith($"/api/v1/files/{ofTheParty.File}/thumbnail?");
        (await visitorOfPublishing.GetAsync(address)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await visitorOfPublishing.GetAsync(ContentInsteadOf(address))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // No photograph is offered as its upload in the first place. (The survey is, on the same
        // answer and by design: a drawing is only useful as its own bytes.)
        Json(on[cave.PastTrip]).GetProperty("pictures").GetRawText().ShouldNotContain("/content");

        // ---- a photograph taken out of the gallery is taken off the replay --------------------
        await PutInGalleryAsync(ofAna.Document, published: false);
        var after = await ReadAsync(visitorOfPublishing, cave.PastTrip, on[cave.PastTrip].Tag);
        after.Status.ShouldBe(HttpStatusCode.OK);
        PicturesOf(after).Select(p => p.File).ShouldBe([ofTheParty.File, ofTheLeaver.File]);
    }

    /// <summary>
    /// On an installation that publishes these and names nobody, a replay carrying pictures of
    /// people carries no word of anybody's name on any of the four routes — although a note typed
    /// beside one of the pictures names its subject.
    /// </summary>
    [Fact]
    public async Task With_names_switched_off_a_replay_carrying_pictures_of_people_carries_no_word_of_a_name()
    {
        var cave = await PublishedCaveAsync();
        var then = cave.Then.Trip;
        var people = await PeopleOfAsync(then);
        var start = Stamp(clock.Now.AddDays(-6));

        var ofAna = await PhotographAsync("of-one");
        var ofBogdan = await PhotographAsync("of-two");
        await PutInGalleryAsync(ofAna.Document);
        await PutInGalleryAsync(ofBogdan.Document);
        await AttachAsync(then,
            new { documentId = ofAna.Document, at = start.AddHours(2), caverId = (Guid?)people[Ana], caption = NoteNamingAna },
            new { documentId = ofBogdan.Document, at = start.AddHours(3), caverId = (Guid?)people[Bogdan], caption = (string?)$"{Bogdan} la sifon" });
        await FinishAsync(then, start, start.AddHours(8));

        // The same answer on the host that names the party does carry the names, so the search
        // below is one that finds a name when it is there.
        var named = await ReadAsync(visitorOfPublishing, cave.PastTrip);
        named.Body.ShouldContain("Dalthane");
        NumbersByLabel(named).Keys.ShouldBe([Ana, Bogdan, Leaver], ignoreOrder: true);

        foreach (var route in cave.Routes)
        {
            var answer = await ReadAsync(visitorOfUnnamed, route);
            answer.Status.ShouldBe(HttpStatusCode.OK, route);
            foreach (var word in new[] { Ana, Bogdan, Leaver }.SelectMany(name => name.Split(' ')))
            {
                answer.Body.ShouldNotContain(word, Case.Insensitive, route);
            }

            foreach (var person in people.Values) ShouldNotHold(answer.Body, person, route);
        }

        // And the replay that was searched is one that carries both pictures, each about a number.
        var pictures = PicturesOf(await ReadAsync(visitorOfUnnamed, cave.PastTrip));
        pictures.Select(p => p.File).ShouldBe([ofAna.File, ofBogdan.File]);
        pictures.ShouldAllBe(p => p.Ordinal != null);
    }

    /// <summary>
    /// A cave whose position becomes protected after its trips were published has no published
    /// answer at all, so its photographs travel nowhere — on an installation that publishes them,
    /// where a moment earlier the same link told them.
    /// </summary>
    [Fact]
    public async Task A_protected_cave_has_no_published_answer_for_its_photographs_to_travel_in()
    {
        var cave = await PublishedCaveAsync();
        var start = Stamp(clock.Now.AddDays(-6));
        var photograph = await PhotographAsync("of-all");
        await PutInGalleryAsync(photograph.Document);
        await AttachAsync(cave.Then.Trip,
            new { documentId = photograph.Document, at = start.AddHours(2), caverId = (Guid?)null, caption = (string?)null });
        await FinishAsync(cave.Then.Trip, start, start.AddHours(8));

        // Told, while the cave may be published.
        PicturesOf(await ReadAsync(visitorOfPublishing, cave.PastTrip)).Select(p => p.File).ShouldBe([photograph.File]);

        using (var scope = asInstalled.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            await writer.SetLocationProtectedAsync(cave.Cave, true);
            await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
        }

        foreach (var client in new[] { visitor, visitorOfPublishing })
        {
            foreach (var route in cave.Routes)
            {
                var refused = await ReadAsync(client, route);
                refused.Status.ShouldBe(HttpStatusCode.NotFound, route);
                ShouldNotHold(refused.Body, photograph.File, route);
            }
        }
    }

    // ---- what a reader sees ------------------------------------------------------------------

    private sealed record Answer(HttpStatusCode Status, string? Tag, string Body);

    private static async Task<Answer> ReadAsync(HttpClient client, string url, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch).ShouldBeTrue();
        }

        using var response = await client.SendAsync(request);
        return new Answer(
            response.StatusCode, response.Headers.ETag?.ToString(), await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Json(Answer answer) => JsonDocument.Parse(answer.Body).RootElement;

    /// <summary>
    /// The photographs a replay came with, each as its moment, its number in the party and the
    /// file its address opens a rendering of.
    /// </summary>
    private static List<(DateTimeOffset At, int? Ordinal, Guid File)> PicturesOf(Answer answer)
    {
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        var pictures = Json(answer).GetProperty("pictures");
        pictures.ValueKind.ShouldBe(JsonValueKind.Array);
        return [.. pictures.EnumerateArray().Select(picture =>
        {
            // Exactly these four members: no id of the photograph, of its link or of a person.
            picture.EnumerateObject().Select(m => m.Name).ShouldBe(["at", "ordinal", "thumbnailUrl", "caption"]);
            var address = picture.GetProperty("thumbnailUrl").GetString()!;
            address.ShouldStartWith("/api/v1/files/");
            address.ShouldContain("/thumbnail?");
            address.ShouldContain("token=");
            var ordinal = picture.GetProperty("ordinal");
            return (
                picture.GetProperty("at").GetDateTimeOffset(),
                ordinal.ValueKind == JsonValueKind.Null ? (int?)null : ordinal.GetInt32(),
                Guid.Parse(address["/api/v1/files/".Length..address.IndexOf("/thumbnail", StringComparison.Ordinal)]));
        })];
    }

    private static List<(DateTimeOffset At, int? Ordinal, Guid File)> Expecting(
        params (DateTimeOffset At, int? Ordinal, Guid File)[] pictures) => [.. pictures];

    /// <summary>Each person's number in the party, by the name the answer gives them.</summary>
    private static Dictionary<string, int> NumbersByLabel(Answer answer) =>
        Json(answer).GetProperty("participants").EnumerateArray().ToDictionary(
            p => p.GetProperty("label").GetString()!,
            p => p.GetProperty("ordinal").GetInt32());

    /// <summary>An identifier in neither of the two spellings an answer could write it in.</summary>
    private static void ShouldNotHold(string body, Guid id, string where)
    {
        body.ShouldNotContain(id.ToString("D"), Case.Insensitive, where);
        body.ShouldNotContain(id.ToString("N"), Case.Insensitive, where);
    }

    /// <summary>
    /// An answer as text that two reads of one state share: one member of the answer itself left
    /// out when one is named, and every address that is signed afresh on each read replaced by a
    /// mark.
    /// </summary>
    private static string Comparable(JsonElement node, string? withoutAtTheTop) => node.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", node.EnumerateObject()
            .Where(m => m.Name != withoutAtTheTop)
            .Select(m => $"\"{m.Name}\":{Comparable(m.Value, null)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",", node.EnumerateArray().Select(e => Comparable(e, null))) + "]",
        JsonValueKind.String when node.GetString()!.Contains("token=", StringComparison.Ordinal) => "\"signed\"",
        _ => node.GetRawText(),
    };

    /// <summary>The same delivery address aimed at the route that hands over the upload.</summary>
    private static string ContentInsteadOf(string thumbnailUrl)
    {
        var query = thumbnailUrl[(thumbnailUrl.IndexOf('?') + 1)..]
            .Split('&')
            .Single(part => part.StartsWith("token=", StringComparison.Ordinal));
        return thumbnailUrl[..thumbnailUrl.IndexOf('?')].Replace("/thumbnail", "/content") + "?" + query;
    }

    // ---- seeding -----------------------------------------------------------------------------

    /// <summary>An instant with no fraction of a second, so it reads back as it was written.</summary>
    private static DateTimeOffset Stamp(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero);

    private sealed record Photograph(Guid Document, Guid File);

    /// <summary>A photograph uploaded the real way, as both of its identities.</summary>
    private async Task<Photograph> PhotographAsync(string label)
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        var content = new ByteArrayContent(image.ToByteArray(MagickFormat.Jpeg));
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", $"{label}-{Guid.NewGuid():N}.jpg" } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return new Photograph(version.DocumentId, fileId);
    }

    /// <summary>
    /// Puts a photograph in the installation's public gallery, or takes it out — the act of
    /// publication a published page takes as consent, by the only kind of account allowed it.
    /// </summary>
    private async Task PutInGalleryAsync(Guid documentId, bool published = true)
    {
        var response = await curator.PutAsync(
            $"/api/v1/photos/{documentId}/public?published={(published ? "true" : "false")}", null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Hangs photographs on moments of a trip through the route a member uses.</summary>
    private async Task AttachAsync(Guid trip, params object[] items)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/pictures", new { items });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Adds a member naming a feature to the link a photograph hangs by — how a link about a
    /// moment of one trip comes to touch a cave the trip is not about.
    /// </summary>
    private async Task AlsoNamingAsync(Guid documentId, Guid feature)
    {
        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var link = await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId == documentId)
            .Select(m => m.ResLinkId)
            .SingleAsync();
        db.ResLinkMembers.Add(new ResLinkMember { ResLinkId = link, FeatureId = feature, SortOrder = 50 });
        await db.SaveChangesAsync();
    }

    /// <summary>Takes somebody off a trip's list, leaving everything hung about them where it was.</summary>
    private async Task TakeOffTheTripAsync(Guid trip, Guid caver)
    {
        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.TripLogParticipants.Where(p => p.TripLogId == trip && p.CaverId == caver).ToListAsync();
        rows.ShouldNotBeEmpty();
        db.TripLogParticipants.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    /// <summary>The people of a trip by name.</summary>
    private async Task<Dictionary<string, Guid>> PeopleOfAsync(Guid trip)
    {
        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ids = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().ToListAsync();
        return await db.Cavers.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.FullName, c => c.Id);
    }

    /// <summary>
    /// Closes the watch through the API, then writes the trip's hours as they would have been had
    /// it run days ago: when it was started, when it was closed, and every report an hour in.
    /// </summary>
    private async Task FinishAsync(Guid trip, DateTimeOffset armedAt, DateTimeOffset closedAt)
    {
        (await PutConfigAsync(trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ArmedAt = armedAt;
        tracking.ClosedAt = closedAt;
        foreach (var report in await db.TripPositionEvents.Where(e => e.TripLogId == trip).ToListAsync())
        {
            report.RecordedAt = armedAt.AddHours(1);
        }

        await db.SaveChangesAsync();
    }

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private sealed record Published(Guid Trip, string Token);

    /// <summary>
    /// One cave with a party in it now and a trip of three people that is to be finished, both
    /// published, and the four addresses the first trip's link reads.
    /// </summary>
    private sealed record PublishedCave(Guid Cave, Published Now, Published Then)
    {
        public string Follow => $"/api/v1/public/trips/{Uri.EscapeDataString(Now.Token)}";

        public string Live => $"{Follow}/live";

        public string Past => $"{Follow}/past";

        public string PastTrip => $"{Past}/{Then.Trip}";

        public string[] Routes => [Follow, Live, Past, PastTrip];
    }

    private async Task<PublishedCave> PublishedCaveAsync()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var then = await PublishedTripAsync("Last week", model, Ana, Bogdan, Leaver);
        var now = await PublishedTripAsync("Tonight", model, $"Guest {Guid.NewGuid():N}"[..24]);
        return new PublishedCave(cave, now, then);
    }

    /// <summary>A trip of the named people, armed on the model, the first of them placed, and published.</summary>
    private async Task<Published> PublishedTripAsync(string title, Guid model, params string[] party)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = party.Select(name => new { newCaverName = name }).ToArray(),
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        Guid caver;
        using (var scope = asInstalled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caver = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
                .OrderBy(p => p.Id).Select(p => p.CaverId).FirstAsync();
        }

        (await PutConfigAsync(trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/events",
            new { caverIds = new[] { caver }, kind = "atStation", stationName = "cave.upper.2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var minted = await owner.PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var link = JsonDocument.Parse(await minted.Content.ReadAsStringAsync()).RootElement;
        return new Published(trip, link.GetProperty("token").GetString()!);
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pictures Cave {Guid.NewGuid():N}"[..30],
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
    /// A survey model created the real way — so it has a file, and the answers that draw it hand
    /// out a signed address — then stations seeded straight into the graph tables.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "trippics.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 31700;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", 300, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(Guid modelId, string name, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = name[..name.LastIndexOf('.')],
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    private async Task<HttpResponseMessage> PutConfigAsync(Guid trip, object body)
    {
        var current = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await owner.SendAsync(request);
    }
}
