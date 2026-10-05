// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// An event as the target of the rows that point at a record by kind and id — files attached to
/// it and tags on it — and what becomes of those rows when the event, or the rest of its run, is
/// deleted.
/// </summary>
/// <remarks>
/// Three accounts: the Editor who writes the events, a Viewer who is granted Read on one of them
/// and nothing else, and a Viewer who holds nothing at all. The middle one is what separates "may
/// not write it" from "cannot see it": an account that cannot see the event is answered as though
/// it did not exist, and one that can see it but may not change it is refused in so many words.
/// </remarks>
public sealed class EventPolymorphicTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private HttpClient outsider = null!;
    private Guid readerId;

    public EventPolymorphicTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-evpoly-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ep-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ep-rdr-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ep-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"ep-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"ep-rdr-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"ep-out-{suffix}@t.local");
    }

    /// <summary>
    /// The agenda, the minutes and the course material are what a club files against a date, so
    /// an event takes a file and a tag — under the event's own write right and no other. A
    /// reader of the event sees what it carries and may add nothing; an account that cannot see
    /// the event is told it is not there.
    /// </summary>
    [Fact]
    public async Task An_event_takes_a_file_and_a_tag_under_its_own_write_right()
    {
        var ev = await CreateEventAsync("Tagged evening");
        var fileId = await UploadAsync("agenda.txt");
        var tagName = $"ev-{Guid.NewGuid():N}"[..16];

        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", AttachBody(fileId, ev));
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());
        var tagged = await owner.PostAsJsonAsync("/api/v1/taggings/", TagBody(tagName, ev));
        tagged.StatusCode.ShouldBe(HttpStatusCode.Created, await tagged.Content.ReadAsStringAsync());

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attachments/?entityType=event&entityId={ev}"))
            .GetArrayLength().ShouldBe(1);
        var listedTags = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/taggings/?entityType=event&entityId={ev}");
        listedTags.GetArrayLength().ShouldBe(1);
        listedTags[0].GetProperty("tag").GetProperty("name").GetString().ShouldBe(tagName);

        // Read and nothing more: the rows are listed, and adding to them is refused outright.
        await GrantAsync(ev, readerId, AccessAction.Read);
        (await reader.GetAsync($"/api/v1/attachments/?entityType=event&entityId={ev}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync("/api/v1/attachments/", AttachBody(fileId, ev))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync("/api/v1/taggings/", TagBody("not-yours", ev))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Nothing at all on a private event: the target is invisible, and so is every answer about it.
        (await outsider.GetAsync($"/api/v1/attachments/?entityType=event&entityId={ev}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync("/api/v1/attachments/", AttachBody(fileId, ev))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync("/api/v1/taggings/", TagBody("not-yours", ev))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    private static object AttachBody(Guid fileId, Guid eventId) =>
        new { fileId, entityType = "event", entityId = eventId, role = "other", sortOrder = 0 };

    private static object TagBody(string tagName, Guid eventId) =>
        new { tagName, entityType = "event", entityId = eventId };

    private async Task<Guid> CreateEventAsync(string title, DateOnly? startDate = null, object? recurrence = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/events", new
        {
            title = $"{title} {suffix}",
            kind = "clubMeeting",
            startDate = (startDate ?? new DateOnly(2054, 10, 5)).ToString("yyyy-MM-dd"),
            endDate = (string?)null,
            place = (string?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            recurrence,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The run one event belongs to, first occurrence first.</summary>
    private async Task<List<Guid>> OccurrencesAsync(Guid eventId)
    {
        var row = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/events/{eventId}");
        var seriesId = row.GetProperty("seriesId").GetGuid();
        var page = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/events?pageSize=200&seriesId={seriesId}");
        return [.. page.GetProperty("items").EnumerateArray()
            .OrderBy(x => x.GetProperty("startDate").GetString())
            .Select(x => x.GetProperty("id").GetGuid())];
    }

    private async Task<Guid> UploadAsync(string fileName)
    {
        var content = new ByteArrayContent("an evening's own paperwork"u8.ToArray());
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// How many attachment and tagging rows still name the event — the two kinds that point at
    /// it by kind and id with no foreign key to follow. A relation cannot name an event, so there
    /// is no third count here; that arm of the shared sweep is proved on a camp.
    /// </summary>
    private async Task<(int Attachments, int Taggings)> RowsPointingAtAsync(Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (
            await db.Attachments.AsNoTracking().CountAsync(
                a => a.EntityType == AttachedEntityType.Event && a.EntityId == eventId),
            await db.Taggings.AsNoTracking().CountAsync(
                t => t.EntityType == AttachedEntityType.Event && t.EntityId == eventId));
    }

    /// <summary>A rule on one event, written straight to the table — the shape the route writes.</summary>
    private async Task GrantAsync(Guid eventId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Events,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = eventId,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        reader.Dispose();
        outsider.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
