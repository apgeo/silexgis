// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Resolving a code printed on a cave label for somebody who is not signed in: what a code that
/// resolves discloses, and that every reason a code does not resolve looks the same.
/// </summary>
public sealed class PublicQrLandingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;
    private long cavePlaceTypeId;
    private long entranceTypeId;

    public PublicQrLandingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"qrland-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            cavePlaceTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place")
                .Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"qrland-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The cave here is deliberately <b>not</b> location-protected, which is the only fixture
    /// that can catch the mistake this route exists to make impossible.
    /// <para>
    /// The batch protection helper answers "may this caller see exact coordinates", and for an
    /// unprotected feature it answers yes — to everyone, signed in or not, because that is the
    /// right answer to the question it was asked. So a version of this route that emitted a
    /// position "run through protection" would hand full-precision coordinates to the entire
    /// internet for every cave nobody had flipped protection on, which is most of them, and
    /// would sail through a test built on a protected cave. The precondition below states out
    /// loud that this fixture is in exactly that arm, so the assertions after it refute the
    /// leaking implementation instead of coinciding with the safe one.
    /// </para>
    /// <para>
    /// And what is asserted is the shape of the answer, not the value of any field in it: the
    /// response carries one property and it is the installation's name. A field holding a
    /// blanked or snapped position would fail this, which is the point — the rule is that there
    /// is nowhere to put one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_published_unprotected_cave_gives_up_no_coordinate_and_no_name()
    {
        const double Lon = 25.7712345;
        const double Lat = 45.3798765;
        var caveName = $"Peștera {Guid.NewGuid():N}"[..24];
        var caveId = await CreateCaveAsync(caveName);
        await CreateEntranceAsync(caveId, Lon, Lat);

        var qcri = $"a{Guid.NewGuid():N}"[..9];
        var placeName = $"Sala {Guid.NewGuid():N}"[..20];
        var placeId = await CreatePlaceAsync(caveId, placeName, qcri: qcri, pci: "RO-BH-0001-014");

        using (var scope = factory.Services.CreateScope())
        {
            var protection = scope.ServiceProvider.GetRequiredService<FeatureProtection>();
            var exact = await protection.ExactViewIdsAsync(null, new[] { caveId, placeId });

            // The precondition, stated rather than assumed: for this fixture the protection
            // helper tells an anonymous caller that both rows are exact. Any implementation that
            // leaned on it to decide what to emit would emit them.
            exact.ShouldContain(caveId);
            exact.ShouldContain(placeId);
        }

        await PublishAsync(caveId);

        var response = await anonymous.GetAsync($"/api/v1/public/qr/{qcri}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var envelope = JsonNode.Parse(body)!.AsObject();
        envelope.Select(p => p.Key).ShouldBe(["instanceName"]);
        envelope["instanceName"]!.GetValue<string>().ShouldBe("SilexGIS");

        // Said again over the whole payload rather than field by field, because the field list
        // is what is being trusted and a whole-payload sweep does not trust it.
        body.ShouldNotContain(caveName);
        body.ShouldNotContain(placeName);
        body.ShouldNotContain(caveId.ToString());
        body.ShouldNotContain(placeId.ToString());
        // The property set above is what forbids a name field; this sweep is for a value that
        // located or identified something arriving inside some other field's text.
        foreach (var fragment in new[] { "25.77", "45.37", "coord", "geom", "\"lat", "\"lon" })
        {
            body.ShouldNotContain(fragment, Case.Insensitive);
        }

        // The device that prints these matches either code, and matches without regard to case,
        // so the server does too or a scan resolves on one phone and not on another.
        (await anonymous.GetAsync($"/api/v1/public/qr/{qcri.ToUpperInvariant()}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/v1/public/qr/ro-bh-0001-014"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/v1/public/qr/RO-BH-0001-014"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A code that is not a code, a code nobody issued, a code on a cave nobody published, and a
    /// code on a cave somebody published and withdrew: four different facts and one answer, down
    /// to the bytes and the headers. Telling them apart would be telling a stranger which codes
    /// exist and which caves this installation holds — and the published case is asserted in the
    /// same test, so the four refusals are about those four causes and not about a route that
    /// refuses everyone.
    /// </summary>
    [Fact]
    public async Task Every_reason_a_code_does_not_resolve_answers_identically()
    {
        var publishedCave = await CreateCaveAsync($"Published {Guid.NewGuid():N}"[..20]);
        var liveCode = $"b{Guid.NewGuid():N}"[..9];
        await CreatePlaceAsync(publishedCave, "Live place", qcri: liveCode);
        await PublishAsync(publishedCave);

        var unpublishedCave = await CreateCaveAsync($"Unpublished {Guid.NewGuid():N}"[..20]);
        var unpublishedCode = $"c{Guid.NewGuid():N}"[..9];
        await CreatePlaceAsync(unpublishedCave, "Quiet place", qcri: unpublishedCode);

        var revokedCave = await CreateCaveAsync($"Withdrawn {Guid.NewGuid():N}"[..20]);
        var revokedCode = $"d{Guid.NewGuid():N}"[..9];
        await CreatePlaceAsync(revokedCave, "Withdrawn place", qcri: revokedCode);
        await PublishAsync(revokedCave);

        // The positive, before any of the refusals: this code resolves right now.
        (await anonymous.GetAsync($"/api/v1/public/qr/{liveCode}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/public/qr/{revokedCode}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Withdrawal takes hold on the next scan, with nothing to expire and nothing to purge.
        (await owner.DeleteAsync($"/api/v1/caves/{revokedCave}/qr-publication"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var causes = new[]
        {
            ("malformed", new string('z', 200)),
            ("unknown", $"e{Guid.NewGuid():N}"[..9]),
            ("unpublished", unpublishedCode),
            ("revoked", revokedCode),
        };

        string? first = null;
        string? firstHeaders = null;
        foreach (var (cause, code) in causes)
        {
            var response = await anonymous.GetAsync($"/api/v1/public/qr/{code}");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, cause);

            var text = WithoutTraceId(await response.Content.ReadAsStringAsync());
            JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().ShouldBe("qr.not_found");

            var headers = string.Join(
                '\n',
                response.Headers.Concat(response.Content.Headers)
                    .Where(h => !h.Key.Equals("Date", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(h => h.Key, StringComparer.Ordinal)
                    .Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));

            first ??= text;
            firstHeaders ??= headers;
            text.ShouldBe(first, $"the {cause} answer is distinguishable from the first one");
            headers.ShouldBe(firstHeaders, $"the {cause} answer's headers are distinguishable");
        }

        // And the live one still resolves, so the four refusals above were about their own
        // causes and not about the route having stopped answering.
        (await anonymous.GetAsync($"/api/v1/public/qr/{liveCode}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Nothing forbids two features carrying the same code — two datasets syncing into one
    /// installation can legally land one twice, and no constraint anywhere says otherwise. More
    /// than one match answers as one match, which costs nothing only because the answer names
    /// nothing: whichever was found, the caller is told the same sentence. The day the answer
    /// names what it found, this stops being a simplification and becomes a question that has to
    /// be answered before anything can be shown.
    /// </summary>
    [Fact]
    public async Task A_code_on_two_caves_answers_as_one()
    {
        var shared = $"f{Guid.NewGuid():N}"[..9];

        var firstCave = await CreateCaveAsync($"Twin one {Guid.NewGuid():N}"[..20]);
        await CreatePlaceAsync(firstCave, "Twin place one", qcri: shared);
        var secondCave = await CreateCaveAsync($"Twin two {Guid.NewGuid():N}"[..20]);
        await CreatePlaceAsync(secondCave, "Twin place two", qcri: shared);

        // Neither published: the duplicate is not a way in.
        (await anonymous.GetAsync($"/api/v1/public/qr/{shared}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await PublishAsync(firstCave);
        var oneOfTwo = await anonymous.GetAsync($"/api/v1/public/qr/{shared}");
        oneOfTwo.StatusCode.ShouldBe(HttpStatusCode.OK);
        var oneBody = await oneOfTwo.Content.ReadAsStringAsync();

        await PublishAsync(secondCave);
        var twoOfTwo = await anonymous.GetAsync($"/api/v1/public/qr/{shared}");
        twoOfTwo.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await twoOfTwo.Content.ReadAsStringAsync()).ShouldBe(oneBody);

        // Withdrawing one of the two leaves the code resolving through the other, and
        // withdrawing both stops it — the decision is per cave and both are consulted.
        (await owner.DeleteAsync($"/api/v1/caves/{firstCave}/qr-publication"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"/api/v1/public/qr/{shared}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/caves/{secondCave}/qr-publication"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"/api/v1/public/qr/{shared}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The two indexes the lookup depends on. They stand over an expression inside the property
    /// document, which the object model cannot describe, so they are created by hand in a
    /// migration and nothing in the model refers to them — which is what keeps a later model
    /// change from proposing they be dropped, and equally what would let one disappear without
    /// anything noticing. This is the thing that notices.
    /// </summary>
    [Fact]
    public async Task The_code_lookup_has_its_indexes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        foreach (var name in new[] { "ix_features_speleoloc_qcri_lower", "ix_features_speleoloc_pci_lower" })
        {
            var definition = await connection.ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    "SELECT indexdef FROM pg_indexes WHERE tablename = 'features' AND indexname = @name",
                    new { name }));

            definition.ShouldNotBeNull($"{name} is missing, so every scan of a label reads every feature");
            definition.ShouldContain("lower(");
        }
    }

    /// <summary>A problem body without the per-request trace identifier, which no two share.</summary>
    private static string WithoutTraceId(string problem)
    {
        var body = JsonNode.Parse(problem)!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    private async Task PublishAsync(Guid caveId)
    {
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/qr-publication", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "private",
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePlaceAsync(Guid caveId, string name, string? qcri = null, string? pci = null)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        if (qcri is not null)
        {
            properties["speleolocQcri"] = qcri;
        }

        if (pci is not null)
        {
            properties["speleolocPci"] = pci;
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = cavePlaceTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.7712345, 45.3798765 } },
            visibility = "private",
            parents = new[] { new { parentId = caveId, isPrimary = true } },
            properties,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync()
    {
        owner.Dispose();
        anonymous.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}

/// <summary>
/// The window on the landing route. Its own class with its own application, because the limit
/// has to be tightened enough to trip on purpose and any other test sharing the application
/// would starve behind it.
/// </summary>
public sealed class PublicQrRateLimitTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory =
        new(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Qr:RateLimitPerMinute"] = "6",
        });

    /// <summary>
    /// A script sweeping the code space is turned away — and turned away for scanning, not for
    /// having guessed anything, which is the honest reading of this window. It keeps the load
    /// off the database. It is not what keeps the codes safe; nothing about a printed code is
    /// secret, and the safety is that a code which does resolve discloses nothing.
    /// </summary>
    [Fact]
    public async Task A_burst_of_scans_is_turned_away()
    {
        using var client = factory.CreateClient();

        HttpResponseMessage? refused = null;
        for (var i = 0; i < 20 && refused is null; i++)
        {
            var response = await client.GetAsync($"/api/v1/public/qr/probe{i}");
            refused = response.StatusCode == HttpStatusCode.TooManyRequests ? response : null;
        }

        refused.ShouldNotBeNull("the landing route should stop answering a burst within its window");

        // The body, not just the status. The limiter answers before the route, so this refusal
        // mints no `code` — while still arriving as a problem document. A client that branches on
        // `code`, as every other failure on this surface asks it to, meets a document without one
        // here, and the published catalogue has to say so rather than call the answer empty.
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(429);
        body.GetProperty("title").GetString().ShouldBe("Too Many Requests");
        body.TryGetProperty("code", out _).ShouldBeFalse(
            "the limiter answers before the route and mints no code, and the catalogue says so");
    }

    public void Dispose() => factory.Dispose();
}
