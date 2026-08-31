// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Publishing a cave's printed codes: the decision is gated on the right to share the cave, a
/// cave the caller may not read is not disclosed by the question, and the decision is a record
/// that can be taken back without being erased.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CaveQrPublicationTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Viewer — reads what everyone signed in reads, shares nothing
    private Guid ownerUserId;
    private long caveTypeId;
    private long entranceTypeId;

    public CaveQrPublicationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"qrpub-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"qrout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"qrpub-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"qrout-{suffix}@t.local");
    }

    [Fact]
    public async Task Publication_is_gated_by_the_share_permission_and_hides_a_cave_it_refuses()
    {
        var readableId = await CreateCaveAsync("authenticated");
        var unreadableId = await CreateCaveAsync("private");
        var absentId = Guid.NewGuid();

        using (var anonymous = factory.CreateClient())
        {
            var url = $"/api/v1/caves/{readableId}/qr-publication";
            (await anonymous.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.PostAsync(url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.DeleteAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The positive case, in the same test as the refusals: the account that may share the
        // cave gets a real answer, so the refusals below are about the caller and not about the
        // route being broken for everyone.
        var mine = await ReadAsync(owner, readableId);
        mine["published"]!.GetValue<bool>().ShouldBeFalse();
        mine["publicationId"].ShouldBeNull();
        mine["revokedAt"].ShouldBeNull();

        // A Viewer reads an installation-wide cave but may not share it, so the refusal says so
        // — it is a decision about this caller that this caller could ask somebody to change.
        foreach (var (verb, response) in await EveryVerbAsync(outsider, readableId))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, verb);
        }

        // The same Viewer against a cave nobody granted them anything on: no grant, no
        // membership, no visibility band that reaches it. That cave answers exactly as a cave
        // that does not exist, down to the problem body — otherwise the question "may I publish
        // this?" would be a way of asking which caves an installation holds.
        var unreadable = await EveryVerbAsync(outsider, unreadableId);
        var absent = await EveryVerbAsync(outsider, absentId);
        for (var i = 0; i < unreadable.Count; i++)
        {
            unreadable[i].Response.StatusCode.ShouldBe(HttpStatusCode.NotFound, unreadable[i].Verb);
            absent[i].Response.StatusCode.ShouldBe(HttpStatusCode.NotFound, absent[i].Verb);
            var hidden = WithoutTraceId(await unreadable[i].Response.Content.ReadAsStringAsync());
            var missing = WithoutTraceId(await absent[i].Response.Content.ReadAsStringAsync());
            hidden.ShouldBe(missing, $"{unreadable[i].Verb} distinguishes a hidden cave from an absent one");
            JsonDocument.Parse(hidden).RootElement.GetProperty("code").GetString()
                .ShouldBe("cave.not_found");
        }

        // Publication is per cave. An entrance is a feature with an id like any other, and it is
        // not one of these — it is reached through the cave that contains it.
        var entranceId = await CreateEntranceAsync(readableId);
        foreach (var (verb, response) in await EveryVerbAsync(owner, entranceId))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, verb);
        }
    }

    [Fact]
    public async Task Publishing_is_idempotent_revocable_and_keeps_the_first_withdrawal()
    {
        var caveId = await CreateCaveAsync("authenticated");

        var published = await PublishAsync(caveId);
        published["published"]!.GetValue<bool>().ShouldBeTrue();
        published["publishedBy"]!.GetValue<Guid>().ShouldBe(ownerUserId);
        published["revokedAt"].ShouldBeNull();
        var publicationId = published["publicationId"]!.GetValue<Guid>();

        // Read back rather than carried over from the write: the response to a write reports the
        // timestamp the interceptor stamped in memory, which carries finer precision than the
        // column it lands in, so the two disagree in their last digits for reasons that have
        // nothing to do with what is being asserted here.
        var stored = await ReadAsync(owner, caveId);
        stored["published"]!.GetValue<bool>().ShouldBeTrue();
        var publishedAt = stored["publishedAt"]!.GetValue<DateTimeOffset>();

        // Pressing the button twice is not two decisions. The standing record answers, keeping
        // both the moment it was taken and the person who took it.
        var again = await PublishAsync(caveId);
        again["publicationId"]!.GetValue<Guid>().ShouldBe(publicationId);
        again["publishedAt"]!.GetValue<DateTimeOffset>().ShouldBe(publishedAt);
        again["publishedBy"]!.GetValue<Guid>().ShouldBe(ownerUserId);
        (await CountAsync(caveId)).ShouldBe(1);

        (await owner.DeleteAsync(Url(caveId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var revoked = await ReadAsync(owner, caveId);
        revoked["published"]!.GetValue<bool>().ShouldBeFalse();
        revoked["publicationId"]!.GetValue<Guid>().ShouldBe(publicationId);
        var revokedAt = revoked["revokedAt"]!.GetValue<DateTimeOffset>();

        // Withdrawing again, and withdrawing a cave nobody published, are both the state already
        // asked for: they answer the same way and move nothing. The moment the codes stopped
        // resolving is a fact about the world, not a count of how often it was asked for.
        (await owner.DeleteAsync(Url(caveId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync(owner, caveId))["revokedAt"]!.GetValue<DateTimeOffset>().ShouldBe(revokedAt);
        var neverPublished = await CreateCaveAsync("authenticated");
        (await owner.DeleteAsync(Url(neverPublished))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync(owner, neverPublished))["published"]!.GetValue<bool>().ShouldBeFalse();
        (await CountAsync(neverPublished)).ShouldBe(0);

        // Publishing after a withdrawal is a new decision, not an undoing of the old one: a new
        // record, with the withdrawn one still standing beside it as what happened.
        var republished = await PublishAsync(caveId);
        republished["published"]!.GetValue<bool>().ShouldBeTrue();
        republished["publicationId"]!.GetValue<Guid>().ShouldNotBe(publicationId);
        republished["revokedAt"].ShouldBeNull();
        (await CountAsync(caveId)).ShouldBe(2);
    }

    /// <summary>
    /// One standing decision per cave, and the database is what holds it. The read path asks
    /// "is there a live row for this cave" and would have no answer if two could stand at once,
    /// so the rule is proved by trying to break it directly rather than by reading back the
    /// handler that is careful not to.
    /// </summary>
    [Fact]
    public async Task A_cave_cannot_hold_two_standing_decisions_at_once()
    {
        var caveId = await CreateCaveAsync("authenticated");
        await PublishAsync(caveId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.CaveQrPublications.Add(new CaveQrPublication { FeatureId = caveId, PublishedBy = ownerUserId });
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }

        // Withdrawn decisions sit outside the rule, because they are the history: a cave
        // published, withdrawn and published again is two rows, one of them standing.
        (await owner.DeleteAsync(Url(caveId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await PublishAsync(caveId);
        (await CountAsync(caveId)).ShouldBe(2);
    }

    private static string Url(Guid caveId) => $"/api/v1/caves/{caveId}/qr-publication";

    /// <summary>
    /// A problem body without the per-request trace identifier. Every problem response carries
    /// one and no two of them share it, so two answers that are identical in everything a
    /// caller could learn from still differ as text — and comparing the text is the only way to
    /// prove they are identical in everything else.
    /// </summary>
    private static string WithoutTraceId(string problem)
    {
        var body = JsonNode.Parse(problem)!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    private async Task<JsonObject> ReadAsync(HttpClient client, Guid caveId)
    {
        var response = await client.GetAsync(Url(caveId));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsObject();
    }

    private async Task<JsonObject> PublishAsync(Guid caveId)
    {
        var response = await owner.PostAsync(Url(caveId), null);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsObject();
    }

    /// <summary>All three verbs, so a refusal is proved for the whole resource and not one route of it.</summary>
    private static async Task<List<(string Verb, HttpResponseMessage Response)>> EveryVerbAsync(
        HttpClient client, Guid caveId) =>
    [
        ("GET", await client.GetAsync(Url(caveId))),
        ("POST", await client.PostAsync(Url(caveId), null)),
        ("DELETE", await client.DeleteAsync(Url(caveId))),
    ];

    private async Task<int> CountAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CaveQrPublications.CountAsync(p => p.FeatureId == caveId);
    }

    private async Task<Guid> CreateCaveAsync(string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"QR publication {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateEntranceAsync(Guid caveId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.77, 45.37 } },
            positionQuality = "Gps",
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
