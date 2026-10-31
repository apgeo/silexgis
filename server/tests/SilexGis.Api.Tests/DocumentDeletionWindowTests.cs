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
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The same routes on an installation that has chosen a retention window: a document deleted
/// longer ago than that is not offered, cannot be restored, and is what the sweep may remove.
/// </summary>
public sealed class DocumentDeletionWindowTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient owner = null!;

    public DocumentDeletionWindowTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-docwindow-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["DocumentRetention:RetentionDays"] = "30",
        });
    }

    public async ValueTask InitializeAsync()
    {
        var email = $"dw-own-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        owner = await AuthHelper.BearerClientAsync(factory, email);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
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

    [Fact]
    public async Task A_window_counts_down_and_a_restore_past_it_is_refused()
    {
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/documents/config"))
            .GetProperty("deletedRetentionDays").GetInt32().ShouldBe(30);

        var fresh = await UploadAsync("fresh");
        var stale = await UploadAsync("stale");
        (await owner.DeleteAsync($"/api/v1/documents/{fresh}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/api/v1/documents/{stale}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var document = await db.Documents.IgnoreQueryFilters().FirstAsync(d => d.Id == stale);
            document.DeletedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(31);
            await db.SaveChangesAsync();
        }

        var page = await owner.GetFromJsonAsync<JsonElement>("/api/v1/documents/deleted?pageSize=100");
        var rows = page.GetProperty("items").EnumerateArray().ToList();
        var row = rows.Single(i => i.GetProperty("id").GetGuid() == fresh);
        row.GetProperty("restorableUntil").GetDateTimeOffset()
            .ShouldBeGreaterThan(row.GetProperty("deletedAt").GetDateTimeOffset());
        rows.ShouldNotContain(i => i.GetProperty("id").GetGuid() == stale);

        var refused = await owner.PostAsync($"/api/v1/documents/{stale}/restore", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()
            .ShouldBe("document.restore_window_passed");
        (await owner.PostAsync($"/api/v1/documents/{fresh}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<Guid> UploadAsync(string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"{text} {Guid.NewGuid()}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", $"{text}.txt" } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        return (await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId)).DocumentId;
    }
}
