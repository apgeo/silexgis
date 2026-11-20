// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.Map;
using SilexGis.Api.Features.Photos;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A capped answer counts only rows its caller is shown, so a row withheld for its position can
/// never take a place in it.
/// </summary>
/// <remarks>
/// <para>
/// Three surfaces read a window or a reach, cut the rows at a ceiling, and only then dropped the
/// ones whose position the caller may not be told: the photo map (<c>/map/photos</c>, at the
/// map's point cap), the photo listing narrowed to a map extent (<c>/photos?bbox</c>, at three
/// thousand), and the proximity pool every importer compares against (two thousand nearest by
/// true distance). Each time the window held more than the ceiling, a hidden row took a place and
/// pushed a visible one out — and the visible one came back the moment an edge, or the search
/// centre, moved past the hidden row's true position. Which visible row a capped answer holds was
/// therefore a probe of where the hidden one is, without its coordinate ever being sent.
/// </para>
/// <para>
/// Each test sets up exactly the displacement: a hidden row ordered ahead of a visible one, and a
/// ceiling with room for one of them. The control in each is a caller who may place the hidden
/// row, for whom it does take the place.
/// </para>
/// </remarks>
public sealed class HiddenRowCapTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid ownerId;
    private Guid viewerId;
    private long caveTypeId;
    private long entranceTypeId;

    public HiddenRowCapTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, Settings());
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cap-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cap-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cap-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"cap-view-{suffix}@t.local");
    }

    /// <summary>
    /// The photo map with room for one point: the older photo hangs on a protected cave the
    /// viewer reads and may not place, the newer on an ordinary one. The viewer is shown the
    /// newer; the owner, who places both, the older.
    /// </summary>
    [Fact]
    public async Task A_withheld_photo_cannot_take_the_only_place_on_the_photo_map()
    {
        using var capped = new SilexGisApiFactory(
            postgres.ConnectionString, Settings(new() { ["Map:MaxPoints"] = "1" }));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Editor, $"capm-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Viewer, $"capm-view-{suffix}@t.local");
        using var cappedOwner = await AuthHelper.BearerClientAsync(capped, $"capm-own-{suffix}@t.local");
        using var cappedViewer = await AuthHelper.BearerClientAsync(capped, $"capm-view-{suffix}@t.local");

        // Ids are time-ordered and the map's cap is applied in id order, so the photo uploaded
        // first is the one the single place goes to unless something keeps it out.
        var hidden = await UploadPhotoAsync(cappedOwner, 61.201, 14.301);
        var visible = await UploadPhotoAsync(cappedOwner, 61.202, 14.302);
        await AttachAsync(cappedOwner, hidden, await CreateCaveAsync(cappedOwner, "Cap Guarded", locationProtected: true));
        await AttachAsync(cappedOwner, visible, await CreateCaveAsync(cappedOwner, "Cap Open", locationProtected: false));

        const string window = "14.2,61.1,14.4,61.3";
        (await MapPhotoIdsAsync(cappedViewer, window)).ShouldBe([visible]);
        (await MapPhotoIdsAsync(cappedOwner, window)).ShouldBe([hidden]);
    }

    /// <summary>
    /// The photo listing narrowed to an extent, with room for one: the newer photo — which the
    /// listing takes first — hangs on a protected cave, the older on an ordinary one.
    /// </summary>
    [Fact]
    public async Task A_withheld_photo_cannot_take_the_only_place_in_an_extent()
    {
        var visible = await UploadPhotoAsync(owner, 62.201, 15.301);
        var hidden = await UploadPhotoAsync(owner, 62.202, 15.302);
        await AttachAsync(owner, visible, await CreateCaveAsync(owner, "Extent Open", locationProtected: false));
        await AttachAsync(owner, hidden, await CreateCaveAsync(owner, "Extent Guarded", locationProtected: true));

        var extent = new GeometryFactory(new PrecisionModel(), 4326).ToGeometry(new Envelope(15.2, 15.4, 62.1, 62.3));
        var visibleDocument = await DocumentOfAsync(visible);
        var hiddenDocument = await DocumentOfAsync(hidden);

        (await InExtentAsync(viewerId, extent)).DocumentIds.ShouldBe([visibleDocument]);
        (await InExtentAsync(ownerId, extent)).DocumentIds.ShouldBe([hiddenDocument]);
    }

    /// <summary>
    /// A window crowded with photos the viewer may not be told about is read only as far as the
    /// photo map's ceiling — that many batches of the cap — and the answer says it was cut short
    /// rather than reading the window to its end. Here the cap is one and the visible photo comes
    /// after a ceiling's worth of hidden ones.
    /// </summary>
    [Fact]
    public async Task The_photo_map_reads_no_further_than_its_ceiling_and_says_it_stopped()
    {
        using var capped = new SilexGisApiFactory(
            postgres.ConnectionString, Settings(new() { ["Map:MaxPoints"] = "1" }));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Editor, $"capc-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(capped, GlobalRoles.Viewer, $"capc-view-{suffix}@t.local");
        using var cappedOwner = await AuthHelper.BearerClientAsync(capped, $"capc-own-{suffix}@t.local");
        using var cappedViewer = await AuthHelper.BearerClientAsync(capped, $"capc-view-{suffix}@t.local");

        var guarded = await CreateCaveAsync(cappedOwner, "Ceiling Guarded", locationProtected: true);
        var hidden = new List<Guid>();
        for (var i = 0; i < MapEndpoints.MaxPhotoBatches; i++)
        {
            var photo = await UploadPhotoAsync(cappedOwner, 64.201 + (i * 0.0001), 17.301);
            await AttachAsync(cappedOwner, photo, guarded);
            hidden.Add(photo);
        }

        var visible = await UploadPhotoAsync(cappedOwner, 64.202, 17.302);
        await AttachAsync(cappedOwner, visible, await CreateCaveAsync(cappedOwner, "Ceiling Open", locationProtected: false));

        const string window = "17.2,64.1,17.4,64.3";
        var forViewer = await MapPhotosAsync(cappedViewer, window);
        forViewer.Ids.ShouldBeEmpty();
        forViewer.CutShort.ShouldBeTrue("the window was read past its ceiling instead of being cut short");

        // The control: the owner places the first photo, which fills the cap at once.
        var forOwner = await MapPhotosAsync(cappedOwner, window);
        forOwner.Ids.ShouldBe([hidden[0]]);
        forOwner.CutShort.ShouldBeFalse();
    }

    /// <summary>
    /// The extent read stops at its ceiling the same way: with room for one photo and two batches,
    /// two newer hidden photos use the whole ceiling, and the answer is cut short; with a third
    /// batch the older visible photo is reached and the answer is whole.
    /// </summary>
    [Fact]
    public async Task An_extent_reads_no_further_than_its_ceiling_and_says_it_stopped()
    {
        var visible = await UploadPhotoAsync(owner, 65.201, 18.301);
        var guarded = await CreateCaveAsync(owner, "Extent Ceiling Guarded", locationProtected: true);
        foreach (var lat in new[] { 65.202, 65.203 })
        {
            await AttachAsync(owner, await UploadPhotoAsync(owner, lat, 18.302), guarded);
        }

        await AttachAsync(owner, visible, await CreateCaveAsync(owner, "Extent Ceiling Open", locationProtected: false));
        var extent = new GeometryFactory(new PrecisionModel(), 4326).ToGeometry(new Envelope(18.2, 18.4, 65.1, 65.3));

        var cut = await InExtentAsync(viewerId, extent, maxBatches: 2);
        cut.DocumentIds.ShouldBeEmpty();
        cut.CutShort.ShouldBeTrue("the extent was read past its ceiling instead of being cut short");

        var whole = await InExtentAsync(viewerId, extent, maxBatches: 3);
        whole.DocumentIds.ShouldBe([await DocumentOfAsync(visible)]);
        whole.CutShort.ShouldBeFalse();
    }

    /// <summary>
    /// The proximity pool, filled to its ceiling with ordinary points, and a protected entrance
    /// nearer the centre than any of them. Every ordinary point is still in the pool for a caller
    /// who may not place the entrance.
    /// </summary>
    [Fact]
    public async Task A_withheld_entrance_cannot_take_a_place_in_the_proximity_pool()
    {
        const double lon = 16.40;
        const double lat = 63.40;
        await CreateCaveWithEntranceAsync(owner, "Pool Guarded", lon + 0.00001, lat, locationProtected: true);

        long sinkholeTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            var points = new GeometryFactory(new PrecisionModel(), 4326);

            // A ring of ordinary points well outside the entrance's distance from the centre,
            // exactly as many as the pool holds. Derived state is recomputed once for the lot,
            // as an import does.
            writer.BeginDeferredDerivedState();
            for (var i = 0; i < VisibleProximitySearch.MaxNearbyFeatures; i++)
            {
                var angle = 2 * Math.PI * i / VisibleProximitySearch.MaxNearbyFeatures;
                await writer.CreateGenericAsync(new Feature
                {
                    Name = $"Pool point {i}",
                    FeatureTypeId = sinkholeTypeId,
                    Geom = points.CreatePoint(new Coordinate(lon + (0.002 * Math.Cos(angle)), lat + (0.001 * Math.Sin(angle)))),
                    OwnerUserId = ownerId,
                    Visibility = Visibility.Authenticated,
                }, []);
            }

            await writer.FlushDerivedStateAsync();
            await db.SaveChangesAsync();
        }

        var centre = new GeometryFactory(new PrecisionModel(), 4326).CreatePoint(new Coordinate(lon, lat));
        var forViewer = await NearAsync(viewerId, centre);
        forViewer.Count.ShouldBe(VisibleProximitySearch.MaxNearbyFeatures, "a hidden entrance took a place in the pool");
        forViewer.ShouldAllBe(hit => hit.Kind == FeatureKind.Generic);

        // The control: the owner places the entrance, and for them it is the nearest thing there.
        var forOwner = await NearAsync(ownerId, centre);
        forOwner.Count.ShouldBe(VisibleProximitySearch.MaxNearbyFeatures);
        forOwner.ShouldContain(hit => hit.Kind == FeatureKind.CaveEntrance);
    }

    /// <summary>
    /// Two candidates far apart with more points between them than the pool holds: each candidate
    /// still finds the place a metre from it. Ranked from the middle of the page and cut once, the
    /// pool was spent on the points between them and both neighbours were cut.
    /// </summary>
    [Fact]
    public async Task Each_candidate_keeps_its_nearest_place_however_many_lie_between_the_candidates()
    {
        const double lon = 16.80;
        const double lat = 63.80;
        const double halfSpan = 0.15;
        var geometry = new GeometryFactory(new PrecisionModel(), 4326);
        var west = geometry.CreatePoint(new Coordinate(lon - halfSpan, lat));
        var east = geometry.CreatePoint(new Coordinate(lon + halfSpan, lat));
        Guid westNeighbour;
        Guid eastNeighbour;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            var sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();

            Task<Feature> PointAsync(string name, double x, double y) => writer.CreateGenericAsync(new Feature
            {
                Name = name,
                FeatureTypeId = sinkholeTypeId,
                Geom = geometry.CreatePoint(new Coordinate(x, y)),
                OwnerUserId = ownerId,
                Visibility = Visibility.Authenticated,
            }, []);

            writer.BeginDeferredDerivedState();
            for (var i = 0; i <= VisibleProximitySearch.MaxNearbyFeatures; i++)
            {
                var angle = 2 * Math.PI * i / VisibleProximitySearch.MaxNearbyFeatures;
                await PointAsync($"Between {i}", lon + (0.01 * Math.Cos(angle)), lat + (0.005 * Math.Sin(angle)));
            }

            // About a metre east of the west candidate and west of the east one.
            westNeighbour = (await PointAsync("West neighbour", west.X + 0.00002, lat)).Id;
            eastNeighbour = (await PointAsync("East neighbour", east.X - 0.00002, lat)).Id;
            await writer.FlushDerivedStateAsync();
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var search = scope.ServiceProvider.GetRequiredService<VisibleProximitySearch>();
            var ctx = await AccessContextResolver.ResolveAsync(db, ownerId);

            // A radius that reaches the points between from either candidate.
            var pool = await search.NearAsync([west, east], 10_000, ctx);
            var ids = pool.Select(hit => hit.FeatureId).ToList();
            ids.ShouldContain(westNeighbour, "the west candidate's neighbour was cut from the pool");
            ids.ShouldContain(eastNeighbour, "the east candidate's neighbour was cut from the pool");

            // One candidate alone is still given the whole ceiling.
            (await search.NearAsync([west], 10_000, ctx)).Count.ShouldBe(VisibleProximitySearch.MaxNearbyFeatures);
        }
    }

    /// <summary>
    /// The proximity pool holds points only, decided before its cut: an area lying over the
    /// centre — nearer than any point can be — does not take the place a point then loses.
    /// </summary>
    [Fact]
    public async Task An_area_over_the_centre_does_not_take_a_point_s_place_in_the_proximity_pool()
    {
        const double lon = 16.60;
        const double lat = 63.60;
        var geometry = new GeometryFactory(new PrecisionModel(), 4326);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
        var areaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();

        var area = await writer.CreateGenericAsync(new Feature
        {
            Name = "Pool area over the centre",
            FeatureTypeId = areaTypeId,
            Geom = geometry.CreatePolygon([
                new(lon - 0.0005, lat - 0.0005), new(lon + 0.0005, lat - 0.0005),
                new(lon + 0.0005, lat + 0.0005), new(lon - 0.0005, lat + 0.0005), new(lon - 0.0005, lat - 0.0005)]),
            OwnerUserId = ownerId,
            Visibility = Visibility.Authenticated,
        }, []);
        var point = await writer.CreateGenericAsync(new Feature
        {
            Name = "Pool point beside the area",
            FeatureTypeId = sinkholeTypeId,
            Geom = geometry.CreatePoint(new Coordinate(lon + 0.002, lat)),
            OwnerUserId = ownerId,
            Visibility = Visibility.Authenticated,
        }, []);
        await db.SaveChangesAsync();

        var ctx = await AccessContextResolver.ResolveAsync(db, ownerId);
        var ids = await ProximitySql.NearestPlaceableIdsAsync(
            db, ctx, [new Coordinate(lon, lat)], 1000, [FeatureKind.Generic], 1, CancellationToken.None);

        ids.ShouldBe([point.Id]);
        (await db.Features.CountAsync(f => f.Id == area.Id)).ShouldBe(1);
    }

    private async Task<IReadOnlyList<ProximityHit>> NearAsync(Guid userId, Point centre)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var search = scope.ServiceProvider.GetRequiredService<VisibleProximitySearch>();
        var ctx = await AccessContextResolver.ResolveAsync(db, userId);
        return await search.NearAsync([centre], 1000, ctx);
    }

    private async Task<PhotoExtent> InExtentAsync(
        Guid userId, Geometry extent, int maxBatches = PhotoQueries.MaxExtentBatches)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var disclosure = scope.ServiceProvider.GetRequiredService<PhotoPositionDisclosure>();
        var ctx = await AccessContextResolver.ResolveAsync(db, userId);
        var photographs = await PhotographReads.VisiblePhotographsAsync(db, ctx, CancellationToken.None);
        return await PhotoQueries.InExtentAsync(
            db, ctx, disclosure, photographs, extent, 1, maxBatches, CancellationToken.None);
    }

    private async Task<Guid> DocumentOfAsync(Guid fileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from file in db.StoredFiles
                      join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                      where file.Id == fileId
                      select version.DocumentId).SingleAsync();
    }

    private static async Task<Guid[]> MapPhotoIdsAsync(HttpClient client, string bbox) =>
        (await MapPhotosAsync(client, bbox)).Ids;

    private static async Task<(Guid[] Ids, bool CutShort)> MapPhotosAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/map/photos?bbox={bbox}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var root = JsonDocument.Parse(payload).RootElement;
        var ids = root.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid())
            .ToArray();
        return (ids, root.TryGetProperty("cutShort", out var cut) && cut.GetBoolean());
    }

    private static async Task<Guid> UploadPhotoAsync(HttpClient client, double lat, double lon)
    {
        var content = new ByteArrayContent(MakeGeotaggedJpeg(lat, lon));
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", $"cap-{Guid.NewGuid():N}.jpg" } };
        var response = await client.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AttachAsync(HttpClient client, Guid fileId, Guid caveId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "photoEntrance",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(HttpClient client, string name, bool locationProtected)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..36],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task CreateCaveWithEntranceAsync(
        HttpClient client, string name, double lon, double lat, bool locationProtected)
    {
        var caveId = await CreateCaveAsync(client, name, locationProtected);
        var entrance = await client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "Gps",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
    }

    private Dictionary<string, string?> Settings(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }

    private static byte[] MakeGeotaggedJpeg(double lat, double lon)
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

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A scratch folder left behind is harmless; the next run uses a fresh one.
        }
    }
}
