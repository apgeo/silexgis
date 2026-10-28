// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The rasters stream's <c>pictureMirrored</c> and <c>pictureTurn</c>, proved against the renderer
/// itself: a mark in a stored picture is found in the rendition a phone is handed exactly where
/// those two members, the stored size and the rendition's own size say it is.
/// </summary>
/// <remarks>
/// <para>
/// The prediction is made here from the protocol's own formulas — flip left to right first, then
/// the clockwise turn, then scale to the rendition — and from nothing the server computes but the
/// four members it sends. So the test fails when the members are wrong for what the renderer does,
/// whichever of the two is at fault.
/// </para>
/// <para>
/// Every orientation code is covered as a camera writes it (in a JPEG's EXIF profile), and two as a
/// scanner writes them (in a TIFF's own tag, with no EXIF profile at all — one turning, one
/// mirroring). Each picture is then turned in the browser three times, one quarter at a time, so
/// every code meets every recorded turn. The picture is not square and the mark sits off both
/// axes and both diagonals, so each of the eight ways of drawing it puts the mark somewhere else.
/// </para>
/// </remarks>
public sealed class SyncRasterPictureTurnTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int StoredWidth = 400;
    private const int StoredHeight = 240;
    private const int RenditionSize = 160;

    /// <summary>The mark: a black square, its top left corner and side in the stored frame.</summary>
    private static readonly (int X, int Y, int Size) Mark = (40, 20, 40);

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient uploader = null!;
    private Guid clubId;
    private long caveTypeId;

    public SyncRasterPictureTurnTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncrasterturn-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        var uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"srt-up-{marker}@t.local");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Club {marker}", Slug = $"club-{marker}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;
            await RosterHelper.AddMemberAsync(db, clubId, uploaderId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        uploader = await AuthHelper.BearerClientAsync(factory, $"srt-up-{marker}@t.local");
    }

    [Fact]
    public async Task A_mark_on_a_stored_picture_is_in_every_rendition_where_the_streams_turn_and_mirror_put_it()
    {
        var cave = await CreateCaveAsync();
        var set = await CreateSetAsync([cave]);

        var pictures = new List<(Guid Map, string Label)>();
        var rows = new List<object>();
        foreach (var (format, code, extension) in Pictures())
        {
            var bytes = Corpus.Oriented(format, code, StoredWidth, StoredHeight, Mark, $"{marker} {format} {code}");
            ShouldStateTheCodeTheWayItsFormatDoes(bytes, format, code);
            var map = Guid.CreateVersion7();
            var fileName = $"plan-{code}.{extension}";
            rows.Add(await NewMapAsync(map, cave, $"{format} {code}", fileName, bytes));
            pictures.Add((map, $"{format} code {code}"));
        }

        var answer = await UploadAsync(set, rows);
        answer.GetProperty("rasterMaps").EnumerateArray()
            .Select(r => r.GetProperty("status").GetString()).ShouldAllBe(s => s == "created");

        var documents = new Dictionary<Guid, Guid>();
        var cursor = (string?)null;
        for (var recorded = 0; recorded < 4; recorded++)
        {
            var page = await GetPageAsync($"/api/v1/sync/sets/{set}/rasters");
            foreach (var (mapId, label) in pictures)
            {
                var map = page.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == mapId);
                var because = $"{label}, {recorded} recorded quarter turn(s)";
                map.GetProperty("pictureWidth").GetInt32().ShouldBe(StoredWidth, because);
                map.GetProperty("pictureHeight").GetInt32().ShouldBe(StoredHeight, because);
                var mirrored = map.GetProperty("pictureMirrored").GetBoolean();
                var turn = map.GetProperty("pictureTurn").GetInt32();
                new[] { 0, 90, 180, 270 }.ShouldContain(turn, because);
                documents[mapId] = map.GetProperty("documentId").GetGuid();

                using var rendition = new MagickImage(await RenditionAsync(documents[mapId]));
                var (turnedWidth, turnedHeight) = turn is 90 or 270
                    ? (StoredHeight, StoredWidth)
                    : (StoredWidth, StoredHeight);
                ((int)rendition.Width, (int)rendition.Height).ShouldBe(
                    Fitted(turnedWidth, turnedHeight), $"{because}: the rendition is the turned frame, fitted");

                // The mark's centre, carried through the protocol's formulas and scaled to the rendition.
                var centre = (X: Mark.X + ((Mark.Size - 1) / 2.0), Y: Mark.Y + ((Mark.Size - 1) / 2.0));
                var (px, py) = FlipThenTurn(centre.X, centre.Y, mirrored, turn);
                var expected = (
                    X: ((px + 0.5) * rendition.Width / turnedWidth) - 0.5,
                    Y: ((py + 0.5) * rendition.Height / turnedHeight) - 0.5);
                var found = DarkCentre(rendition);
                found.ShouldNotBeNull($"{because}: the mark is in the rendition");
                Math.Abs(found.Value.X - expected.X).ShouldBeLessThan(2.5, $"{because}: mark across (mirrored {mirrored}, turn {turn})");
                Math.Abs(found.Value.Y - expected.Y).ShouldBeLessThan(2.5, $"{because}: mark down (mirrored {mirrored}, turn {turn})");
            }

            if (recorded == 3)
            {
                break;
            }

            // Turned in the browser, a quarter clockwise. The map comes again past the position a
            // phone held, so a phone keeping renditions learns that they are drawn differently now.
            cursor = page.GetProperty("nextCursor").GetString();
            var turned = await uploader.PostAsJsonAsync("/api/v1/photos/bulk", new
            {
                documentIds = pictures.Select(p => documents[p.Map]).ToArray(),
                rotateQuarterTurns = 1,
            });
            turned.StatusCode.ShouldBe(HttpStatusCode.OK, await turned.Content.ReadAsStringAsync());
            var again = await GetPageAsync($"/api/v1/sync/sets/{set}/rasters?cursor={Uri.EscapeDataString(cursor!)}");
            again.GetProperty("maps").EnumerateArray().Select(m => m.GetProperty("id").GetGuid())
                .ShouldBe(pictures.Select(p => p.Map), ignoreOrder: true);
        }

        // What the stream reads for the scanner's files is the code the renderer obeyed, and not
        // the EXIF orientation, which those files do not have.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documentIds = documents.Values.ToList();
        var tiffs = await (from file in db.StoredFiles.AsNoTracking()
                           join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                           where documentIds.Contains(version.DocumentId) && file.MimeType == "image/tiff"
                           select new { file.PixelOrientation, file.Metadata })
            .ToListAsync();
        tiffs.Select(t => t.PixelOrientation).ShouldBe([6, 5], ignoreOrder: true);
        tiffs.ShouldAllBe(t => Domain.Documents.PhotoExif.FromMetadata(t.Metadata).Orientation == null);
    }

    [Fact]
    public async Task Both_members_are_null_exactly_when_the_size_is()
    {
        var cave = await CreateCaveAsync();
        var set = await CreateSetAsync([cave]);
        var sized = Guid.CreateVersion7();
        var unsized = Guid.CreateVersion7();
        await UploadAsync(set, [
            await NewMapAsync(sized, cave, "Sized", "sized.jpg", Corpus.Oriented(MagickFormat.Jpeg, 1, 64, 48)),
            await NewMapAsync(unsized, cave, "Unsized", "unsized.jpg", Corpus.Oriented(MagickFormat.Jpeg, 8, 64, 48)),
        ]);

        // A picture stored before its size was read: nothing read at write, and no camera profile
        // stating a size either.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var document = await db.RasterMaps.Where(m => m.Id == unsized).Select(m => m.DocumentId).SingleAsync();
            var file = await db.StoredFiles.SingleAsync(f => db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.DocumentId == document));
            file.PixelWidth = null;
            file.PixelHeight = null;
            file.PixelOrientation = null;
            file.Metadata = "{}";
            await db.SaveChangesAsync();
        }

        var page = await GetPageAsync($"/api/v1/sync/sets/{set}/rasters");
        var withSize = page.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == sized);
        withSize.GetProperty("pictureWidth").GetInt32().ShouldBe(64);
        withSize.GetProperty("pictureMirrored").GetBoolean().ShouldBeFalse();
        withSize.GetProperty("pictureTurn").GetInt32().ShouldBe(0);
        var withoutSize = page.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == unsized);
        foreach (var member in new[] { "pictureWidth", "pictureHeight", "pictureMirrored", "pictureTurn" })
        {
            withoutSize.GetProperty(member).ValueKind.ShouldBe(JsonValueKind.Null, member);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    /// <summary>Every code in a camera's JPEG; one turning and one mirroring code in a scanner's TIFF.</summary>
    private static IEnumerable<(MagickFormat Format, int Code, string Extension)> Pictures()
    {
        for (var code = 1; code <= 8; code++)
        {
            yield return (MagickFormat.Jpeg, code, "jpg");
        }

        yield return (MagickFormat.Tiff, 6, "tif");
        yield return (MagickFormat.Tiff, 5, "tif");
    }

    /// <summary>
    /// Fixture proof: the JPEG states its code in its EXIF profile, and the TIFF in its own tag with
    /// no EXIF profile at all.
    /// </summary>
    private static void ShouldStateTheCodeTheWayItsFormatDoes(byte[] bytes, MagickFormat format, int code)
    {
        using var image = new MagickImage(bytes);
        ((int)image.Width, (int)image.Height).ShouldBe((StoredWidth, StoredHeight));
        var exifCode = image.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value;
        if (code == 1)
        {
            // Upright as stored: the library may leave the code out, and reads none back.
            ((int)image.Orientation).ShouldBeOneOf(0, 1);
            exifCode.ShouldBeOneOf(null, (ushort?)1);
            return;
        }

        ((int)image.Orientation).ShouldBe(code);
        if (format == MagickFormat.Jpeg)
        {
            exifCode.ShouldBe((ushort)code);
        }
        else
        {
            exifCode.ShouldBeNull();
        }
    }

    /// <summary>The protocol's formulas: flip left to right if mirrored, then the clockwise turn.</summary>
    private static (double X, double Y) FlipThenTurn(double x, double y, bool mirrored, int turn)
    {
        var fx = mirrored ? StoredWidth - 1 - x : x;
        var fy = y;
        return turn switch
        {
            0 => (fx, fy),
            90 => (StoredHeight - 1 - fy, fx),
            180 => (StoredWidth - 1 - fx, StoredHeight - 1 - fy),
            270 => (fy, StoredWidth - 1 - fx),
            _ => throw new ArgumentOutOfRangeException(nameof(turn), turn, null),
        };
    }

    /// <summary>A frame fitted into the rendition's box, its longer side to the box, never enlarged.</summary>
    private static (int Width, int Height) Fitted(int width, int height) =>
        width >= height
            ? (RenditionSize, (int)Math.Round(height * (double)RenditionSize / width))
            : ((int)Math.Round(width * (double)RenditionSize / height), RenditionSize);

    /// <summary>The centre of the dark pixels of a rendition, or null when there are none.</summary>
    private static (double X, double Y)? DarkCentre(MagickImage image)
    {
        using var pixels = image.GetPixels();
        double sumX = 0, sumY = 0;
        var count = 0;
        var values = pixels.ToByteArray(PixelMapping.RGB)!;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var at = ((y * (int)image.Width) + x) * 3;
                var luminance = (values[at] + values[at + 1] + values[at + 2]) / 3;
                if (luminance < 128)
                {
                    sumX += x;
                    sumY += y;
                    count++;
                }
            }
        }

        return count == 0 ? null : (sumX / count, sumY / count);
    }

    private async Task<byte[]> RenditionAsync(Guid documentId)
    {
        var links = await uploader.PostAsJsonAsync("/api/v1/sync/files/links", new
        {
            items = new[] { new { documentId, rendition = RenditionSize } },
        });
        var payload = await links.Content.ReadAsStringAsync();
        links.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var url = JsonDocument.Parse(payload).RootElement.GetProperty("links")[0].GetProperty("url").GetString()!;
        var response = await uploader.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<object> NewMapAsync(Guid id, Guid caveId, string title, string fileName, byte[] picture) => new
    {
        id,
        caveId,
        title,
        mapType = "planView",
        fileName,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(picture)),
        sizeBytes = picture.Length,
        stagedUploadId = await StageAsync(fileName, picture),
    };

    private async Task<Guid> StageAsync(string fileName, byte[] bytes)
    {
        var opened = await uploader.PostAsJsonAsync("/api/v1/files/uploads", new { fileName, sizeBytes = bytes.Length, stageForSync = true });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await uploader.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    private async Task<JsonElement> UploadAsync(Guid set, IReadOnlyList<object> maps)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rasterMaps = maps,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<JsonElement> GetPageAsync(string url)
    {
        var response = await uploader.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<Guid> CreateSetAsync(IReadOnlyList<Guid> roots)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            cavingGroupId = clubId,
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Turns {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }
}
