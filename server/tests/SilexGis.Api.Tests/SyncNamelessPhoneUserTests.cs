// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A phone user an upload names — as who took a photo, started a recording or made a scan —
/// without the phone ever having described them: recorded at once, nameless, for the roster's
/// keeper to see waiting; filled in by a later description rather than duplicated; recorded once
/// however often it is named; and never touched when it is already on record, whoever's it is.
/// </summary>
/// <remarks>
/// Two accounts, each with a phone and a set over the same cave. Every phone user here is a fresh
/// identifier, so what a test finds on record is what that test's uploads made.
/// </remarks>
public sealed class SyncNamelessPhoneUserTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient other = null!;
    private Guid ownerId;
    private Guid otherId;
    private Guid cave;
    private Guid ownerSet;
    private Guid otherSet;

    public SyncNamelessPhoneUserTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-nameless-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // The trips the recordings become are made by a job tested on its own.
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"npu-own-{marker}@t.local");
        otherId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"npu-oth-{marker}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"npu-own-{marker}@t.local");
        other = await AuthHelper.BearerClientAsync(factory, $"npu-oth-{marker}@t.local");

        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Nameless {marker}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        cave = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        ownerSet = await CreateSetAsync(owner);
        otherSet = await CreateSetAsync(other);
    }

    [Fact]
    public async Task A_photo_naming_a_phone_user_never_described_lists_it_nameless_until_a_description_fills_it_in()
    {
        var phoneUser = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var bytes = Corpus.GeotaggedJpeg(45.41, 25.71);
        var staged = await StageAsync(owner, "IMG_31.jpg", bytes);

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var answer = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { new { id = documentId, fileName = "photo.jpg", sha256 = Hash(bytes), stagedUploadId = staged, createdByDeviceUserId = phoneUser } },
            documentLinks = new[] { new { documentId, target = new { kind = "feature", id = cave }, deleted = false } },
        });
        answer.GetProperty("documents")[0].GetProperty("status").GetString().ShouldBe("created");

        var person = await PersonAsync(phoneUser);
        person.ShouldNotBeNull();
        (person.Username, person.FirstName, person.LastName, person.CaverId).ShouldBe((null, null, null, (Guid?)null));
        person.UploadedByUserId.ShouldBe(ownerId);
        person.FirstSeenAt.ShouldBeGreaterThan(before);

        // The roster's keeper sees it waiting, with who sent it and when, and nothing more.
        var listed = await PhoneUserAsync(owner, phoneUser);
        listed.GetProperty("username").ValueKind.ShouldBe(JsonValueKind.Null);
        listed.GetProperty("caverId").ValueKind.ShouldBe(JsonValueKind.Null);
        listed.GetProperty("sentByUserId").GetGuid().ShouldBe(ownerId);
        var sentBy = listed.GetProperty("sentByName").GetString();
        sentBy.ShouldNotBeNullOrWhiteSpace();
        sentBy.ShouldNotContain("@");
        listed.GetProperty("firstSeenAt").GetDateTimeOffset().ShouldBe(person.FirstSeenAt, TimeSpan.FromMilliseconds(1));

        // A later description fills the names into the same record.
        var described = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { new { deviceUserId = phoneUser, username = "ionp", firstName = "Ion", lastName = "Popescu" } },
        });
        described.GetProperty("people")[0].GetProperty("status").GetString().ShouldBe("updated");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.DevicePersons.AsNoTracking().Where(p => p.DeviceUserId == phoneUser).ToListAsync();
        rows.Count.ShouldBe(1);
        (rows[0].Username, rows[0].FirstName, rows[0].LastName).ShouldBe(("ionp", "Ion", "Popescu"));
        rows[0].FirstSeenAt.ShouldBe(person.FirstSeenAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_recording_and_its_scans_naming_one_undescribed_phone_user_list_it_once()
    {
        var phoneUser = Guid.CreateVersion7();
        var recording = Guid.CreateVersion7();
        var answer = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = new[] { Recording(recording, phoneUser) },
            tripPoints = new[] { Scan(Guid.CreateVersion7(), recording, 10, phoneUser), Scan(Guid.CreateVersion7(), recording, 20, phoneUser) },
        });
        answer.GetProperty("tripRecordings")[0].GetProperty("status").GetString().ShouldBe("created");
        answer.GetProperty("tripPoints").EnumerateArray().Select(p => p.GetProperty("status").GetString())
            .ShouldBe(["created", "created"]);

        var first = await PersonAsync(phoneUser);
        first.ShouldNotBeNull();
        first.Username.ShouldBeNull();
        first.UploadedByUserId.ShouldBe(ownerId);

        // Named again by a later upload, it is neither recorded again nor changed.
        var second = Guid.CreateVersion7();
        await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = new[] { Recording(second, phoneUser) },
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.DevicePersons.AsNoTracking().Where(p => p.DeviceUserId == phoneUser).ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].UpdatedAt.ShouldBe(first.UpdatedAt);
        (await db.TripRecordings.AsNoTracking().CountAsync(r => r.CreatedByDeviceUserId == phoneUser)).ShouldBe(2);
    }

    [Fact]
    public async Task Another_accounts_phone_user_is_never_touched_by_being_named()
    {
        // The other account's phone describes its user.
        var theirs = Guid.CreateVersion7();
        (await UploadAsync(other, otherSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new[] { new { deviceUserId = theirs, username = "theirs", firstName = "Ana" } },
        })).GetProperty("people")[0].GetProperty("status").GetString().ShouldBe("created");
        var described = await PersonAsync(theirs);
        described.ShouldNotBeNull();

        // This account's phone names it on a recording and on a photo.
        var bytes = Corpus.GeotaggedJpeg(45.42, 25.72);
        var staged = await StageAsync(owner, "IMG_32.jpg", bytes);
        var documentId = Guid.CreateVersion7();
        var answer = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { new { id = documentId, fileName = "photo.jpg", sha256 = Hash(bytes), stagedUploadId = staged, createdByDeviceUserId = theirs } },
            documentLinks = new[] { new { documentId, target = new { kind = "feature", id = cave }, deleted = false } },
            tripRecordings = new[] { Recording(Guid.CreateVersion7(), theirs) },
        });
        answer.GetProperty("documents")[0].GetProperty("status").GetString().ShouldBe("created");
        answer.GetProperty("tripRecordings")[0].GetProperty("status").GetString().ShouldBe("created");

        var after = await PersonAsync(theirs);
        after.ShouldNotBeNull();
        (after.Username, after.FirstName, after.UploadedByUserId, after.LastSeenAt, after.UpdatedAt)
            .ShouldBe((described.Username, described.FirstName, described.UploadedByUserId, described.LastSeenAt, described.UpdatedAt));
        after.UploadedByUserId.ShouldBe(otherId);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        other?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    private object Recording(Guid id, Guid author) => new
    {
        id,
        caveId = cave,
        title = "Invented trip",
        startedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        endedAt = new DateTimeOffset(2026, 9, 12, 16, 0, 0, TimeSpan.Zero),
        createdByDeviceUserId = author,
    };

    private static object Scan(Guid id, Guid recordingId, int minutes, Guid author) => new
    {
        id,
        recordingId,
        scannedAt = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes),
        notes = $"scan {minutes}",
        createdByDeviceUserId = author,
    };

    private async Task<DevicePerson?> PersonAsync(Guid deviceUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.DevicePersons.AsNoTracking().FirstOrDefaultAsync(p => p.DeviceUserId == deviceUserId);
    }

    private static async Task<JsonElement> PhoneUserAsync(HttpClient client, Guid deviceUserId)
    {
        for (var page = 1; page < 50; page++)
        {
            var answer = await client.GetFromJsonAsync<JsonElement>($"/api/v1/phone-users/?page={page}&pageSize=100");
            var items = answer.GetProperty("items").EnumerateArray().ToList();
            if (items.FirstOrDefault(i => i.GetProperty("deviceUserId").GetGuid() == deviceUserId) is { ValueKind: JsonValueKind.Object } found)
            {
                return found;
            }

            if (items.Count < 100)
            {
                break;
            }
        }

        throw new ShouldAssertException($"Phone user {deviceUserId} is not listed.");
    }

    private async Task<Guid> CreateSetAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            uploadVisibility = "private",
            rootFeatureIds = new[] { cave },
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid setId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{setId}/upload", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<Guid> StageAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var opened = await client.PostAsJsonAsync("/api/v1/files/uploads", new { fileName, sizeBytes = bytes.Length, stageForSync = true });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }
}
