// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Feature share links: minting (one-time token), listing and revocation under the Share
/// permission; anonymous resolution of public shares with location protection applied
/// absolutely (a protected chain never yields exact geometry, whatever the share); and
/// the requires-login mode, where the caller's own permissions gate the read.
/// </summary>
public sealed class FeatureShareTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Viewer (regular user), unrelated — Editors read everything now
    private Guid outsiderId;
    private long sinkholeTypeId;
    private long caveTypeId;
    private long entranceTypeId;

    public FeatureShareTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fsown-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fsout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"fsown-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"fsout-{suffix}@t.local");
    }

    [Fact]
    public async Task Mint_list_and_revoke_are_gated_by_the_share_permission()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var featureId = await CreateGenericAsync($"Shared feature {marker}", "authenticated", 25.81, 45.41);
        var privateId = await CreateGenericAsync($"Private feature {marker}", "private", 25.82, 45.42);

        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.PostAsJsonAsync($"/api/v1/features/{featureId}/shares", new { mode = "public" }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Readable-but-not-shareable is 403; an unreadable feature is an undisclosing 404.
        (await outsider.PostAsJsonAsync($"/api/v1/features/{featureId}/shares", new { mode = "public" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PostAsJsonAsync($"/api/v1/features/{privateId}/shares", new { mode = "public" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Mint: the plaintext token appears exactly once, in the mint response.
        var minted = await MintAsync(featureId, "public");
        var shareId = minted["id"]!.GetValue<Guid>();
        var token = minted["token"]!.GetValue<string>();
        token.ShouldNotBeNullOrEmpty();
        minted["mode"]!.GetValue<string>().ShouldBe("public");

        // The management list carries metadata only — never a token.
        var listResponse = await owner.GetAsync($"/api/v1/features/{featureId}/shares");
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listRaw = await listResponse.Content.ReadAsStringAsync();
        listRaw.ShouldContain(shareId.ToString());
        listRaw.ShouldNotContain(token);

        (await outsider.GetAsync($"/api/v1/features/{featureId}/shares"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/v1/features/{featureId}/shares/{shareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Anonymous resolution of a public share of an unprotected feature: exact view.
        using var reader = factory.CreateClient();
        var shared = (await reader.GetFromJsonAsync<JsonObject>($"/api/v1/shared/features/{token}"))!;
        shared["kind"]!.GetValue<string>().ShouldBe("generic");
        shared["feature"]!["id"]!.GetValue<Guid>().ShouldBe(featureId);
        shared["feature"]!["approximateLocation"]!.GetValue<bool>().ShouldBeFalse();
        shared["feature"]!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(25.81);

        // Malformed, unknown and oversized tokens answer identically.
        foreach (var bad in new[] { "definitely-not-a-token", new string('x', 150) })
        {
            var miss = await reader.GetAsync($"/api/v1/shared/features/{bad}");
            miss.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await miss.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
                .ShouldBe("share.not_found");
        }

        // Revocation kills the link and is idempotent.
        (await owner.DeleteAsync($"/api/v1/features/{featureId}/shares/{shareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetAsync($"/api/v1/shared/features/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/features/{featureId}/shares/{shareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Public_shares_never_reveal_protected_exact_locations()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var lon = 25.4611;
        var lat = 45.5322;

        // A private, location-protected cave with an entrance at exact coordinates
        // (altitude in Z) and a centerline — the full protected chain.
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Shared Protected Cave {marker}",
            caveTypeId,
            visibility = "private",
            locationProtected = true,
            explorationStatus = "unknown",
            isShowCave = false,
            closestAddress = "Strada Izvorului 7",
            landRegistryNumber = "CF-12345",
            locationNotes = "Behind the third boulder",
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await caveResponse.Content.ReadAsStringAsync());
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var entranceResponse = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = $"Hidden entrance {marker}",
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat, 431.0 } },
            positionQuality = "gps",
        });
        entranceResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await entranceResponse.Content.ReadAsStringAsync());
        var entranceId = (await entranceResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        Guid centerlineId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            var geometries = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
            var path = geometries.CreateLineString(
                [new Coordinate(lon, lat), new Coordinate(lon + 0.002, lat + 0.001)]);
            var feature = new Feature
            {
                Name = $"Survey {marker}",
                Geom = geometries.CreateMultiLineString([path]),
            };
            var centerline = new Centerline { CaveFeatureId = caveId, PathCount = 1, Source = CenterlineSource.Uploaded };
            await writer.CreateCenterlineAsync(feature, centerline);
            await db.SaveChangesAsync();
            centerlineId = feature.Id;
        }

        using var reader = factory.CreateClient();

        // A public share of a PRIVATE feature resolves for anyone holding the link — the
        // share grants Read of the envelope — but protection is absolute: the location is
        // obfuscated for the anonymous viewer no matter what the minter may see.
        var caveToken = (await MintAsync(caveId, "public"))["token"]!.GetValue<string>();
        var caveShared = await reader.GetAsync($"/api/v1/shared/features/{caveToken}");
        caveShared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var caveEnvelope = JsonNode.Parse(await caveShared.Content.ReadAsStringAsync())!.AsObject();

        caveEnvelope["kind"]!.GetValue<string>().ShouldBe("cave");
        caveEnvelope["feature"]!["approximateLocation"]!.GetValue<bool>().ShouldBeTrue();
        var caveCoordinates = caveEnvelope["feature"]!["geometry"]!["coordinates"]!.AsArray();
        caveCoordinates.Count.ShouldBe(2); // snap drops the altitude
        caveCoordinates[0]!.GetValue<double>().ShouldNotBe(lon);
        caveCoordinates[1]!.GetValue<double>().ShouldNotBe(lat);
        // The precise-location text trio is redacted alongside the coordinates.
        caveEnvelope["cave"]!["closestAddress"].ShouldBeNull();
        caveEnvelope["cave"]!["landRegistryNumber"].ShouldBeNull();
        caveEnvelope["cave"]!["locationNotes"].ShouldBeNull();
        // Nowhere in the whole payload do the exact coordinates appear.
        var caveScrubbed = WithoutTimestamps(caveEnvelope);
        caveScrubbed.ShouldNotContain("25.4611");
        caveScrubbed.ShouldNotContain("45.5322");

        // Subtree children are identity-only rows; the protected centerline is withheld
        // even from that listing — its existence is not disclosed.
        var children = caveEnvelope["children"]!.AsArray();
        var childRow = children.Single()!.AsObject();
        childRow["id"]!.GetValue<Guid>().ShouldBe(entranceId);
        childRow["kind"]!.GetValue<string>().ShouldBe("caveEntrance");
        childRow.ContainsKey("geometry").ShouldBeFalse();
        children.Select(c => c!["id"]!.GetValue<Guid>()).ShouldNotContain(centerlineId);

        // Sharing the entrance itself: snapped point, no altitude, no position quality.
        var entranceToken = (await MintAsync(entranceId, "public"))["token"]!.GetValue<string>();
        var entranceShared = await reader.GetAsync($"/api/v1/shared/features/{entranceToken}");
        entranceShared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entranceEnvelope = JsonNode.Parse(await entranceShared.Content.ReadAsStringAsync())!.AsObject();
        entranceEnvelope["kind"]!.GetValue<string>().ShouldBe("caveEntrance");
        entranceEnvelope["feature"]!["approximateLocation"]!.GetValue<bool>().ShouldBeTrue();
        entranceEnvelope["feature"]!["geometry"]!["coordinates"]!.AsArray().Count.ShouldBe(2);
        entranceEnvelope["entrance"]!["altitude"].ShouldBeNull();
        entranceEnvelope["entrance"]!["positionQuality"].ShouldBeNull();
        var entranceScrubbed = WithoutTimestamps(entranceEnvelope);
        entranceScrubbed.ShouldNotContain("25.4611");
        entranceScrubbed.ShouldNotContain("45.5322");

        // A protected centerline cannot be served obfuscated — any part of it is exact
        // location data. Even a minted share answers as if it did not exist.
        var centerlineToken = (await MintAsync(centerlineId, "public"))["token"]!.GetValue<string>();
        var centerlineShared = await reader.GetAsync($"/api/v1/shared/features/{centerlineToken}");
        centerlineShared.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await centerlineShared.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("share.not_found");

        // Without the subtree flag the envelope carries no children at all.
        var bareToken = (await MintAsync(caveId, "public", includeSubtree: false))["token"]!.GetValue<string>();
        (await reader.GetFromJsonAsync<JsonObject>($"/api/v1/shared/features/{bareToken}"))!
            ["children"].ShouldBeNull();

        // The share token never widens the regular API: the feature routes still 401.
        (await reader.GetAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Requires_login_shares_enforce_the_callers_own_permissions()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Login Share Cave {marker}",
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await caveResponse.Content.ReadAsStringAsync());
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var token = (await MintAsync(caveId, "requiresLogin"))["token"]!.GetValue<string>();

        // Anonymous callers are told to sign in — the one deliberate difference from 404.
        using (var anonymous = factory.CreateClient())
        {
            var challenge = await anonymous.GetAsync($"/api/v1/shared/features/{token}");
            challenge.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await challenge.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
                .ShouldBe("share.login_required");
        }

        // Signed in, the caller's own permissions decide: the link is only an address.
        (await outsider.GetAsync($"/api/v1/shared/features/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var ownerView = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/shared/features/{token}"))!;
        ownerView["kind"]!.GetValue<string>().ShouldBe("cave");
        ownerView["feature"]!["id"]!.GetValue<Guid>().ShouldBe(caveId);

        // A deleted feature takes its share links with it.
        var publicToken = (await MintAsync(caveId, "public"))["token"]!.GetValue<string>();
        using var reader = factory.CreateClient();
        (await reader.GetAsync($"/api/v1/shared/features/{publicToken}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetAsync($"/api/v1/shared/features/{publicToken}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A share link is revoked through its own feature and no other: the right to share one
    /// feature is not a right over another feature's links. A revoke repeated later keeps the
    /// moment of the first.
    /// </summary>
    [Fact]
    public async Task A_link_is_revoked_only_through_its_own_feature_and_keeps_the_first_revocation()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var mine = await CreateGenericAsync($"Revoking feature {marker}", "authenticated", 25.83, 45.43);
        var other = await CreateGenericAsync($"Linked feature {marker}", "authenticated", 25.84, 45.44);
        var minted = await MintAsync(other, "public");
        var shareId = minted["id"]!.GetValue<Guid>();
        var token = minted["token"]!.GetValue<string>();

        (await owner.DeleteAsync($"/api/v1/features/{mine}/shares/{shareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var reader = factory.CreateClient();
        (await reader.GetAsync($"/api/v1/shared/features/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"/api/v1/features/{other}/shares/{shareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetAsync($"/api/v1/shared/features/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var firstRevokedAt = await RevokedAtAsync(other, shareId);
        firstRevokedAt.ShouldNotBeNull();

        await Task.Delay(20);
        (await owner.DeleteAsync($"/api/v1/features/{other}/shares/{shareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RevokedAtAsync(other, shareId)).ShouldBe(firstRevokedAt);
    }

    [Theory]
    [InlineData("\"nonsense\"")]
    [InlineData("99")]
    public async Task A_mode_that_is_not_one_of_the_two_is_refused(string mode)
    {
        var featureId = await CreateGenericAsync($"Moded feature {Guid.NewGuid():N}"[..30], "authenticated", 25.85, 45.45);
        using var body = new StringContent(
            $"{{\"mode\":{mode},\"includeSubtree\":false}}", System.Text.Encoding.UTF8, "application/json");
        var response = await owner.PostAsync($"/api/v1/features/{featureId}/shares", body);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureId}/shares"))!.Count.ShouldBe(0);
    }

    /// <summary>
    /// Under a sign-in link with its subtree, the subtree is the signed-in caller's own: a child
    /// they may not read is left out, and so is everything beneath it, though the owner of the
    /// link is shown all of it.
    /// </summary>
    [Fact]
    public async Task A_sign_in_links_subtree_leaves_out_what_the_caller_may_not_read_and_what_is_under_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var root = await CreateAreaAsync($"Shared area {marker}");
        var open = await CreateChildAsync($"Open child {marker}", root);
        var closed = await CreateChildAsync($"Closed child {marker}", root);
        var under = await CreateChildAsync($"Under closed {marker}", closed);
        await DenyReadAsync(outsiderId, closed);

        var token = (await MintAsync(root, "requiresLogin"))["token"]!.GetValue<string>();

        var outsiders = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/shared/features/{token}"))!;
        outsiders["children"]!.AsArray().Select(c => c!["id"]!.GetValue<Guid>()).ShouldBe([open]);
        var owners = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/shared/features/{token}"))!;
        owners["children"]!.AsArray().Select(c => c!["id"]!.GetValue<Guid>())
            .ShouldBe([open, closed, under], ignoreOrder: true);
    }

    /// <summary>
    /// A token longer than any the server mints is refused before it is looked up, even when a
    /// stored link would match it; one at the limit is looked up as usual.
    /// </summary>
    [Fact]
    public async Task A_token_longer_than_the_limit_is_refused_before_it_is_looked_up()
    {
        var featureId = await CreateGenericAsync($"Long token feature {Guid.NewGuid():N}"[..40], "authenticated", 25.86, 45.46);
        var tooLong = new string('a', 101);
        var atLimit = new string('b', 100);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var creator = await db.Features.Where(f => f.Id == featureId).Select(f => f.OwnerUserId).SingleAsync();
            foreach (var token in new[] { tooLong, atLimit })
            {
                db.FeatureShares.Add(new FeatureShare
                {
                    FeatureId = featureId,
                    TokenHash = HashOf(token),
                    Mode = FeatureShareMode.Public,
                    IncludeSubtree = false,
                    CreatedBy = creator,
                });
            }

            await db.SaveChangesAsync();
        }

        using var reader = factory.CreateClient();
        (await reader.GetAsync($"/api/v1/shared/features/{tooLong}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/shared/features/{atLimit}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// What a restore does to a deleted feature's links, pinned as it stands: the link was never
    /// revoked, only unanswerable while its feature was deleted, so it answers again once the
    /// feature is back.
    /// </summary>
    [Fact]
    public async Task A_restored_features_link_answers_again()
    {
        var featureId = await CreateGenericAsync($"Restored shared {Guid.NewGuid():N}"[..40], "authenticated", 25.87, 45.47);
        var token = (await MintAsync(featureId, "public"))["token"]!.GetValue<string>();
        using var reader = factory.CreateClient();

        (await owner.DeleteAsync($"/api/v1/features/{featureId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetAsync($"/api/v1/shared/features/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.PostAsync($"/api/v1/features/{featureId}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/shared/features/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static string HashOf(string token) =>
        System.Buffers.Text.Base64Url.EncodeToString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private async Task<DateTimeOffset?> RevokedAtAsync(Guid featureId, Guid shareId)
    {
        var listed = (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{featureId}/shares"))!;
        var row = listed.Single(s => s!["id"]!.GetValue<Guid>() == shareId)!;
        return row["revokedAt"]?.GetValue<DateTimeOffset>();
    }

    private async Task<Guid> CreateAreaAsync(string name)
    {
        long areaTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            areaTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = areaTypeId,
            visibility = "authenticated",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private async Task<Guid> CreateChildAsync(string name, Guid parentId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.88, 45.48 } },
            visibility = "authenticated",
            parents = new[] { new { parentId, isPrimary = true } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private async Task DenyReadAsync(Guid subjectId, Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = SilexGis.Domain.Access.AccessSubjectKind.User,
            SubjectId = subjectId,
            Effect = SilexGis.Domain.Access.AccessEffect.Deny,
            Domain = SilexGis.Domain.Access.AccessDomain.Features,
            Actions = SilexGis.Domain.Access.AccessAction.Read,
            ScopeKind = SilexGis.Domain.Access.AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The envelope re-serialized without its timestamp fields, for whole-payload
    /// "exact coordinates appear nowhere" checks: a timestamp's fractional seconds
    /// (":45.5322809Z") could otherwise collide with a coordinate substring by chance.
    /// </summary>
    private static string WithoutTimestamps(JsonObject envelope)
    {
        var clone = envelope.DeepClone().AsObject();
        var feature = clone["feature"]!.AsObject();
        feature.Remove("createdAt");
        feature.Remove("updatedAt");
        return clone.ToJsonString();
    }

    private async Task<Guid> CreateGenericAsync(string name, string visibility, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = sinkholeTypeId,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private async Task<JsonObject> MintAsync(Guid featureId, string mode, bool includeSubtree = true)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/features/{featureId}/shares", new { mode, includeSubtree });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public async ValueTask DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        await Task.CompletedTask;
        Dispose();
    }

    public void Dispose() => factory.Dispose();
}
