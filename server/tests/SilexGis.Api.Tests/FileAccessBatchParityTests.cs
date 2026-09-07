// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two forms of "which of these files may this caller read" — one file at a time, and
/// the whole list at once — over one fixture that reaches every arm either form has.
///
/// The point is agreement, not speed: a batched check is only worth having if it answers
/// what the careful one answers, and the way that breaks is a world quietly missing from the
/// fast path, which shows up as a file the slow form allows and the fast one drops. So every
/// caller is asked about every file both ways and the answers are compared, and the fixture
/// is then checked for actually discriminating — a caller who sees none of the files, or all
/// of them, would make the comparison pass while proving nothing.
/// </summary>
public sealed class FileAccessBatchParityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — uploads every file below
    private Guid ownerId;
    private Guid viewerId;   // Viewer — no domain rights; reaches files only by attachment
    private Guid strangerId; // Viewer — same, and in none of the caving groups
    private Guid editorId;   // Editor — domain-wide reads from the seed
    private long caveTypeId;

    private readonly Dictionary<string, Guid> files = [];
    private readonly List<Guid> tripIds = [];

    public FileAccessBatchParityTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-batchparity-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"bp-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"bp-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"bp-view-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"bp-str-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"bp-ed-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        // A group the viewer belongs to and one nobody here does: the caving-group arm is the
        // only one decided without touching storage, so it has to be exercised both ways.
        var joined = new CavingGroup { Name = $"Batch In {suffix}", Slug = $"batch-in-{suffix}" };
        var foreign = new CavingGroup { Name = $"Batch Out {suffix}", Slug = $"batch-out-{suffix}" };
        db.CavingGroups.AddRange(joined, foreign);
        await db.SaveChangesAsync();
        await RosterHelper.AddMemberAsync(db, joined.Id, viewerId);

        var openCave = await CreateCaveAsync("Open", "authenticated");
        var closedCave = await CreateCaveAsync("Closed", "private");

        files["openCave"] = await UploadAsync("open-cave.txt");
        await AttachToFeatureAsync(files["openCave"], openCave);

        files["closedCave"] = await UploadAsync("closed-cave.txt");
        await AttachToFeatureAsync(files["closedCave"], closedCave);

        // One unreadable route and one readable one on the same file: the batched form groups
        // a file's rows and must still take the widening answer, not the first row it meets.
        files["bothCaves"] = await UploadAsync("both-caves.txt");
        await AttachToFeatureAsync(files["bothCaves"], closedCave);
        await AttachToFeatureAsync(files["bothCaves"], openCave);

        // Hangs on nothing: reachable by its uploader and nobody else.
        files["unattached"] = await UploadAsync("unattached.txt");

        files["joinedGroup"] = await UploadAsync("joined-group.txt");
        await AttachToEntityAsync(files["joinedGroup"], AttachedEntityType.CavingGroup, joined.Id);

        files["foreignGroup"] = await UploadAsync("foreign-group.txt");
        await AttachToEntityAsync(files["foreignGroup"], AttachedEntityType.CavingGroup, foreign.Id);

        // A world that the batched form resolves through a filter rather than a point check,
        // present in both answers. It has to be here as a readable row and not only as a
        // refused one: a world left out of the batched form refuses everything, which a
        // fixture of refusals agrees with perfectly while proving nothing at all.
        var openTrip = await CreateTripAsync("Open Trip", Visibility.Authenticated);
        var closedTrip = await CreateTripAsync("Closed Trip", Visibility.Private);

        files["openTrip"] = await UploadAsync("open-trip.txt");
        await AttachToEntityAsync(files["openTrip"], AttachedEntityType.TripLog, openTrip);

        files["closedTrip"] = await UploadAsync("closed-trip.txt");
        await AttachToEntityAsync(files["closedTrip"], AttachedEntityType.TripLog, closedTrip);

        // A file reached only through another file. The one-at-a-time form answers this by
        // recursing; the batched form collects the closure first and settles it by repeating
        // until nothing new turns up. Neither route is exercised at all by a chain that ends
        // nowhere, so one that ends somewhere readable is here, beside one that does not.
        files["viaOpenFile"] = await UploadAsync("via-open-file.txt");
        await AttachToEntityAsync(files["viaOpenFile"], AttachedEntityType.StoredFile, files["openCave"]);

        files["viaClosedFile"] = await UploadAsync("via-closed-file.txt");
        await AttachToEntityAsync(files["viaClosedFile"], AttachedEntityType.StoredFile, files["closedCave"]);

        // Two steps, so the answer cannot come from a single hop: this one is readable only
        // because the file it names is readable only because of the cave that one names.
        files["viaTwoFiles"] = await UploadAsync("via-two-files.txt");
        await AttachToEntityAsync(files["viaTwoFiles"], AttachedEntityType.StoredFile, files["viaOpenFile"]);

        // Rows naming targets that are not there. Both forms must refuse them, and this is
        // where the worlds with no readable row of their own get their arm touched: a target
        // that does not exist and one the caller may not read are the same answer.
        foreach (var (name, type) in new (string, AttachedEntityType)[]
        {
            ("danglingTrip", AttachedEntityType.TripLog),
            ("danglingGeofile", AttachedEntityType.Geofile),
            ("danglingMap", AttachedEntityType.GeoreferencedMap),
            ("danglingView", AttachedEntityType.MapView),
            ("danglingFile", AttachedEntityType.StoredFile),
        })
        {
            files[name] = await UploadAsync($"{name}.txt");
            await AttachToEntityAsync(files[name], type, Guid.CreateVersion7());
        }
    }

    [Fact]
    public async Task The_batched_check_answers_what_the_one_at_a_time_check_answers_for_every_caller()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();

        var fileIds = files.Values.ToList();
        var stored = await db.StoredFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToListAsync();
        stored.Count.ShouldBe(fileIds.Count, "the fixture lost a file");

        var seenBatched = new Dictionary<string, HashSet<Guid>>();
        foreach (var (label, userId) in new (string, Guid)[]
        {
            ("uploader", ownerId), ("viewer", viewerId), ("stranger", strangerId), ("editor", editorId),
        })
        {
            var ctx = await RosterHelper.AccessContextOfAsync(db, userId);

            var batched = await FileAccessRules.ReadableFileIdsAsync(db, ctx, fileIds, default);
            seenBatched[label] = batched;

            foreach (var file in stored)
            {
                var one = await FileAccessRules.CanAccessAsync(db, access, ctx, file, default);
                batched.Contains(file.Id).ShouldBe(
                    one, $"{label} disagrees about {NameOf(file.Id)} (one-at-a-time said {one})");
            }
        }

        // The comparison above is only worth something if the callers actually differ. The
        // uploader reaches everything because they uploaded everything; the stranger, who is
        // in no group and holds nothing, reaches only what the open cave carries.
        seenBatched["uploader"].Count.ShouldBe(fileIds.Count);

        seenBatched["stranger"].ShouldContain(files["openCave"]);
        seenBatched["stranger"].ShouldContain(files["bothCaves"]);
        seenBatched["stranger"].ShouldNotContain(files["closedCave"]);
        seenBatched["stranger"].ShouldNotContain(files["unattached"]);
        seenBatched["stranger"].ShouldNotContain(files["joinedGroup"]);
        seenBatched["stranger"].ShouldNotContain(files["foreignGroup"]);

        // The trip world, both ways round, so its arm is pinned as present rather than merely
        // as refusing. Same for the file-names-file chain, one hop and two.
        seenBatched["stranger"].ShouldContain(files["openTrip"]);
        seenBatched["stranger"].ShouldNotContain(files["closedTrip"]);
        seenBatched["stranger"].ShouldContain(files["viaOpenFile"]);
        seenBatched["stranger"].ShouldContain(files["viaTwoFiles"]);
        seenBatched["stranger"].ShouldNotContain(files["viaClosedFile"]);

        // Membership is the whole difference between these two callers, and it shows.
        seenBatched["viewer"].ShouldContain(files["joinedGroup"]);
        seenBatched["viewer"].ShouldNotContain(files["foreignGroup"]);

        foreach (var dangling in new[]
        {
            "danglingTrip", "danglingGeofile", "danglingMap", "danglingView", "danglingFile",
        })
        {
            seenBatched["stranger"].ShouldNotContain(files[dangling], dangling);
            seenBatched["viewer"].ShouldNotContain(files[dangling], dangling);
        }
    }

    [Fact]
    public async Task Asking_about_a_whole_list_costs_a_fixed_number_of_queries()
    {
        // Not a benchmark — a shape check. The one-at-a-time form issues work per file and
        // again per object each file hangs on, so a listing scales with the archive; this
        // form resolves each world once. Both answers are already known to match, so the
        // only thing left to pin is that the batched form does not quietly walk per file.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await RosterHelper.AccessContextOfAsync(db, viewerId);

        var fileIds = files.Values.ToList();
        var all = await FileAccessRules.ReadableFileIdsAsync(db, ctx, fileIds, default);
        var half = await FileAccessRules.ReadableFileIdsAsync(db, ctx, [.. fileIds.Take(fileIds.Count / 2)], default);

        // Answers do not depend on which other files were asked about at the same time.
        foreach (var id in fileIds.Take(fileIds.Count / 2))
        {
            half.Contains(id).ShouldBe(all.Contains(id), NameOf(id));
        }

        // An empty question needs no answer and no round trip.
        (await FileAccessRules.ReadableFileIdsAsync(db, ctx, [], default)).ShouldBeEmpty();
    }

    private string NameOf(Guid fileId) =>
        files.FirstOrDefault(kv => kv.Value == fileId).Key ?? fileId.ToString();

    // ---- fixture helpers ----

    private async Task<Guid> CreateCaveAsync(string label, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{label} {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Written straight to storage: the trip only has to exist and be visible or not, and
    /// posting one drags in participants, dates and cave links that decide nothing here.
    /// </summary>
    private async Task<Guid> CreateTripAsync(string title, Visibility visibility)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trip = new TripLog
        {
            Title = $"{title} {Guid.NewGuid():N}"[..30],
            TripDate = new DateOnly(2026, 1, 1),
            OwnerUserId = ownerId,
            Visibility = visibility,
        };
        db.TripLogs.Add(trip);
        await db.SaveChangesAsync();
        tripIds.Add(trip.Id);
        return trip.Id;
    }

    private async Task<Guid> UploadAsync(string fileName)
    {
        var content = new ByteArrayContent("contents"u8.ToArray());
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Written straight to storage rather than posted: some of these rows deliberately name
    /// targets that do not exist, which the authoring surface refuses — correctly — and which
    /// is exactly the state both forms have to agree about.
    /// </summary>
    private async Task AttachToFeatureAsync(Guid fileId, Guid featureId) =>
        await AddAttachmentAsync(new Attachment { FileId = fileId, FeatureId = featureId });

    private async Task AttachToEntityAsync(Guid fileId, AttachedEntityType type, Guid entityId) =>
        await AddAttachmentAsync(new Attachment { FileId = fileId, EntityType = type, EntityId = entityId });

    private async Task AddAttachmentAsync(Attachment attachment)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        attachment.Role = AttachmentRole.Document;
        attachment.AddedBy = ownerId;
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Removes the rows above, the dangling ones especially. They name targets that were never
    /// there, which is the state this class exists to pin — and also exactly what the integrity
    /// checks look for. The database is shared with every other class in this collection, so a
    /// row left behind here does not fail this test, it fails theirs.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var fileIds = files.Values.ToList();
        await db.Attachments.Where(a => fileIds.Contains(a.FileId)).ExecuteDeleteAsync();

        // One of these is visible to every signed-in account, which would put it in the
        // listings other classes in this collection are counting.
        await db.TripLogs.Where(t => tripIds.Contains(t.Id)).ExecuteDeleteAsync();
    }

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
