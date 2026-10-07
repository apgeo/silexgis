// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The warning a publisher and an administrator get when a protected cave that is not the trip's
/// own stands inside the rectangle the published survey's stations span.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three things are held down.</b> That the warning follows position and protection and nothing
/// else — a neighbour inside the rectangle warns only while it is protected, and a protected one
/// beside the rectangle never does. That it is a warning: every publication here succeeds. And
/// that it cannot be used to find a cave: somebody who may not see where a protected cave is gets
/// the same answer whether or not one is there.
/// </para>
/// <para>
/// <b>Each test surveys ground of its own.</b> The tests of this class share one database, and a
/// neighbour is anything standing inside a rectangle, so two tests on the same ground would answer
/// for each other. Each takes a small rectangle at a place drawn at random, far from where the
/// other fixtures of the suite put their stations.
/// </para>
/// <para>
/// A cave is placed the way a person places one, by giving it a main entrance, and protected
/// through the write service that maintains the derived columns: a flag written straight into the
/// column leaves them stale, and the assertion then passes whether or not the rule works.
/// </para>
/// </remarks>
public sealed class PublishedSurveyBoundsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string List = "/api/v1/admin/published-trips";

    /// <summary>How wide and tall the surveyed rectangle is, in degrees.</summary>
    private const double Span = 0.01;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    // The south-west corner of this test's survey.
    private readonly double west = 130 + (Random.Shared.NextDouble() * 15);
    private readonly double south = -38 + (Random.Shared.NextDouble() * 15);

    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private long caveTypeId;
    private long entranceTypeId;

    public PublishedSurveyBoundsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"pubbounds-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"pubbounds-adm-{suffix}@t.local";
        var editorEmail = $"pubbounds-ed-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, editorEmail);
        admin = await AuthHelper.BearerClientAsync(factory, adminEmail);
        editor = await AuthHelper.BearerClientAsync(factory, editorEmail);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    private (double Lon, double Lat) Inside => (west + (Span / 2), south + (Span / 2));

    /// <summary>Level with the middle of the rectangle and a whole rectangle's width east of it.</summary>
    private (double Lon, double Lat) Beside => (west + (Span * 2), south + (Span / 2));

    [Fact]
    public async Task A_protected_neighbour_inside_the_surveys_bounds_warns_and_one_beside_them_or_unprotected_does_not()
    {
        // The trip's own cave stands inside its own survey, as a cave does; it is not a neighbour.
        var trip = await ArmedTripAsync(admin, ownEntranceAt: Inside);

        (await MintAsync(admin, trip)).Warned.ShouldBeFalse();

        // Two neighbours, each wrong in one respect: one inside the rectangle and not protected,
        // one protected and beside the rectangle.
        var (within, withinName) = await NeighbourAsync(admin, Inside, locationProtected: false);
        _ = await NeighbourAsync(admin, Beside, locationProtected: true);

        var quiet = await MintAsync(admin, trip);
        quiet.Warned.ShouldBeFalse();
        (await ListedWarningAsync(quiet.Id)).ShouldBeFalse();

        // The one inside becomes protected, and nothing else changes.
        await SetLocationProtectedAsync(within, true);

        var minted = await PublishAsync(admin, trip);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await minted.Content.ReadAsStringAsync();
        var answer = JsonDocument.Parse(body).RootElement;

        // A warning and not a refusal: the link was handed out, and it says to look.
        answer.GetProperty("token").GetString().ShouldNotBeNullOrWhiteSpace();
        answer.GetProperty("protectedCaveWithinSurveyBounds").ValueKind.ShouldBe(JsonValueKind.True);

        // One bit. Nothing beside it says which cave, how many, or where.
        answer.EnumerateObject().Select(p => p.Name).Order().ShouldBe(
            ["createdAt", "expiresAt", "id", "protectedCaveWithinSurveyBounds", "token"]);
        body.ShouldNotContain(within.ToString());
        body.ShouldNotContain(withinName);

        // The list says the same of that link — and of the one handed out before the neighbour was
        // protected, because it is a reading of what stands there now, not a record of what was
        // answered then.
        var warned = answer.GetProperty("id").GetGuid();
        (await ListedWarningAsync(warned)).ShouldBeTrue();
        (await ListedWarningAsync(quiet.Id)).ShouldBeTrue();

        // A replacement hands out the same survey, and so the same warning.
        var replaced = await admin.PostAsync($"{Shares(trip)}/{warned}/replace", null);
        replaced.StatusCode.ShouldBe(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        var fresh = JsonDocument.Parse(await replaced.Content.ReadAsStringAsync()).RootElement;
        fresh.GetProperty("protectedCaveWithinSurveyBounds").GetBoolean().ShouldBeTrue();

        // The link that was replaced opens nothing now, so it hands out no survey to warn about.
        (await ListedWarningAsync(warned)).ShouldBeFalse();
        (await ListedWarningAsync(fresh.GetProperty("id").GetGuid())).ShouldBeTrue();

        // And the protection lifted, with the neighbour standing exactly where it stood: no warning.
        await SetLocationProtectedAsync(within, false);
        (await MintAsync(admin, trip)).Warned.ShouldBeFalse();
        (await ListedWarningAsync(quiet.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_publisher_who_may_not_place_the_protected_neighbour_is_told_nothing_and_the_administrators_list_is()
    {
        // The editor's own trip, in the editor's own cave: theirs to publish.
        var trip = await ArmedTripAsync(editor, ownEntranceAt: null);

        // Somebody else's protected cave inside the survey's rectangle. The editor did not make it
        // and holds no grant of its exact position.
        var (neighbour, _) = await NeighbourAsync(admin, Inside, locationProtected: true);

        // That the editor really cannot place it, shown where a position is shown: they are given
        // an approximate one, and the administrator the exact one.
        (await EntranceIsApproximateAsync(editor, neighbour)).ShouldBeTrue();
        (await EntranceIsApproximateAsync(admin, neighbour)).ShouldBeFalse();

        // So publishing tells the editor nothing — the answer they would get were nothing there.
        // Told otherwise, they could file a survey of their own drawing under a cave of their own
        // and ask of any rectangle whether a protected cave stands in it.
        var theirs = await MintAsync(editor, trip);
        theirs.Warned.ShouldBeFalse();

        // The same trip, the same survey, the same instant, asked by somebody who may place it.
        (await MintAsync(admin, trip)).Warned.ShouldBeTrue();

        // And the installation's list, which is read only by people who may place every cave, says
        // it of the editor's own link.
        (await ListedWarningAsync(theirs.Id)).ShouldBeTrue();
    }

    /// <summary>
    /// Being entitled to a cave's exact position is not being allowed to read the cave.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state is built on purpose, because the two rights usually travel together and a
    /// fixture in which they do proves nothing. A member owns a protected area, which entitles
    /// them to the exact position of everything beneath it. Somebody else keeps a private cave
    /// beneath that area: the member cannot open it and no list shows it to them. Both halves are
    /// asserted before anything is published — that the member is refused the cave, and that the
    /// position rule alone would let them place it — so the answer that follows can only have
    /// come from asking whether they may read it.
    /// </para>
    /// <para>
    /// The member is an ordinary account given the right to create and nothing that reads past
    /// anybody's privacy; an editor would not do, since an editor reads every cave.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_cave_the_publisher_cannot_read_is_not_counted_even_where_they_hold_its_exact_position()
    {
        var (member, memberId) = await MemberWhoReadsOnlyTheirOwnAsync();

        // The member's protected area, and somebody else's private cave beneath it, standing
        // inside the rectangle the member's survey will span.
        var area = await ProtectedAreaAsync(member);
        var hidden = await CaveAsync(admin, locationProtected: false, visibility: "private", parent: area);
        await EntranceAsync(admin, hidden.Id, Inside);

        // Refused the cave, exactly as a cave that does not exist is refused.
        (await member.GetAsync($"/api/v1/caves/{hidden.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/v1/caves/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/v1/caves/{hidden.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var protection = scope.ServiceProvider.GetRequiredService<FeatureProtection>();
            var ctx = await AccessContextResolver.ResolveAsync(db, memberId);

            // The cave and its entrance stand inside the rectangle and are protected by the area.
            var beneath = await db.Features.AsNoTracking()
                .Where(f => f.AncestorIds.Contains(hidden.Id))
                .Select(f => new { f.Id, f.IsProtectedEffective, f.Geom })
                .ToListAsync();
            beneath.Count.ShouldBe(2);
            beneath.ShouldAllBe(f => f.IsProtectedEffective && f.Geom != null);

            // The position rule alone lets the member place both ...
            var ids = beneath.Select(f => f.Id).ToList();
            (await protection.ExactViewIdsAsync(ctx, ids)).ShouldBe(ids, ignoreOrder: true);

            // ... and the read rule shows them neither.
            (await db.Features.AsNoTracking()
                    .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                    .CountAsync(f => ids.Contains(f.Id)))
                .ShouldBe(0);
        }

        // So publishing tells the member nothing. Told otherwise, the owner of an area could find
        // every private cave under it by publishing surveys of rectangles of their choosing.
        var trip = await ArmedTripAsync(member, ownEntranceAt: null);
        var theirs = await MintAsync(member, trip);
        theirs.Warned.ShouldBeFalse();
        var replaced = await member.PostAsync($"{Shares(trip)}/{theirs.Id}/replace", null);
        replaced.StatusCode.ShouldBe(HttpStatusCode.Created, await replaced.Content.ReadAsStringAsync());
        var fresh = JsonDocument.Parse(await replaced.Content.ReadAsStringAsync()).RootElement;
        fresh.GetProperty("protectedCaveWithinSurveyBounds").GetBoolean().ShouldBeFalse();

        // The same survey asked about by somebody who reads every cave: it is there, and it counts.
        (await MintAsync(admin, trip)).Warned.ShouldBeTrue();
        (await ListedWarningAsync(fresh.GetProperty("id").GetGuid())).ShouldBeTrue();
    }

    // ---- fixtures ----------------------------------------------------------------------------

    /// <summary>
    /// An account that may create content and reads only what is its own or what an audience
    /// admits it to — no entry of it reads past anybody's privacy.
    /// </summary>
    private async Task<(HttpClient Client, Guid Id)> MemberWhoReadsOnlyTheirOwnAsync()
    {
        var email = $"pubbounds-mem-{Guid.NewGuid():N}@t.local";
        var id = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var domain in new[]
                     {
                         AccessDomain.Features, AccessDomain.TripLogs, AccessDomain.Files, AccessDomain.Cavers,
                     })
            {
                db.AccessEntries.Add(new AccessEntry
                {
                    SubjectKind = AccessSubjectKind.User,
                    SubjectId = id,
                    Effect = AccessEffect.Allow,
                    Domain = domain,
                    Actions = AccessAction.Create,
                    ScopeKind = AccessScopeKind.All,
                });
            }

            await db.SaveChangesAsync();
        }

        return (await AuthHelper.BearerClientAsync(factory, email), id);
    }

    /// <summary>A private karst area of <paramref name="owner"/>'s around this test's ground, protected.</summary>
    private async Task<Guid> ProtectedAreaAsync(HttpClient owner)
    {
        long karstAreaTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        var (w, s, e, n) = (west - Span, south - Span, west + (Span * 2), south + (Span * 2));
        var created = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Bounds Area {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[] { new[] { w, s }, new[] { e, s }, new[] { e, n }, new[] { w, n }, new[] { w, s } },
                },
            },
            visibility = "private",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var area = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await SetLocationProtectedAsync(area, true);
        return area;
    }

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private sealed record Minted(Guid Id, bool Warned);

    private static Task<HttpResponseMessage> PublishAsync(HttpClient publisher, Guid trip) =>
        publisher.PostAsync(Shares(trip), null);

    private static async Task<Minted> MintAsync(HttpClient publisher, Guid trip)
    {
        var minted = await PublishAsync(publisher, trip);
        var payload = await minted.Content.ReadAsStringAsync();
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var body = JsonDocument.Parse(payload).RootElement;
        return new Minted(
            body.GetProperty("id").GetGuid(), body.GetProperty("protectedCaveWithinSurveyBounds").GetBoolean());
    }

    /// <summary>What the administrators' list says of one link, found by its id.</summary>
    private async Task<bool> ListedWarningAsync(Guid link)
    {
        var response = await admin.GetAsync($"{List}?pageSize=500");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var row = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == link);
        return row.GetProperty("protectedCaveWithinSurveyBounds").GetBoolean();
    }

    /// <summary>
    /// A cave of <paramref name="coordinator"/>'s, a survey of it spanning this test's rectangle,
    /// and a trip whose watch is running on that survey.
    /// </summary>
    private async Task<Guid> ArmedTripAsync(HttpClient coordinator, (double Lon, double Lat)? ownEntranceAt)
    {
        var (cave, _) = await CaveAsync(coordinator, locationProtected: false);
        if (ownEntranceAt is { } at) await EntranceAsync(coordinator, cave, at);
        var model = await ModelAsync(coordinator, cave);

        var created = await coordinator.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Bounds {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = new[] { new { newCaverName = $"Guest {Guid.NewGuid():N}"[..24] } },
            visibility = "authenticated",
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var current = await coordinator.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var arm = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(new { state = "armed", surveyModelId = model }),
        };
        arm.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        var armed = await coordinator.SendAsync(arm);
        armed.StatusCode.ShouldBe(HttpStatusCode.OK, await armed.Content.ReadAsStringAsync());
        return trip;
    }

    /// <summary>A cave that is not the trip's, standing at a given place.</summary>
    private async Task<(Guid Id, string Name)> NeighbourAsync(
        HttpClient creator, (double Lon, double Lat) at, bool locationProtected)
    {
        var cave = await CaveAsync(creator, locationProtected);
        await EntranceAsync(creator, cave.Id, at);
        return cave;
    }

    private async Task<(Guid Id, string Name)> CaveAsync(
        HttpClient creator, bool locationProtected, string visibility = "authenticated", Guid? parent = null)
    {
        var name = $"Bounds Cave {Guid.NewGuid():N}"[..30];
        var response = await creator.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            parentId = parent,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), name);
    }

    /// <summary>The cave's main entrance, which is what gives the cave itself a position.</summary>
    private async Task EntranceAsync(HttpClient creator, Guid cave, (double Lon, double Lat) at)
    {
        var response = await creator.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { at.Lon, at.Lat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<bool> EntranceIsApproximateAsync(HttpClient reader, Guid cave)
    {
        var entrances = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{cave}/entrances");
        return entrances[0].GetProperty("approximateLocation").GetBoolean();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    /// <summary>
    /// A survey model created the real way, then given two stations at opposite corners of this
    /// test's rectangle — which is all a rectangle needs.
    /// </summary>
    private async Task<Guid> ModelAsync(HttpClient uploader, Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "pubbounds.3d");
        var created = await uploader.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 4326;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.sw", west, south),
            Station(modelId, "cave.ne", west + Span, south + Span));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(Guid modelId, string name, double longitude, double latitude) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = "cave",
            Position = new Point(new CoordinateZ(longitude, latitude, 300)) { SRID = 4326 },
            Flags = SurveyStationFlags.Underground,
        };
}
