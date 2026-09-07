// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// The survey-source archive end to end: which files may be archived against a cave, that the
/// bytes have to be the format the name claims, that a corrected source is a new revision of the
/// same document, and that the whole archive is withheld from a caller who may not place the cave.
/// </summary>
public sealed class SurveySourceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveySourceTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-srcs-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"svs-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"svs-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"svs-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"svs-read-{suffix}@t.local");
    }

    [Fact]
    public async Task A_survey_source_is_archived_listed_and_superseded_by_a_new_revision()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using var form = BuildForm("cave.svx", SurvexText());
        form.Add(new StringContent("Field notes typed up, 2024"), "description");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        var fileId = body.GetProperty("fileId").GetGuid();
        body.GetProperty("kind").GetString().ShouldBe("survexSource");
        body.GetProperty("fileName").GetString().ShouldBe("cave.svx");
        body.GetProperty("versionNumber").GetInt32().ShouldBe(1);
        body.GetProperty("description").GetString().ShouldBe("Field notes typed up, 2024");

        var listed = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources");
        listed.GetArrayLength().ShouldBe(1);
        listed[0].GetProperty("id").GetGuid().ShouldBe(id);
        listed[0].GetProperty("sizeBytes").GetInt64().ShouldBe(SurvexText().LongLength);

        // A source is stored as survey material under a media type of its own — not as a text
        // document, which is what would send it to be read for words.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
            stored.Kind.ShouldBe(FileKind.Survey);
            stored.MimeType.ShouldBe("application/x-survex");
            stored.TextExtraction.ShouldBe(TextExtractionState.NotApplicable);
        }

        // The bytes come back through the ordinary signed file URL.
        var contentUrl = listed[0].GetProperty("contentUrl").GetString()!;
        using var anonymous = factory.CreateClient();
        var delivered = await anonymous.GetAsync(contentUrl);
        delivered.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await delivered.Content.ReadAsByteArrayAsync()).ShouldBe(SurvexText());

        // A corrected source is a revision of the same document, through the machinery that owns
        // revisions — so the archive entry keeps pointing at one thread of history.
        using var revised = BuildForm("cave.svx", SurvexText("2 3 8.10 210 4"));
        var version = await owner.PostAsync($"/api/v1/files/{fileId}/versions", revised);
        version.StatusCode.ShouldBe(HttpStatusCode.Created, await version.Content.ReadAsStringAsync());

        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources");
        after.GetArrayLength().ShouldBe(1);
        after[0].GetProperty("id").GetGuid().ShouldBe(id);
        after[0].GetProperty("versionNumber").GetInt32().ShouldBe(2);
        after[0].GetProperty("fileId").GetGuid().ShouldNotBe(fileId);
        after[0].GetProperty("documentId").GetGuid().ShouldBe(body.GetProperty("documentId").GetGuid());

        (await owner.DeleteAsync($"/api/v1/survey-sources/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_file_that_is_not_what_its_name_says_is_refused_and_nothing_is_stored()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        // The same name, twice: once with the text it claims to be, once with an executable's
        // bytes. Only the extension list would let both through.
        using var honest = BuildForm("cave.svx", SurvexText());
        (await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", honest))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // What the store holds once one honest upload is in it. The refusals below are checked
        // against this rather than against zero, so a refusal that leaves its bytes behind shows up
        // here — the row count alone cannot see an orphaned blob, and an orphan is invisible for
        // ever precisely because no row points at it.
        var storedBefore = BlobCount();

        using var disguised = BuildForm("cave.svx", Executable());
        var refused = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", disguised);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("survey_source.content_mismatch");
        problem.GetProperty("detail").GetString()!.ShouldContain("text file");

        // Claiming text in the request header does not vouch for the bytes either.
        using var declared = BuildForm("cave.svx", Executable(), "text/plain");
        (await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", declared))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // One archived source, and one stored file — the refused uploads left nothing behind.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveySources.CountAsync(s => s.CaveFeatureId == caveId)).ShouldBe(1);

        // Nothing on disk either. The bytes have to be written before they can be read, so the
        // refusal is what has to take them away again.
        BlobCount().ShouldBe(storedBefore);
    }

    [Fact]
    public async Task A_name_or_a_description_longer_than_the_record_holds_is_refused_before_anything_is_stored()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        var storedBefore = BlobCount();

        // A file name a deep export path produces without anybody trying. It is stored twice — as
        // the display name and as the name the file arrived under — and it has to be answered
        // rather than handed to the database to refuse.
        using var longName = BuildForm(new string('p', 260) + ".svx", SurvexText());
        var refusedName = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", longName);
        refusedName.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refusedName.Content.ReadAsStringAsync());
        (await refusedName.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("survey_source.name_invalid");

        using var longDescription = BuildForm("cave.svx", SurvexText());
        longDescription.Add(new StringContent(new string('d', 4001)), "description");
        var refusedNote = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", longDescription);
        refusedNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refusedNote.Content.ReadAsStringAsync());
        (await refusedNote.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("survey_source.description_invalid");

        // Refused before the bytes were read, so nothing was written and nothing has to be undone.
        BlobCount().ShouldBe(storedBefore);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(0);

        // The longest name that does fit is archived, so the limit refuses what it says it refuses
        // and not everything near it.
        using var atTheLimit = BuildForm(new string('p', 251) + ".svx", SurvexText());
        (await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", atTheLimit))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>How many files the store holds, whatever any row says about them.</summary>
    private int BlobCount() =>
        Directory.Exists(filesRoot)
            ? Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories)
                .Count(f => !f.StartsWith(Path.Combine(filesRoot, "keys"), StringComparison.Ordinal))
            : 0;

    [Fact]
    public async Task A_compiled_model_is_not_a_source_and_an_empty_file_is_not_archived()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using var compiled = BuildForm("cave.lox", SurvexText());
        var wrongFormat = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", compiled);
        wrongFormat.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongFormat.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("survey_source.format_unsupported");

        using var empty = BuildForm("cave.th", []);
        var noBytes = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", empty);
        noBytes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noBytes.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("survey_source.size_invalid");

        // The log is archived as it is, unread, and is the one kind whose whole point is that it
        // is kept rather than interpreted.
        using var log = BuildForm("therion.log", Encoding.UTF8.GetBytes("therion 6.2.0\nloop closure ok\n"));
        var kept = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", log);
        kept.StatusCode.ShouldBe(HttpStatusCode.Created, await kept.Content.ReadAsStringAsync());
        (await kept.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("kind").GetString().ShouldBe("therionLog");
    }

    [Fact]
    public async Task Archiving_needs_an_account_and_write_access_to_the_cave()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using var anonymous = factory.CreateClient();
        using var unauthenticated = BuildForm("cave.th", SurvexText());
        (await anonymous.PostAsync($"/api/v1/caves/{caveId}/survey-sources", unauthenticated))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A viewer may read the cave and sees the archive, but may not add to it or remove from it.
        using var byViewer = BuildForm("cave.th", SurvexText());
        (await reader.PostAsync($"/api/v1/caves/{caveId}/survey-sources", byViewer))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var id = await ArchiveAsync(caveId, "cave.th");
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(1);
        (await reader.DeleteAsync($"/api/v1/survey-sources/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/survey-sources/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Sources_of_a_protected_cave_are_withheld_without_exact_location()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        var id = await ArchiveAsync(caveId, "secret.th");

        // The cave itself stays readable; a survey source can name where its fixed points are, so
        // it is location data and goes with the position rather than with the cave record.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(0);

        // Not disclosed by the write paths either: 404 rather than 403.
        (await reader.DeleteAsync($"/api/v1/survey-sources/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var intrusion = BuildForm("intrusion.th", SurvexText());
        (await reader.PostAsync($"/api/v1/caves/{caveId}/survey-sources", intrusion))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner never lost sight of it, and an explicit grant flips it visible.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(1);

        await GrantExactViewAsync(caveId);
        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources");
        granted.GetArrayLength().ShouldBe(1);
        granted[0].GetProperty("id").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task A_private_caves_archive_is_not_disclosed_to_outsiders()
    {
        var caveId = await CreateCaveAsync(visibility: "private", locationProtected: false);
        var id = await ArchiveAsync(caveId, "private.svx");

        (await reader.GetAsync($"/api/v1/caves/{caveId}/survey-sources"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.DeleteAsync($"/api/v1/survey-sources/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var intrusion = BuildForm("intrusion.svx", SurvexText());
        (await reader.PostAsync($"/api/v1/caves/{caveId}/survey-sources", intrusion))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-sources"))
            .GetArrayLength().ShouldBe(1);
    }

    // ---- helpers ----

    private async Task<Guid> ArchiveAsync(Guid caveId, string fileName)
    {
        using var form = BuildForm(fileName, SurvexText());
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task GrantExactViewAsync(Guid featureId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Source Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A survey source as it really arrives: characters, in the language of its tool.</summary>
    private static byte[] SurvexText(string leg = "1 2 12.50 145 -3") => Encoding.UTF8.GetBytes(
        $"*begin main\n*data normal from to tape compass clino\n{leg}\n*end main\n");

    /// <summary>The first bytes of a Linux executable, whatever the upload chooses to call it.</summary>
    private static byte[] Executable() =>
        [.. new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0 }, .. new byte[64]];

    private static MultipartFormDataContent BuildForm(
        string fileName, byte[] bytes, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
            // Temp files; best effort.
        }
    }
}
