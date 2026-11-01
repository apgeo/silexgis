// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the relations graph must never tell. A link member the viewer cannot read is a
/// placeholder of its own — no title, no id, no way to expand it — and two links naming the
/// same unreadable item do not meet in it; around a location-protected cave the viewer may not
/// place exactly, locating feature links, withheld link members, attachment pairings and
/// protected centerlines are absent rather than restricted — from whichever side the graph is
/// asked, a tag's included. Every negative stands beside the owner seeing the same row.
/// </summary>
public sealed class GraphDisclosureTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private GraphTestKit kit = null!;

    public GraphDisclosureTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync() => kit = await GraphTestKit.CreateAsync(factory, "gxd");

    [Fact]
    public async Task An_unreadable_member_is_a_placeholder_of_its_own_that_cannot_be_expanded()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var seed = await kit.FeatureAsync($"Graf seed {m}");
        var hidden = await kit.FeatureAsync($"Graf unreadable {m}");
        var other = await kit.FeatureAsync($"Graf third {m}");
        await kit.DenyViewerAsync(hidden);
        var first = await kit.ResLinkAsync(seed, hidden);
        var second = await kit.ResLinkAsync(seed, hidden, other);
        var hiddenKey = GraphTestKit.FeatureKey(hidden);

        // The owner reads it: one node, reached from both hubs.
        var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", seed, "resLink");
        GraphTestKit.Node(owners, hiddenKey).GetProperty("title").GetString().ShouldBe($"Graf unreadable {m}");
        GraphTestKit.Edges(owners, "resLink").Count(e => e.GetProperty("target").GetString() == hiddenKey).ShouldBe(2);

        // The viewer: a placeholder per membership, keyed by the member row, saying nothing.
        var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", seed, "resLink");
        GraphTestKit.HasNode(viewers, hiddenKey).ShouldBeFalse();
        var placeholders = viewers.GetProperty("nodes").EnumerateArray()
            .Where(n => n.GetProperty("restricted").GetBoolean()).ToList();
        placeholders.Count.ShouldBe(2);
        placeholders.Select(n => n.GetProperty("key").GetString()).Distinct().Count().ShouldBe(2);
        foreach (var placeholder in placeholders)
        {
            var key = placeholder.GetProperty("key").GetString()!;
            key.ShouldStartWith("restricted:");
            key.ShouldNotContain(hidden.ToString());
            placeholder.GetProperty("type").GetString().ShouldBe("feature");
            placeholder.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
            placeholder.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
            placeholder.GetProperty("subtitle").ValueKind.ShouldBe(JsonValueKind.Null);
            placeholder.GetProperty("route").ValueKind.ShouldBe(JsonValueKind.Null);
            placeholder.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.Null);
            placeholder.GetProperty("expandable").GetBoolean().ShouldBeFalse();

            // Each placeholder hangs off exactly one hub, one per link.
            GraphTestKit.Edges(viewers, "resLink").Where(e => e.GetProperty("target").GetString() == key)
                .Select(e => e.GetProperty("source").GetString()).Single()
                .ShouldBeOneOf($"resLink:{first}", $"resLink:{second}");
        }

        // Expanding one is refused: "restricted" is no type, the member row is no item, and the
        // item itself answers the viewer as a miss.
        var memberId = placeholders[0].GetProperty("key").GetString()!["restricted:".Length..];
        (await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=restricted&id={memberId}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={memberId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={hidden}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The hub counts the placeholder among the members it shows.
        GraphTestKit.Node(viewers, $"resLink:{second}").GetProperty("memberCount").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task A_locating_feature_link_to_a_cave_the_viewer_cannot_place_is_absent()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var guarded = await kit.CaveAsync($"Graf guarded {m}", locationProtected: true);
        var sinkhole = await kit.FeatureAsync($"Graf sinkhole {m}");
        await kit.FeatureLinksAsync(sinkhole, (guarded, "associated_cave"));

        foreach (var seed in new[] { sinkhole, guarded })
        {
            var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", seed, "featureLink");
            GraphTestKit.Edges(owners, "featureLink").Count().ShouldBe(1);

            var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", seed, "featureLink");
            GraphTestKit.Edges(viewers, "featureLink").ShouldBeEmpty();
            GraphTestKit.Count(viewers, "featureLink").GetProperty("total").GetInt32().ShouldBe(0);
            viewers.GetProperty("nodes").GetArrayLength().ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_link_member_naming_a_cave_the_viewer_cannot_place_is_dropped_not_restricted()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var guarded = await kit.CaveAsync($"Graf guarded member {m}", locationProtected: true);
        var plain = await kit.FeatureAsync($"Graf plain member {m}");
        var link = await kit.ResLinkAsync(plain, guarded);
        var guardedKey = GraphTestKit.FeatureKey(guarded);

        var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", plain, "resLink");
        GraphTestKit.HasNode(owners, guardedKey).ShouldBeTrue();

        // From the plain side: the hub stands, with the plain member alone — no placeholder,
        // since even a bare row would say that this link names something more.
        var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", plain, "resLink");
        GraphTestKit.HasNode(viewers, guardedKey).ShouldBeFalse();
        viewers.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("restricted").GetBoolean()).ShouldBeFalse();
        GraphTestKit.Edges(viewers, "resLink").Select(e => e.GetProperty("target").GetString())
            .ShouldBe([GraphTestKit.FeatureKey(plain)]);
        GraphTestKit.Node(viewers, $"resLink:{link}").GetProperty("memberCount").GetInt32().ShouldBe(1);

        // From the cave's side: the panel is empty and its total is zero, and so is the graph's.
        var fromCave = await GraphTestKit.GraphAsync(kit.Viewer, "feature", guarded, "resLink");
        GraphTestKit.Edges(fromCave, "resLink").ShouldBeEmpty();
        GraphTestKit.Count(fromCave, "resLink").GetProperty("total").GetInt32().ShouldBe(0);
        var ownersFromCave = await GraphTestKit.GraphAsync(kit.Owner, "feature", guarded, "resLink");
        GraphTestKit.Count(ownersFromCave, "resLink").GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_protected_caves_centerline_is_absent_for_a_viewer_who_cannot_place_it()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var guarded = await kit.CaveAsync($"Graf guarded line {m}", locationProtected: true);
        var line = await kit.CenterlineAsync(guarded, $"graf-guarded-line-{m}.gpx");
        var entrance = await kit.EntranceAsync(guarded, $"Graf guarded entry {m}");
        var lineKey = GraphTestKit.FeatureKey(line);

        var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", guarded, "hierarchy");
        GraphTestKit.HasNode(owners, lineKey).ShouldBeTrue();
        GraphTestKit.Count(owners, "hierarchy").GetProperty("total").GetInt32().ShouldBe(2);

        var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", guarded, "hierarchy");
        GraphTestKit.HasNode(viewers, lineKey).ShouldBeFalse();
        GraphTestKit.Edges(viewers, "hierarchy").Any(e => e.GetProperty("relation").GetString() == "centerline")
            .ShouldBeFalse();
        // Counted out too, while the entrance (no position on this route) is still told.
        GraphTestKit.Count(viewers, "hierarchy").GetProperty("total").GetInt32().ShouldBe(1);
        GraphTestKit.HasNode(viewers, GraphTestKit.FeatureKey(entrance)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_protected_centerline_is_never_named_from_a_tag_nor_opened_as_a_seed()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var guarded = await kit.CaveAsync($"Graf guarded tagged {m}", locationProtected: true);
        var line = await kit.CenterlineAsync(guarded, $"graf-tagged-line-{m}.gpx");
        var open = await kit.FeatureAsync($"Graf tagged open {m}");
        var tagName = $"graf-x-{m}";
        foreach (var item in new[] { line, open })
        {
            var tagged = await kit.Owner.PostAsJsonAsync("/api/v1/taggings", new { tagName, entityType = "feature", entityId = item });
            tagged.IsSuccessStatusCode.ShouldBeTrue(await tagged.Content.ReadAsStringAsync());
        }

        long tagId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            tagId = await db.Tags.Where(t => t.Name == tagName).Select(t => t.Id).SingleAsync();
        }

        var lineKey = GraphTestKit.FeatureKey(line);

        // The owner places the cave exactly: the centerline is one of the tag's two items.
        var owners = await GraphTestKit.GraphAsync(kit.Owner, $"type=tag&id={tagId}&categories=tag");
        GraphTestKit.HasNode(owners, lineKey).ShouldBeTrue();
        GraphTestKit.Count(owners, "tag").GetProperty("total").GetInt32().ShouldBe(2);
        (await kit.Owner.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={line}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // The viewer's own address for the centerline answers not-found, and so does the graph:
        // not drawn under the tag, not counted, not open as a seed.
        (await kit.Viewer.GetAsync($"/api/v1/features/{line}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var viewers = await GraphTestKit.GraphAsync(kit.Viewer, $"type=tag&id={tagId}&categories=tag");
        GraphTestKit.HasNode(viewers, lineKey).ShouldBeFalse();
        GraphTestKit.Neighbours(viewers, "tag", $"tag:{tagId}").ShouldBe([GraphTestKit.FeatureKey(open)]);
        GraphTestKit.Count(viewers, "tag").GetProperty("total").GetInt32().ShouldBe(1);
        (await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={line}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_geotagged_attachment_on_a_cave_the_viewer_cannot_place_is_paired_from_neither_side()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var guarded = await kit.CaveAsync($"Graf guarded photo {m}", locationProtected: true);
        var open = await kit.FeatureAsync($"Graf photo open {m}");
        var photoFile = await kit.UploadAsync($"graf-photo-{m}.jpg", GraphTestKit.GeotaggedJpeg(45.53127, 25.44721), "image/jpeg");
        var noteFile = await kit.UploadAsync($"graf-note-{m}.txt", System.Text.Encoding.UTF8.GetBytes($"note {m}"), "text/plain");
        await kit.AttachToFeatureAsync(photoFile, guarded);
        await kit.AttachToFeatureAsync(photoFile, open);
        await kit.AttachToFeatureAsync(noteFile, guarded);
        var photo = kit.DocumentOfFile(photoFile);
        var note = kit.DocumentOfFile(noteFile);
        var guardedKey = GraphTestKit.FeatureKey(guarded);
        var photoKey = $"document:{photo}";

        // Fixture proof: the upload read a capture point, so the photo carve-out is exercised.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == photoFile)).Geom.ShouldNotBeNull();
        }

        try
        {
            foreach (var reveal in new[] { false, true })
            {
                await SetRevealAsync(reveal);

                // The owner may place the cave: both pairings, from the cave and from the photo.
                var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", guarded, "attachment");
                GraphTestKit.Neighbours(owners, "attachment", guardedKey).ShouldBe([photoKey, $"document:{note}"], ignoreOrder: true);
                var ownersFromPhoto = await GraphTestKit.GraphAsync(kit.Owner, "document", photo, "attachment");
                GraphTestKit.Neighbours(ownersFromPhoto, "attachment", photoKey)
                    .ShouldBe([guardedKey, GraphTestKit.FeatureKey(open)], ignoreOrder: true);

                // The viewer: the cave's attachment list is the graph's, and neither carries the
                // photo — whether the installation reveals protected associations or not, since a
                // positioned picture beside the cave's name is the cave's position.
                var listed = (await GraphTestKit.BodyAsync(kit.Viewer, $"/api/v1/attachments?entityType=feature&entityId={guarded}"))
                    .EnumerateArray().Select(a => $"document:{kit.DocumentOfFile(a.GetProperty("fileId").GetGuid())}").ToHashSet();
                var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", guarded, "attachment");
                var neighbours = GraphTestKit.Neighbours(viewers, "attachment", guardedKey);
                neighbours.ShouldBe(listed, ignoreOrder: true);
                neighbours.ShouldNotContain(photoKey);
                GraphTestKit.Count(viewers, "attachment").GetProperty("total").GetInt32().ShouldBe(listed.Count);
                listed.Count.ShouldBe(reveal ? 1 : 0);

                // From the photo: the open feature's pairing stands, the cave's is not told.
                var fromPhoto = await GraphTestKit.GraphAsync(kit.Viewer, "document", photo, "attachment");
                GraphTestKit.Neighbours(fromPhoto, "attachment", photoKey).ShouldBe([GraphTestKit.FeatureKey(open)]);
                GraphTestKit.HasNode(fromPhoto, guardedKey).ShouldBeFalse();
                GraphTestKit.Count(fromPhoto, "attachment").GetProperty("total").GetInt32().ShouldBe(1);
            }
        }
        finally
        {
            await SetRevealAsync(false);
        }
    }

    [Fact]
    public async Task Entrances_are_counted_as_they_are_drawn_for_a_cave_read_through_a_grant()
    {
        var m = Guid.NewGuid().ToString("N")[..8];
        var cave = await kit.CaveAsync($"Graf granted cave {m}", visibility: "private");
        var entrance = await kit.EntranceAsync(cave, $"Graf granted entry {m}");
        await kit.GrantViewerAsync(cave);

        var owners = await GraphTestKit.GraphAsync(kit.Owner, "feature", cave, "hierarchy");
        GraphTestKit.HasNode(owners, GraphTestKit.FeatureKey(entrance)).ShouldBeTrue();

        // Whatever the viewer is shown of the entrance, the total is exactly what paging on
        // would show — never an item the drawing then leaves out — and nothing beyond the
        // cave's own entrance list.
        var listed = (await GraphTestKit.BodyAsync(kit.Viewer, $"/api/v1/caves/{cave}/entrances")).EnumerateArray()
            .Select(e => GraphTestKit.FeatureKey(e.GetProperty("id").GetGuid())).ToHashSet();
        var viewers = await GraphTestKit.GraphAsync(kit.Viewer, "feature", cave, "hierarchy");
        var drawn = GraphTestKit.Edges(viewers, "hierarchy").Where(e => e.GetProperty("relation").GetString() == "entrance")
            .Select(e => e.GetProperty("target").GetString()!).ToHashSet();
        drawn.IsSubsetOf(listed).ShouldBeTrue();
        GraphTestKit.Count(viewers, "hierarchy").GetProperty("total").GetInt32()
            .ShouldBe(GraphTestKit.Edges(viewers, "hierarchy").Count());
        foreach (var key in drawn)
        {
            (await kit.Viewer.GetAsync($"/api/v1/graph/neighbourhood?type=feature&id={key["feature:".Length..]}"))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private async Task SetRevealAsync(bool reveal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        await settings.SaveAsync(AppSettingSections.Protection, new ProtectionSettings { RevealProtectedAssociations = reveal });
    }

    public async ValueTask DisposeAsync()
    {
        await kit.DisposeAsync();
        Dispose();
    }

    public void Dispose() => factory.Dispose();
}
