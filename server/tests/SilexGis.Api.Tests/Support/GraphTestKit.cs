// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// What the relations-graph test classes share: four accounts and small builders for the
/// items and relations they read. The accounts are an administrator, an editor that owns
/// every fixture, a plain viewer, and a second plain viewer who is a member of the kit's
/// caving group. Editors read past visibility by design, so whatever a test means to keep out
/// of sight is kept from the viewer, by a private row, a row shared with the group only or an
/// explicit deny entry, and the same test shows the owner seeing it. The member is the seat
/// that reaches rows through caving-group membership, which neither the administrator nor the
/// editor exercises: a row shared with the group reaches the member and not the viewer.
/// </summary>
public sealed class GraphTestKit : IAsyncDisposable
{
    private readonly SilexGisApiFactory factory;

    private GraphTestKit(SilexGisApiFactory factory) => this.factory = factory;

    public HttpClient Admin { get; private set; } = null!;
    public HttpClient Owner { get; private set; } = null!;
    public HttpClient Viewer { get; private set; } = null!;
    public HttpClient Member { get; private set; } = null!;
    public Guid ViewerId { get; private set; }

    /// <summary>The caving group the member belongs to and the viewer does not.</summary>
    public Guid GroupId { get; private set; }
    public long CaveTypeId { get; private set; }
    public long GenericTypeId { get; private set; }
    public long EntranceTypeId { get; private set; }

    public static async Task<GraphTestKit> CreateAsync(SilexGisApiFactory factory, string prefix)
    {
        var kit = new GraphTestKit(factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"{prefix}-adm-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"{prefix}-own-{suffix}@t.local");
        kit.ViewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"{prefix}-view-{suffix}@t.local");
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"{prefix}-mem-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Clubul Graf Test {suffix}", Slug = $"clubul-graf-test-{suffix}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            kit.GroupId = club.Id;
            await RosterHelper.AddMemberAsync(db, club.Id, memberId);

            kit.CaveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            kit.GenericTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
            kit.EntranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        kit.Admin = await AuthHelper.BearerClientAsync(factory, $"{prefix}-adm-{suffix}@t.local");
        kit.Owner = await AuthHelper.BearerClientAsync(factory, $"{prefix}-own-{suffix}@t.local");
        kit.Viewer = await AuthHelper.BearerClientAsync(factory, $"{prefix}-view-{suffix}@t.local");
        kit.Member = await AuthHelper.BearerClientAsync(factory, $"{prefix}-mem-{suffix}@t.local");
        return kit;
    }

    public async Task<Guid> CaveAsync(string name, bool locationProtected = false, string visibility = "authenticated")
    {
        var response = await Owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId = CaveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        return await IdOfCreatedAsync(response);
    }

