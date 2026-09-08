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
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The routes that hand computed pictures of the ground to a reader: who may ask for them, what
/// they are told about a picture drawn from elevation that has since been replaced, and how the
/// rasters themselves are handed over to something that cannot present a credential.
/// </summary>
/// <remarks>
/// <para>
/// These are pictures of the surface, computed from public elevation. They hold no cave position,
/// so they carry no owner, no audience and no visibility, and the whole permission story is the
/// terrain right — which is what the first test states in full rather than leaving to be inferred
/// from a passing happy path.
/// </para>
/// <para>
/// The delivery route is reached without a signature, because a tile reader in a browser fetches
/// ranges of a raster and can put no header on those requests. What stands in for the header is a
/// signature in the address, minted for one raster of one picture when a caller who held the
/// terrain right read the listing. The test for it is therefore not "does it stream" but "does an
/// address that was not signed for these bytes fail to open them".
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TerrainDerivativeApiTests : IAsyncLifetime, IDisposable
{
    private const int LongitudeLatitude = 4326;

    private readonly SilexGisApiFactory factory;
    private readonly string buildRoot;
    private readonly List<Guid> seeded = [];

    private HttpClient holder = null!;    // an ordinary account granted the terrain right
    private HttpClient outsider = null!;  // an ordinary account granted nothing at all
    private HttpClient anonymous = null!;

    public TerrainDerivativeApiTests(PostgresFixture postgres)
    {
        buildRoot = Path.Combine(Path.GetTempPath(), $"silexgis-tderivapi-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Terrain:BuildRoot"] = buildRoot },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Plain members on both sides. An administrator is waved through before any terrain rule is
        // consulted, so proving the rule needs an account that holds exactly one entry — and an
        // account that holds none. The seeded ruleset every account joins says nothing about
        // terrain, so the outsider genuinely holds no terrain right rather than being assumed to.
        var holderId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"td-hold-{suffix}@t.local");
        holder = await AuthHelper.BearerClientAsync(factory, $"td-hold-{suffix}@t.local");
        await GrantAsync(holderId, AccessAction.Read | AccessAction.Execute | AccessAction.Delete);

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"td-out-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"td-out-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.Where(b => seeded.Contains(b.Id)).ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(buildRoot))
            {
                Directory.Delete(buildRoot, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Litter, not a failing test.
        }
    }

    /// <summary>
    /// The whole permission table for the listing, in one test: refused without a signature,
    /// refused with a signature and no right, allowed with the right and nothing else.
    /// </summary>
    [Fact]
    public async Task Listing_pictures_takes_the_terrain_right_and_nothing_less()
    {
        var buildId = await SeedBuildAsync(active: true);
        await SeedLayerAsync(buildId, TerrainDerivative.Hillshade, "a picture to be seen or not");

        (await anonymous.GetAsync("/api/v1/terrain/derivatives")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.GetAsync("/api/v1/terrain/derivatives")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var allowed = await holder.GetAsync("/api/v1/terrain/derivatives");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listing = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        listing.EnumerateArray()
            .Any(l => l.GetProperty("terrainBuildId").GetGuid() == buildId)
            .ShouldBeTrue(listing.ToString());
    }

    /// <summary>
    /// A picture whose elevation has been replaced is listed as out of date — and is still listed.
    /// </summary>
    /// <remarks>
    /// Both halves matter and neither is enough alone. Withdrawing it would take a picture away
    /// from a reader over ground that has probably not moved; leaving it unmarked would have them
    /// read a shaded relief that disagrees with the heights beneath it as a fault in the cave data
    /// rather than in the tile.
    /// </remarks>
    [Fact]
    public async Task A_picture_drawn_from_replaced_elevation_says_so_and_is_still_offered()
    {
        var original = await SeedBuildAsync(active: true);
        var layerId = await SeedLayerAsync(original, TerrainDerivative.Slope, "steepness");

        (await FindAsync(layerId)).GetProperty("stale").GetBoolean().ShouldBeFalse();

        await SeedBuildAsync(active: true); // a newer build takes over as the ground served

        var after = await FindAsync(layerId);
        after.GetProperty("stale").GetBoolean().ShouldBeTrue(after.ToString());
        after.GetProperty("status").GetString().ShouldBe("ready");
    }

    /// <summary>
    /// A raster opens for the address the listing signed for it, and for nothing else.
    /// </summary>
    [Fact]
    public async Task A_raster_opens_only_for_the_address_that_was_signed_for_it()
    {
        var buildId = await SeedBuildAsync(active: true);
        var first = await SeedLayerAsync(buildId, TerrainDerivative.Hillshade, "lit from the north-west");
        var second = await SeedLayerAsync(buildId, TerrainDerivative.Aspect, "facing");

        var signed = (await FindAsync(first)).GetProperty("rasters")[0].GetProperty("url").GetString()!;
        var other = (await FindAsync(second)).GetProperty("rasters")[0].GetProperty("url").GetString()!;

        // The signed address opens the bytes, with no signature of the caller's own: the address
        // is the permission, which is the only kind a tile reader can carry.
        var opened = await anonymous.GetAsync(signed);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await opened.Content.ReadAsByteArrayAsync()).Length.ShouldBeGreaterThan(0);

        // No signature at all, and a signature minted for a different picture's raster. Both are
        // answered as a raster that does not exist: whether these bytes are here is part of what a
        // refusal keeps back.
        (await anonymous.GetAsync(signed[..signed.IndexOf("?token=", StringComparison.Ordinal)]))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var borrowed = signed[..signed.IndexOf("?token=", StringComparison.Ordinal)]
            + other[other.IndexOf("?token=", StringComparison.Ordinal)..];
        (await anonymous.GetAsync(borrowed)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Asking twice for the same picture of the same elevation answers with the one that exists.
    /// </summary>
    /// <remarks>
    /// Terrain sits on a volume that is neither swept nor backed up, and a picture is computed per
    /// build and per settings. Two rows for one request would be the same rasters stored twice,
    /// with whichever a later query returned first being the one on screen.
    /// </remarks>
    [Fact]
    public async Task Asking_twice_for_one_picture_answers_with_the_one_that_exists()
    {
        var buildId = await SeedBuildAsync(active: true);
        var request = new
        {
            terrainBuildId = buildId,
            derivative = "hillshade",
            name = "shaded relief",
            azimuthDegrees = 315d,
        };

        (await outsider.PostAsJsonAsync("/api/v1/terrain/derivatives", request)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var first = await holder.PostAsJsonAsync("/api/v1/terrain/derivatives", request);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var again = await holder.PostAsJsonAsync("/api/v1/terrain/derivatives", request);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
            .ShouldBe(firstId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TerrainDerivativeLayers.CountAsync(l => l.TerrainBuildId == buildId)).ShouldBe(1);
    }

    /// <summary>
    /// Several people asking for the same picture at the same moment get the same picture.
    /// </summary>
    /// <remarks>
    /// Each request looks for the row, finds nothing, and writes one; the index refuses all but the
    /// first. Answering the losers with the row that won is the same answer they would have had a
    /// moment later — and an unhandled refusal here would be a fault reported to somebody who did
    /// nothing wrong, over an installation that is behaving correctly.
    ///
    /// Whether the requests truly overlap is up to the machine, so this cannot be a proof that the
    /// refusal was met; what it can do is fail if meeting it is wrong, and it costs almost nothing.
    /// </remarks>
    [Fact]
    public async Task Everybody_asking_for_one_picture_at_once_gets_that_one_picture()
    {
        var buildId = await SeedBuildAsync(active: true);
        var request = new
        {
            terrainBuildId = buildId,
            derivative = "aspect",
            name = "facing, asked for by everybody at once",
        };

        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(
            _ => holder.PostAsJsonAsync("/api/v1/terrain/derivatives", request)));

        foreach (var answer in answers)
        {
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var ids = new List<Guid>();
        foreach (var answer in answers)
        {
            ids.Add((await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        ids.Distinct().Count().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TerrainDerivativeLayers.CountAsync(l => l.TerrainBuildId == buildId)).ShouldBe(1);

        // One picture asked for is one run queued, however many people asked at that instant. Named
        // by the picture rather than by the kind, because the job table is shared with every other
        // class using this container.
        // The containment is checked here rather than in the query on purpose: the payload column
        // is jsonb, and a text containment over it has no SQL form -- the provider emits LIKE and
        // the database refuses it. Narrowing by kind first keeps the set to this test's own rows.
        var queued = await db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.TerrainDerivative)
            .Select(j => j.Payload)
            .ToListAsync();

        queued.Count(p => p.Contains(ids[0].ToString(), StringComparison.Ordinal)).ShouldBe(1);
    }

    /// <summary>Settings the computation would refuse are refused before anything is queued.</summary>
    [Fact]
    public async Task Settings_that_cannot_be_computed_are_refused_rather_than_queued_and_failed()
    {
        var buildId = await SeedBuildAsync(active: true);
        var refused = await holder.PostAsJsonAsync("/api/v1/terrain/derivatives", new
        {
            terrainBuildId = buildId,
            derivative = "hillshade",
            name = "lit from nowhere",
            altitudeDegrees = 0d, // a light on the horizon lights nothing
        });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TerrainDerivativeLayers.CountAsync(l => l.TerrainBuildId == buildId)).ShouldBe(0);
    }

    /// <summary>
    /// Removing a picture takes the right to remove it, and takes the picture's files with it.
    /// </summary>
    /// <remarks>
    /// The refusals are asserted against a picture that exists and files that are on disk, so that
    /// "nothing was removed" is a fact about a refusal rather than about there having been nothing
    /// to remove. The last assertion is the one worth the trouble: the directory that goes is the
    /// picture's own, and the build's prepared elevation beside it is untouched — the two are one
    /// path composition apart, and getting it wrong would take a build's ground away and report
    /// success.
    /// </remarks>
    [Fact]
    public async Task Removing_a_picture_takes_the_terrain_right_and_takes_its_files_with_it()
    {
        var buildId = await SeedBuildAsync(active: true);
        var layerId = await SeedLayerAsync(buildId, TerrainDerivative.Hillshade, "a picture to be swept");

        var pictures = Path.Combine(buildRoot, buildId.ToString("N"), "derivatives", layerId.ToString("N"));
        var prepared = Path.Combine(buildRoot, buildId.ToString("N"), "prepared");
        Directory.CreateDirectory(prepared);
        await File.WriteAllBytesAsync(Path.Combine(prepared, "tile.tif"), [0x49, 0x49, 0x2a, 0x00]);

        (await anonymous.DeleteAsync($"/api/v1/terrain/derivatives/{layerId}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.DeleteAsync($"/api/v1/terrain/derivatives/{layerId}")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Refused, and nothing gone: the row is still there and so are its bytes.
        Directory.Exists(pictures).ShouldBeTrue();
        await using (var refusedScope = factory.Services.CreateAsyncScope())
        {
            var refusedDb = refusedScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await refusedDb.TerrainDerivativeLayers.CountAsync(l => l.Id == layerId)).ShouldBe(1);
        }

        (await holder.DeleteAsync($"/api/v1/terrain/derivatives/{layerId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TerrainDerivativeLayers.CountAsync(l => l.Id == layerId)).ShouldBe(0);
        (await db.TerrainDerivativeRasters.CountAsync(r => r.TerrainDerivativeLayerId == layerId))
            .ShouldBe(0);

        Directory.Exists(pictures).ShouldBeFalse();
        File.Exists(Path.Combine(prepared, "tile.tif")).ShouldBeTrue();

        // A picture that is already gone is not there to be removed, said with a stable code.
        var again = await holder.DeleteAsync($"/api/v1/terrain/derivatives/{layerId}");
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> FindAsync(Guid layerId)
    {
        var response = await holder.GetAsync("/api/v1/terrain/derivatives");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listing = await response.Content.ReadFromJsonAsync<JsonElement>();
        return listing.EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == layerId);
    }

    private async Task GrantAsync(Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedBuildAsync(bool active = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // At most one build in the whole installation is active, held by a unique index rather than
        // by a handler, and the PostGIS container is shared with every other test class.
        if (active)
        {
            await db.TerrainBuilds.Where(b => b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false));
        }

        var build = new TerrainBuild
        {
            Extent = Rectangle(25.0, 46.0, 25.1, 46.1),
            RequestedMaxDepth = 13,
            Status = TerrainBuildStatus.Succeeded,
            Phase = TerrainBuildPhase.Publish,
            IsActive = active,
        };

        db.TerrainBuilds.Add(build);
        await db.SaveChangesAsync();
        seeded.Add(build.Id);
        return build.Id;
    }

    /// <summary>A finished picture with one raster, and bytes on disk behind it.</summary>
    private async Task<Guid> SeedLayerAsync(Guid buildId, TerrainDerivative derivative, string name)
    {
        var settings = TerrainDerivativeRegistry.Normalise(
            new TerrainDerivativeSettings { Derivative = derivative });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var layer = new TerrainDerivativeLayer
        {
            TerrainBuildId = buildId,
            Derivative = derivative,
            Settings = TerrainDerivativeRegistry.Describe(settings),
            SettingsHash = TerrainDerivativeRegistry.Fingerprint(settings),
            Name = name,
            Status = TerrainDerivativeStatus.Ready,
            Version = 1,
            ComputedAt = DateTimeOffset.UtcNow,
        };
        db.TerrainDerivativeLayers.Add(layer);

        // Bytes rather than a real raster: the delivery route hands over a file and never opens it,
        // so what is on disk only has to exist and be non-empty for this to mean anything.
        var directory = Path.Combine(
            buildRoot, buildId.ToString("N"), "derivatives", layer.Id.ToString("N"), "v1");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "tile.tif");
        await File.WriteAllBytesAsync(file, [0x49, 0x49, 0x2a, 0x00]);

        db.TerrainDerivativeRasters.Add(new TerrainDerivativeRaster
        {
            TerrainDerivativeLayerId = layer.Id,
            SourcePath = Path.Combine(buildRoot, buildId.ToString("N"), "prepared", "tile.tif"),
            Path = file,
            Width = 16,
            Height = 16,
            PixelSizeDegrees = 0.001,
            Footprint = Rectangle(25.0, 46.0, 25.1, 46.1),
            SizeBytes = 4,
            ComputedAt = DateTimeOffset.UtcNow,
        });

        layer.SizeBytes = 4;
        await db.SaveChangesAsync();
        return layer.Id;
    }

    private static Polygon Rectangle(double west, double south, double east, double north) =>
        new GeometryFactory(new PrecisionModel(), LongitudeLatitude).CreatePolygon(
        [
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south),
        ]);
}
