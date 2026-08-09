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
/// The words of one page, served so a selection made in a browser can be measured against the
/// text the server itself holds.
///
/// Two properties are asserted, and they pull in opposite directions on purpose. It must be
/// reachable by exactly whoever may see the page drawn — the picture already shows every one of
/// these words, so withholding them as characters would protect nothing and would only stop a
/// screen reader. And it must be reachable by nobody else: the delivery token is the whole gate,
/// so a request without one, or with one minted for another file, is answered as if the page did
/// not exist.
///
/// The third property is the one that makes an anchor honest: a page nothing has read yet says
/// so, rather than answering with empty text. A caller that could not tell those apart would
/// store a link pointing at a passage it never found.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PageTextTests : IAsyncLifetime, IDisposable
{
    private const string PageOneText = "Beyond the second sump the passage widens into a chamber.";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient anonymous = null!;

    public PageTextTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-pagetext-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"pt-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"pt-own-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task The_page_text_is_served_to_a_holder_of_the_pages_url()
    {
        var (fileId, pagesUrl) = await UploadReadableAsync();

        var response = await anonymous.GetAsync(TextUrl(pagesUrl, 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("page").GetInt32().ShouldBe(1);
        // Character for character: an offset composed against this has to mean the same thing
        // on the next request, so nothing may normalise it on the way out.
        body.GetProperty("text").GetString().ShouldBe(PageOneText);
        fileId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_page_nothing_has_read_is_not_found_rather_than_empty()
    {
        var (_, pagesUrl) = await UploadReadableAsync();

        // Page two exists as a row — the upload made it — but no reader has been over it.
        var response = await anonymous.GetAsync(TextUrl(pagesUrl, 2));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("file.page_text_not_read");
    }

    [Fact]
    public async Task A_page_that_does_not_exist_is_not_found()
    {
        var (_, pagesUrl) = await UploadReadableAsync();

        (await anonymous.GetAsync(TextUrl(pagesUrl, 9))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(TextUrl(pagesUrl, 0))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_token_the_text_is_not_reachable()
    {
        var (fileId, pagesUrl) = await UploadReadableAsync();

        // No token at all — somebody typing the route into a browser. It is answered exactly as
        // a wrong token is, rather than faulting: the absence of a token is not an exceptional
        // condition, it is the commonest way of not being allowed in.
        (await anonymous.GetAsync($"/api/v1/files/{fileId}/pages/1/text"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A token that was minted for a different file. The signature is valid; the file it
        // names is not this one, which is the case a check on validity alone would let through.
        var (otherId, otherPagesUrl) = await UploadReadableAsync();
        otherId.ShouldNotBe(fileId);
        var borrowed = new Uri(TextUrl(otherPagesUrl, 1), UriKind.Relative).ToString()
            .Replace(otherId.ToString(), fileId.ToString(), StringComparison.OrdinalIgnoreCase);
        (await anonymous.GetAsync(borrowed)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And a signed-in caller with no token is refused just the same: the gate is the token,
        // not the session.
        (await owner.GetAsync($"/api/v1/files/{fileId}/pages/1/text?token=nonsense"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        pagesUrl.ShouldNotBeNullOrEmpty();
    }

    // ---- helpers ----

    /// <summary>The text route for one page, built from the file's own delivery URL.</summary>
    private static string TextUrl(string pagesUrl, int page)
    {
        var parts = pagesUrl.Split('?');
        return $"{parts[0].Replace("/content", $"/pages/{page}/text")}?{parts[1]}";
    }

    /// <summary>
    /// Uploads a two-page document and writes the words of its first page, the way a reader
    /// would. The rows are derived data with no write endpoint, so the fixture stands in for
    /// the extraction job rather than waiting on it.
    /// </summary>
    private async Task<(Guid FileId, string PagesUrl)> UploadReadableAsync()
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% two pages, contents irrelevant here\n%%EOF\n");
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(part, "file", "report.pdf");

        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files?allowDuplicate=true", content);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var file = await db.StoredFiles.SingleAsync(f => f.Id == fileId);
            file.PageCount = 2;
            db.DocumentPages.RemoveRange(db.DocumentPages.Where(p => p.FileId == fileId));
            db.DocumentPages.Add(new DocumentPage
            {
                FileId = fileId,
                PageNumber = 1,
                Text = PageOneText,
                Extractor = "test",
                ExtractorVersion = 1,
            });
            // Read by nothing: the row exists because the file has the page, and its text is
            // absent rather than empty.
            db.DocumentPages.Add(new DocumentPage { FileId = fileId, PageNumber = 2 });
            await db.SaveChangesAsync();
        }

        var seen = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/files/{fileId}")).Content.ReadAsStringAsync()).RootElement;
        var pagesUrl = seen.GetProperty("pagesUrl").GetString();
        pagesUrl.ShouldNotBeNull();
        return (fileId, pagesUrl);
    }

    public async Task DisposeAsync()
    {
        owner.Dispose();
        anonymous.Dispose();
        await factory.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