    public async Task<Guid> FeatureAsync(string name, string visibility = "authenticated", Guid? parentId = null)
    {
        var response = await Owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = GenericTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.71, 45.71 } },
            locationProtected = false,
            visibility,
            parents = parentId is { } p ? new[] { new { parentId = p, isPrimary = true } } : null,
        });
        return await IdOfCreatedAsync(response);
    }

    public async Task<Guid> EntranceAsync(Guid caveId, string name)
    {
        var response = await Owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name,
            entranceTypeId = EntranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.72, 45.72 } },
            altitude = 900m,
            positionQuality = "Gps",
        });
        return await IdOfCreatedAsync(response);
    }

    /// <summary>A small GPX centerline on the cave, named after the file.</summary>
    public async Task<Guid> CenterlineAsync(Guid caveId, string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="silexgis-test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><trkseg>
                <trkpt lat="45.500" lon="25.500"><ele>800</ele></trkpt>
                <trkpt lat="45.501" lon="25.501"><ele>810</ele></trkpt>
              </trkseg></trk>
            </gpx>
            """));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        return await IdOfCreatedAsync(await Owner.PostAsync($"/api/v1/caves/{caveId}/centerlines", form));
    }

    /// <summary>Replaces the feature's outgoing links; each pair is (target, link kind code).</summary>
    public async Task FeatureLinksAsync(Guid fromId, params (Guid To, string Kind)[] links)
    {
        var response = await Owner.PutAsJsonAsync($"/api/v1/features/{fromId}/links", new
        {
            links = links.Select(l => new { toId = l.To, linkKindCode = l.Kind, note = (string?)null }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>An untyped resource link between features, as the owner.</summary>
    public async Task<Guid> ResLinkAsync(params Guid[] featureIds)
    {
        var response = await Owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            members = featureIds.Select((id, i) => new
            {
                targetType = "feature",
                targetId = id,
                isMain = false,
                sortOrder = i,
                anchorKind = "whole",
            }).ToArray(),
        });
        return await IdOfCreatedAsync(response);
    }

    /// <summary>Takes one feature out of the viewer's sight, whatever their role grants.</summary>
    public async Task DenyViewerAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = ViewerId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Gives the viewer Read on one feature by an entry made on that row alone.</summary>
    public async Task GrantViewerAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = ViewerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = featureId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Narrows a row to the kit's caving group: readable by its members (and by those who read
    /// past visibility), by nobody else. Written to the row directly, since what is under test
    /// is how the row is read, not how its audience is chosen.
    /// </summary>
    public async Task ShareWithGroupAsync(GroupShared kind, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var group = GroupId;
        var changed = kind switch
        {
            GroupShared.Feature => await db.Features.Where(x => x.Id == id).ExecuteUpdateAsync(
                u => u.SetProperty(x => x.Visibility, Visibility.CavingGroup).SetProperty(x => x.CavingGroupId, group)),
            GroupShared.Document => await db.Documents.Where(x => x.Id == id).ExecuteUpdateAsync(
                u => u.SetProperty(x => x.Visibility, Visibility.CavingGroup).SetProperty(x => x.CavingGroupId, group)),
            GroupShared.TripLog => await db.TripLogs.Where(x => x.Id == id).ExecuteUpdateAsync(
                u => u.SetProperty(x => x.Visibility, Visibility.CavingGroup).SetProperty(x => x.CavingGroupId, group)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        changed.ShouldBe(1);
    }

    /// <summary>A small JPEG whose EXIF carries a capture point, so its stored file is positioned.</summary>
    public static byte[] GeotaggedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 64, 64);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(lat)));
        exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(lon)));
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        var seconds = (minutesFull - m) * 60d;
        return [new Rational(d), new Rational(m), new Rational(seconds)];
    }

    /// <summary>Uploads a file as the owner; the store files it under a new private document.</summary>
    public async Task<Guid> UploadAsync(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        return await IdOfCreatedAsync(await Owner.PostAsync("/api/v1/files/?allowDuplicate=true", form));
    }

    /// <summary>The document a stored file is a version of.</summary>
    public Guid DocumentOfFile(Guid fileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return db.StoredFiles.AsNoTracking().Where(f => f.Id == fileId)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
            .Single();
    }

    /// <summary>Hangs a stored file on a feature, as the owner.</summary>
    public async Task AttachToFeatureAsync(Guid fileId, Guid featureId, string role = "document")
    {
        var response = await Owner.PostAsJsonAsync("/api/v1/attachments", new
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

    public static async Task<JsonElement> GraphAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/graph/neighbourhood?{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    public static Task<JsonElement> GraphAsync(HttpClient client, string type, Guid id, string? categories = null) =>
        GraphAsync(client, $"type={type}&id={id}" + (categories is null ? string.Empty : $"&categories={categories}"));

    public static async Task<JsonElement> BodyAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    public static IEnumerable<JsonElement> Edges(JsonElement graph, string category) =>
        graph.GetProperty("edges").EnumerateArray().Where(e => e.GetProperty("category").GetString() == category);

    public static JsonElement Node(JsonElement graph, string key) =>
        graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("key").GetString() == key);

    public static bool HasNode(JsonElement graph, string key) =>
        graph.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("key").GetString() == key);

    public static JsonElement Count(JsonElement graph, string category) =>
        graph.GetProperty("counts").EnumerateArray().Single(c => c.GetProperty("category").GetString() == category);

    /// <summary>The far ends of a seed's edges in one category, as node keys.</summary>
    public static HashSet<string> Neighbours(JsonElement graph, string category, string seedKey) =>
        [.. Edges(graph, category).Select(e =>
            e.GetProperty("source").GetString() == seedKey
                ? e.GetProperty("target").GetString()!
                : e.GetProperty("source").GetString()!)];

    public static string FeatureKey(Guid id) => $"feature:{id}";

    private static async Task<Guid> IdOfCreatedAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        Admin.Dispose();
        Owner.Dispose();
        Viewer.Dispose();
        Member.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The kinds of row a test narrows to the kit's caving group.</summary>
public enum GroupShared
{
    Feature,
    Document,
    TripLog,
}
