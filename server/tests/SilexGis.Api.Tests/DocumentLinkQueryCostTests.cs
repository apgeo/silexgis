// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A link listing that shows documents decides who may read them for the whole set at once. It
/// used to walk each document's access on its own, several queries per document on every listing,
/// so a cave with many linked documents cost a panel load in proportion. The listing's query count
/// is now the same whether it shows two documents or twelve.
/// </summary>
public sealed class DocumentLinkQueryCostTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly CommandCounter commands = new();

    private HttpClient owner = null!;

    public DocumentLinkQueryCostTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-doclinkcost-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(commands));
            });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dlc-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"dlc-own-{suffix}@t.local");
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
    public async Task A_listing_of_a_link_to_documents_costs_the_same_queries_for_two_documents_as_for_twelve()
    {
        var few = await CaveWithLinkedDocumentsAsync(2);
        var many = await CaveWithLinkedDocumentsAsync(12);

        // Read once each first, so neither count carries a first request's one-off reads.
        await ListAsync(few, 2);
        await ListAsync(many, 12);

        commands.Reset();
        await ListAsync(few, 2);
        var forFew = commands.Count;

        commands.Reset();
        await ListAsync(many, 12);
        var forMany = commands.Count;

        forMany.ShouldBe(forFew, $"two documents cost {forFew} queries and twelve cost {forMany}");
    }

    private async Task ListAsync(Guid caveId, int expected)
    {
        var page = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/for-target?type=feature&id={caveId}&pageSize=100");
        var titles = page.GetProperty("items").EnumerateArray()
            .SelectMany(link => link.GetProperty("members").EnumerateArray())
            .Count(member => member.GetProperty("targetType").GetString() == "document"
                && member.TryGetProperty("display", out var display) && display.ValueKind == JsonValueKind.Object);
        titles.ShouldBe(expected);
    }

    private async Task<Guid> CaveWithLinkedDocumentsAsync(int documents)
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            caveTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()
                .CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var cave = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Linked {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "authenticated",
        });
        var cavePayload = await cave.Content.ReadAsStringAsync();
        cave.StatusCode.ShouldBe(HttpStatusCode.Created, cavePayload);
        var caveId = JsonDocument.Parse(cavePayload).RootElement.GetProperty("id").GetGuid();

        // One link holding every document, so the two listings differ in the number of documents
        // and in nothing else.
        var members = new List<object> { new { targetType = "feature", targetId = caveId, isMain = false } };
        for (var i = 0; i < documents; i++)
        {
            members.Add(new { targetType = "document", targetId = await UploadDocumentAsync(), isMain = false });
        }

        var link = await owner.PostAsJsonAsync("/api/v1/reslinks", new { members });
        link.StatusCode.ShouldBe(HttpStatusCode.Created, await link.Content.ReadAsStringAsync());
        return caveId;
    }

    private async Task<Guid> UploadDocumentAsync()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"note {Guid.NewGuid():N}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", $"{Guid.NewGuid():N}.txt" } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        return await db.DocumentVersions.AsNoTracking()
            .Where(v => v.Id == file.DocumentVersionId).Select(v => v.DocumentId).FirstAsync();
    }

    /// <summary>Every command a host sends to the database, counted and nothing else.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Reset() => Interlocked.Exchange(ref count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }
    }
}
