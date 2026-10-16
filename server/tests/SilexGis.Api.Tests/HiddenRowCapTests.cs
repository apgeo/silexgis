// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
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

    public async Task InitializeAsync()
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

        (await InExtentAsync(viewerId, extent)).ShouldBe([visibleDocument]);
        (await InExtentAsync(ownerId, extent)).ShouldBe([hiddenDocument]);
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

    private async Task<IReadOnlyList<ProximityHit>> NearAsync(Guid userId, Point centre)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var search = scope.ServiceProvider.GetRequiredService<VisibleProximitySearch>();
        var ctx = await AccessContextResolver.ResolveAsync(db, userId);
        return await search.NearAsync([centre], 1000, ctx);
    }

    private async Task<IReadOnlyCollection<Guid>> InExtentAsync(Guid userId, Geometry extent)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var disclosure = scope.ServiceProvider.GetRequiredService<PhotoPositionDisclosure>();
        var ctx = await AccessContextResolver.ResolveAsync(db, userId);
        var photographs = await PhotographReads.VisiblePhotographsAsync(db, ctx, CancellationToken.None);
        return await PhotoQueries.InExtentAsync(db, ctx, disclosure, photographs, extent, 1, CancellationToken.None);
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

    private static async Task<Guid[]> MapPhotoIdsAsync(HttpClient client, string bbox)
    {
        var response = await client.GetAsync($"/api/v1/map/photos?bbox={bbox}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties").GetProperty("id").GetGuid())];
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

    public Task DisposeAsync()
    {
        owner.Dispose();
        viewer.Dispose();
        return Task.CompletedTask;
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
