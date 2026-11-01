// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The relations graph's attachment, trip-participant, expedition, cabinet, tag, group
/// membership and survey-model categories, each held to the route that already answers the
/// same relation: for an administrator, the fixtures' owner, a member of the caving group some
/// rows are shared with and a plain viewer outside it, the graph's neighbours equal that
/// route's answer to the same caller — from both ends where the graph reads a relation both
/// ways. Every fixture has a part the viewer is kept out of (a private document, a private
/// trip, a denied feature, a location-protected cave, a row shared with the group only), and
/// the owner's answer in the same theory shows that part exists; the group's rows are asserted
/// to reach the member and not the viewer.
/// </summary>
public sealed class GraphRelationParityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private GraphTestKit kit = null!;
    private string m = null!;
    private Guid cave;
    private Guid guarded;
    private Guid hiddenFeature;
    private Guid groupFeature;
    private Guid groupShelfDocument;
    private Guid groupTrip;
    private Guid openDocument;
    private Guid privateDocument;
    private Guid shelfOnlyDocument;
    private Guid shelf;
    private Guid childShelf;
    private long tagId;
    private Guid group;
    private Guid caverA;
    private Guid caverB;
    private Guid openTrip;
    private Guid privateTrip;
    private Guid camp;

    public GraphRelationParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public static TheoryData<string> Viewers => new() { "admin", "owner", "member", "viewer" };

    /// <summary>The seats that read past visibility and every grant: the fixtures' whole truth.</summary>
    private static bool SeesAll(string who) => who is "admin" or "owner";

    public async ValueTask InitializeAsync()
    {
        kit = await GraphTestKit.CreateAsync(factory, "gxr");
        m = Guid.NewGuid().ToString("N")[..8];

        cave = await kit.CaveAsync($"Graf rel cave {m}");
        guarded = await kit.CaveAsync($"Graf rel guarded {m}", locationProtected: true);
        hiddenFeature = await kit.FeatureAsync($"Graf rel hidden {m}");
        await kit.DenyViewerAsync(hiddenFeature);
        groupFeature = await kit.FeatureAsync($"Graf rel group {m}");
        await kit.ShareWithGroupAsync(GroupShared.Feature, groupFeature);

        // Documents: one widened to every signed-in caller, one left as born (private).
        var (openDoc, openFile) = await UploadDocumentAsync($"graf-open-{m}.txt");
        await MakeDocumentReadableAsync(openDoc);
        var (privateDoc, privateFile) = await UploadDocumentAsync($"graf-private-{m}.txt");
        openDocument = openDoc;
        privateDocument = privateDoc;
        await AttachAsync(openFile, cave, "document");
        await AttachAsync(privateFile, cave, "map2d");
        await AttachAsync(openFile, hiddenFeature, "other");
        await AttachAsync(openFile, groupFeature, "other");

        shelf = await CabinetAsync($"Graf shelf {m}", null);
        childShelf = await CabinetAsync($"Graf shelf child {m}", shelf);
        await FileAsync(shelf, openDocument);
        await FileAsync(shelf, privateDocument);

        // Private and hung on nothing, so no reach through an attachment opens it: the one
        // filed document the viewer genuinely may not read.
        (shelfOnlyDocument, _) = await UploadDocumentAsync($"graf-shelf-only-{m}.txt");
        await FileAsync(shelf, shelfOnlyDocument);

        // Filed and hung on nothing, shared with the group: the member reads it, the viewer not.
        (groupShelfDocument, _) = await UploadDocumentAsync($"graf-shelf-group-{m}.txt");
        await kit.ShareWithGroupAsync(GroupShared.Document, groupShelfDocument);
        await FileAsync(shelf, groupShelfDocument);

        var tagName = $"graf-tag-{m}";
        await TagAsync(tagName, cave);
        await TagAsync(tagName, hiddenFeature);
        await TagAsync(tagName, groupFeature);
        await TagAsync($"graf-other-{m}", cave);

        openTrip = await TripAsync($"Graf rel open trip {m}", "authenticated");
        privateTrip = await TripAsync($"Graf rel private trip {m}", "private");
        groupTrip = await TripAsync($"Graf rel group trip {m}", "authenticated");
        await kit.ShareWithGroupAsync(GroupShared.TripLog, groupTrip);
        camp = await CampAsync($"Graf rel camp {m}");
        await JoinAsync(camp, openTrip);
        await JoinAsync(camp, privateTrip);
        await JoinAsync(camp, groupTrip);

        group = await IdOfAsync(
            await kit.Admin.PostAsJsonAsync("/api/v1/caving-groups", new
            {
                name = $"Clubul Graf {m}",
                type = "cavingClub",
                description = (string?)null,
                website = (string?)null,
            }),
            HttpStatusCode.Created);

        await SurveyModelAsync(cave, $"graf-open-{m}.lox");
        await SurveyModelAsync(guarded, $"graf-guarded-{m}.lox");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        tagId = await db.Tags.Where(t => t.Name == tagName).Select(t => t.Id).SingleAsync();
        var a = new Caver { FullName = $"Speolog Graf A {m}" };
        var b = new Caver { FullName = $"Speolog Graf B {m}" };
        db.Cavers.AddRange(a, b);
        await db.SaveChangesAsync();
        caverA = a.Id;
        caverB = b.Id;

        var tripRole = await db.TripParticipantRoles.OrderBy(r => r.Id).Select(r => r.Id).FirstAsync();
        foreach (var (trip, caver) in new[] { (openTrip, caverA), (openTrip, caverB), (privateTrip, caverA), (groupTrip, caverA) })
        {
            db.TripLogParticipants.Add(new TripLogParticipant { TripLogId = trip, CaverId = caver, RoleId = tripRole });
        }

        var rosterRole = await db.ExpeditionRosterRoles.OrderBy(r => r.Id).Select(r => r.Id).FirstAsync();
        db.ExpeditionRoster.Add(new ExpeditionRosterEntry { ExpeditionId = camp, CaverId = caverA, RoleId = rosterRole, FromDate = new DateOnly(2026, 7, 18) });
        db.ExpeditionRoster.Add(new ExpeditionRosterEntry { ExpeditionId = camp, CaverId = caverB, RoleId = rosterRole, FromDate = new DateOnly(2026, 7, 19) });
        db.CavingGroupMemberships.Add(new CavingGroupMembership { CavingGroupId = group, CaverId = caverA, Role = CavingGroupRole.Member });
        db.CavingGroupMemberships.Add(new CavingGroupMembership { CavingGroupId = group, CaverId = caverB, Role = CavingGroupRole.Admin });
        await db.SaveChangesAsync();
    }

    private HttpClient As(string who) => who switch
    {
        "admin" => kit.Admin,
        "owner" => kit.Owner,
        "member" => kit.Member,
        _ => kit.Viewer,
    };

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Attachments_are_the_attachment_list_from_both_ends(string who)
    {
        var client = As(who);
        var listed = (await GraphTestKit.BodyAsync(client, $"/api/v1/attachments?entityType=feature&entityId={cave}"))
            .EnumerateArray().Select(a => DocumentOfFile(a.GetProperty("fileId").GetGuid())).ToHashSet();
        var graph = await GraphTestKit.GraphAsync(client, "feature", cave, "attachment");
        GraphTestKit.Neighbours(graph, "attachment", GraphTestKit.FeatureKey(cave))
            .ShouldBe(listed.Select(d => $"document:{d}").ToHashSet(), ignoreOrder: true);
        if (who == "owner")
        {
            listed.ShouldBe([openDocument, privateDocument], ignoreOrder: true);
        }

        // From the document: the items whose attachment list this caller finds it in. The
        // denied feature's list refuses the viewer, so the viewer is not told it is attached.
        var expected = new HashSet<string>();
        foreach (var item in new[] { cave, hiddenFeature, groupFeature })
        {
            var response = await client.GetAsync($"/api/v1/attachments?entityType=feature&entityId={item}");
            if (response.StatusCode == HttpStatusCode.OK
                && (await ReadAsync(response)).EnumerateArray().Any(a => DocumentOfFile(a.GetProperty("fileId").GetGuid()) == openDocument))
            {
                expected.Add(GraphTestKit.FeatureKey(item));
            }
        }

        var fromDocument = await GraphTestKit.GraphAsync(client, "document", openDocument, "attachment");
        GraphTestKit.Neighbours(fromDocument, "attachment", $"document:{openDocument}").ShouldBe(expected, ignoreOrder: true);
        if (who == "viewer")
        {
            expected.ShouldBe([GraphTestKit.FeatureKey(cave)]);
        }
        else
        {
            expected.Count.ShouldBe(3);
        }
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Cabinet_shelves_and_filings_are_the_cabinet_routes(string who)
    {
        var client = As(who);
        var filed = (await GraphTestKit.BodyAsync(client, $"/api/v1/cabinets/{shelf}/documents?pageSize=500"))
            .GetProperty("items").EnumerateArray().Select(d => $"document:{d.GetProperty("id").GetGuid()}").ToHashSet();
        var graph = await GraphTestKit.GraphAsync(client, "cabinet", shelf, "cabinet");
        var neighbours = GraphTestKit.Neighbours(graph, "cabinet", $"cabinet:{shelf}");
        neighbours.ShouldBe([.. filed, $"cabinet:{childShelf}"], ignoreOrder: true);
        filed.Contains($"document:{openDocument}").ShouldBeTrue();
        filed.Contains($"document:{shelfOnlyDocument}").ShouldBe(SeesAll(who));
        filed.Contains($"document:{groupShelfDocument}").ShouldBe(who != "viewer");

        var fromDocument = await GraphTestKit.GraphAsync(client, "document", openDocument, "cabinet");
        GraphTestKit.Neighbours(fromDocument, "cabinet", $"document:{openDocument}").ShouldBe([$"cabinet:{shelf}"]);
        var fromChild = await GraphTestKit.GraphAsync(client, "cabinet", childShelf, "cabinet");
        GraphTestKit.Neighbours(fromChild, "cabinet", $"cabinet:{childShelf}").ShouldBe([$"cabinet:{shelf}"]);
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Tags_are_the_taggings_list_from_both_ends(string who)
    {
        var client = As(who);
        var listed = (await GraphTestKit.BodyAsync(client, $"/api/v1/taggings?entityType=feature&entityId={cave}"))
            .EnumerateArray().Select(t => $"tag:{t.GetProperty("tag").GetProperty("id").GetInt64()}").ToHashSet();
        var graph = await GraphTestKit.GraphAsync(client, "feature", cave, "tag");
        GraphTestKit.Neighbours(graph, "tag", GraphTestKit.FeatureKey(cave)).ShouldBe(listed, ignoreOrder: true);
        listed.Count.ShouldBe(2);

        var expected = new HashSet<string>();
        foreach (var item in new[] { cave, hiddenFeature, groupFeature })
        {
            var response = await client.GetAsync($"/api/v1/taggings?entityType=feature&entityId={item}");
            if (response.StatusCode == HttpStatusCode.OK
                && (await ReadAsync(response)).EnumerateArray().Any(t => t.GetProperty("tag").GetProperty("id").GetInt64() == tagId))
            {
                expected.Add(GraphTestKit.FeatureKey(item));
            }
        }

        var fromTag = await GraphTestKit.GraphAsync(client, $"type=tag&id={tagId}&categories=tag");
        GraphTestKit.Neighbours(fromTag, "tag", $"tag:{tagId}").ShouldBe(expected, ignoreOrder: true);
        GraphTestKit.Count(fromTag, "tag").GetProperty("total").GetInt32().ShouldBe(expected.Count);
        expected.Count.ShouldBe(who == "viewer" ? 1 : 3);
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Survey_models_are_the_cave_model_list(string who)
    {
        var client = As(who);
        foreach (var seed in new[] { cave, guarded })
        {
            var listed = (await GraphTestKit.BodyAsync(client, $"/api/v1/caves/{seed}/survey-models"))
                .EnumerateArray().Select(s => $"surveyModel:{s.GetProperty("id").GetGuid()}").ToHashSet();
            var graph = await GraphTestKit.GraphAsync(client, "feature", seed, "survey");
            GraphTestKit.Neighbours(graph, "survey", GraphTestKit.FeatureKey(seed)).ShouldBe(listed, ignoreOrder: true);
            GraphTestKit.Count(graph, "survey").GetProperty("total").GetInt32().ShouldBe(listed.Count);

            // A guarded cave's models are its location: none for a caller without exact view,
            // and the owner's answer shows there is one to withhold.
            listed.Count.ShouldBe(seed == guarded && !SeesAll(who) ? 0 : 1);
        }
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Group_members_are_the_member_list_from_both_ends(string who)
    {
        var client = As(who);
        var response = await client.GetAsync($"/api/v1/caving-groups/{group}/members");
        var listed = response.StatusCode == HttpStatusCode.OK
            ? (await ReadAsync(response)).EnumerateArray().Select(x => $"caver:{x.GetProperty("caverId").GetGuid()}").ToHashSet()
            : [];
        var graph = await GraphTestKit.GraphAsync(client, "cavingGroup", group, "groupMembership");
        GraphTestKit.Neighbours(graph, "groupMembership", $"cavingGroup:{group}").ShouldBe(listed, ignoreOrder: true);
        if (SeesAll(who))
        {
            // The group's creator is a member too; the two fixtures' people are what matters.
            listed.ShouldContain($"caver:{caverA}");
            listed.ShouldContain($"caver:{caverB}");
        }

        var fromCaver = await GraphTestKit.GraphAsync(client, "caver", caverB, "groupMembership");
        GraphTestKit.Neighbours(fromCaver, "groupMembership", $"caver:{caverB}")
            .ShouldBe(listed.Count > 0 ? [$"cavingGroup:{group}"] : [], ignoreOrder: true);
        var edge = GraphTestKit.Edges(fromCaver, "groupMembership").SingleOrDefault();
        if (edge.ValueKind != JsonValueKind.Undefined)
        {
            edge.GetProperty("relation").GetString().ShouldBe("admin");
        }
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Trip_participants_are_the_trip_page_and_the_person_filter(string who)
    {
        var client = As(who);
        foreach (var trip in new[] { openTrip, privateTrip, groupTrip })
        {
            var response = await client.GetAsync($"/api/v1/trip-logs/{trip}");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                (await client.GetAsync($"/api/v1/graph/neighbourhood?type=tripLog&id={trip}")).StatusCode
                    .ShouldBe(HttpStatusCode.NotFound);
                continue;
            }

            var detail = await ReadAsync(response);
            var roster = detail.GetProperty("participants").EnumerateArray()
                .Concat(detail.TryGetProperty("proposers", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [])
                .Select(x => $"caver:{x.GetProperty("caverId").GetGuid()}").ToHashSet();
            var graph = await GraphTestKit.GraphAsync(client, "tripLog", trip, "tripParticipant");
            GraphTestKit.Neighbours(graph, "tripParticipant", $"tripLog:{trip}").ShouldBe(roster, ignoreOrder: true);
        }

        var listing = (await GraphTestKit.BodyAsync(client, $"/api/v1/trip-logs/?pageSize=500&participantIds={caverA}"))
            .GetProperty("items").EnumerateArray().Select(t => $"tripLog:{t.GetProperty("id").GetGuid()}").ToHashSet();
        var fromCaver = await GraphTestKit.GraphAsync(client, "caver", caverA, "tripParticipant");
        GraphTestKit.Neighbours(fromCaver, "tripParticipant", $"caver:{caverA}").ShouldBe(listing, ignoreOrder: true);
        listing.Contains($"tripLog:{openTrip}").ShouldBeTrue();
        listing.Contains($"tripLog:{privateTrip}").ShouldBe(SeesAll(who));
        listing.Contains($"tripLog:{groupTrip}").ShouldBe(who != "viewer");
    }

    [Theory]
    [MemberData(nameof(Viewers))]
    public async Task Expedition_trips_and_roster_are_the_camp_routes(string who)
    {
        var client = As(who);
        var trips = (await GraphTestKit.BodyAsync(client, $"/api/v1/trip-logs/?pageSize=500&expeditionId={camp}"))
            .GetProperty("items").EnumerateArray().Select(t => $"tripLog:{t.GetProperty("id").GetGuid()}").ToHashSet();
        var rosterResponse = await client.GetAsync($"/api/v1/expeditions/{camp}/roster");
        var roster = rosterResponse.StatusCode == HttpStatusCode.OK
            ? (await ReadAsync(rosterResponse)).GetProperty("entries").EnumerateArray()
                .Select(x => $"caver:{x.GetProperty("caverId").GetGuid()}").ToHashSet()
            : [];

        var graph = await GraphTestKit.GraphAsync(client, "expedition", camp, "expedition");
        var edges = GraphTestKit.Edges(graph, "expedition").ToList();
        edges.Where(e => e.GetProperty("relation").GetString() == "trip").Select(e => e.GetProperty("target").GetString())
            .ShouldBe(trips, ignoreOrder: true);
        edges.Where(e => e.GetProperty("relation").GetString() == "roster").Select(e => e.GetProperty("target").GetString())
            .ShouldBe(roster, ignoreOrder: true);
        trips.Contains($"tripLog:{privateTrip}").ShouldBe(SeesAll(who));
        trips.Contains($"tripLog:{groupTrip}").ShouldBe(who != "viewer");

        var fromTrip = await GraphTestKit.GraphAsync(client, "tripLog", openTrip, "expedition");
        GraphTestKit.Neighbours(fromTrip, "expedition", $"tripLog:{openTrip}").ShouldBe([$"expedition:{camp}"]);
        var fromCaver = await GraphTestKit.GraphAsync(client, "caver", caverB, "expedition");
        GraphTestKit.Neighbours(fromCaver, "expedition", $"caver:{caverB}")
            .ShouldBe(roster.Count > 0 ? [$"expedition:{camp}"] : [], ignoreOrder: true);
    }

    private Guid DocumentOfFile(Guid fileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return db.StoredFiles.AsNoTracking().Where(f => f.Id == fileId)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
            .Single();
    }

    private async Task<(Guid DocumentId, Guid FileId)> UploadDocumentAsync(string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"contents of {fileName}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var fileId = await IdOfAsync(await kit.Owner.PostAsync("/api/v1/files/?allowDuplicate=true", form), HttpStatusCode.Created);
        return (DocumentOfFile(fileId), fileId);
    }

    private async Task MakeDocumentReadableAsync(Guid documentId)
    {
        var current = await GraphTestKit.BodyAsync(kit.Owner, $"/api/v1/documents/{documentId}");
        var response = await kit.Owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = current.GetProperty("title").GetString(),
            documentTypeId = current.GetProperty("documentTypeId").ValueKind == JsonValueKind.Number
                ? current.GetProperty("documentTypeId").GetInt64()
                : (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task AttachAsync(Guid fileId, Guid featureId, string role)
    {
        var response = await kit.Owner.PostAsJsonAsync("/api/v1/attachments", new
        {
            fileId,
            entityType = "feature",
            entityId = featureId,
            role,
            caption = (string?)null,
            sortOrder = 0,
        });
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CabinetAsync(string name, Guid? parentId) =>
        await IdOfAsync(
            await kit.Owner.PostAsJsonAsync("/api/v1/cabinets", new { name, description = (string?)null, parentId }),
            HttpStatusCode.Created);

    private async Task FileAsync(Guid cabinetId, Guid documentId)
    {
        var response = await kit.Owner.PutAsync($"/api/v1/cabinets/{cabinetId}/documents/{documentId}", null);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task TagAsync(string tagName, Guid featureId)
    {
        var response = await kit.Owner.PostAsJsonAsync("/api/v1/taggings", new { tagName, entityType = "feature", entityId = featureId });
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> TripAsync(string title, string visibility) =>
        await IdOfAsync(
            await kit.Owner.PostAsJsonAsync("/api/v1/trip-logs/", new
            {
                title,
                tripDate = "2026-07-20",
                participants = Array.Empty<object>(),
                visibility,
                hadIncident = false,
            }),
            HttpStatusCode.Created);

    private async Task<Guid> CampAsync(string name) =>
        await IdOfAsync(
            await kit.Owner.PostAsJsonAsync("/api/v1/expeditions/", new
            {
                name,
                description = "A camp.",
                startDate = "2026-07-18",
                endDate = "2026-08-01",
                geom = (object?)null,
                cavingGroupId = (Guid?)null,
                visibility = "authenticated",
            }),
            HttpStatusCode.Created);

    private async Task JoinAsync(Guid expeditionId, Guid tripLogId)
    {
        var response = await kit.Owner.PostAsJsonAsync($"/api/v1/expeditions/{expeditionId}/trips", new { tripLogId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task SurveyModelAsync(Guid caveId, string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes($"LOX-{Guid.NewGuid():N}"));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        await IdOfAsync(await kit.Owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form), HttpStatusCode.Created);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync() => await kit.DisposeAsync();

    public void Dispose() => factory.Dispose();
}
