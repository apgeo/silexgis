// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// JSON answers are compressed by the API when the caller asks for it; nothing else is. The
/// requests are made over plain HTTP, which is the only scheme compression is enabled for.
/// </summary>
public sealed class ResponseCompressionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient admin = null!;

    public ResponseCompressionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rc-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rc-adm-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin.Dispose();
        factory.Dispose();
    }

    private static HttpRequestMessage Get(string path, string? acceptEncoding)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptEncoding is not null)
        {
            request.Headers.AcceptEncoding.ParseAdd(acceptEncoding);
        }

        return request;
    }

    private static async Task<string> DecodedAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var encoding = response.Content.Headers.ContentEncoding.SingleOrDefault();
        Stream source = new MemoryStream(bytes);
        source = encoding switch
        {
            "br" => new BrotliStream(source, CompressionMode.Decompress),
            "gzip" => new GZipStream(source, CompressionMode.Decompress),
            null => source,
            _ => throw new InvalidOperationException($"Unexpected content encoding {encoding}."),
        };
        using var reader = new StreamReader(source, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Theory]
    [InlineData("br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("gzip, br", "br")]
    public async Task A_json_answer_is_compressed_for_a_caller_that_accepts_it_and_decodes_to_the_same_document(
        string accept, string expected)
    {
        var plain = await admin.SendAsync(Get("/api/v1/me", null));
        plain.StatusCode.ShouldBe(HttpStatusCode.OK);
        plain.Content.Headers.ContentEncoding.ShouldBeEmpty();
        var expectedText = await plain.Content.ReadAsStringAsync();

        var response = await admin.SendAsync(Get("/api/v1/me", accept));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldBe([expected]);
        response.Headers.Vary.ShouldContain("Accept-Encoding");
        var decoded = await DecodedAsync(response);
        using var left = JsonDocument.Parse(decoded);
        using var right = JsonDocument.Parse(expectedText);
        left.RootElement.GetRawText().ShouldBe(right.RootElement.GetRawText());
    }

    [Fact]
    public async Task A_caller_that_does_not_ask_for_compression_gets_the_plain_body()
    {
        var response = await admin.SendAsync(Get("/api/v1/me", null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        Should.NotThrow(() => JsonDocument.Parse(response.Content.ReadAsStringAsync().Result).Dispose());
    }

    // The refusal documents are written by the status-code pages, outside the handlers, so this
    // proves the middleware sits ahead of them.
    [Fact]
    public async Task A_problem_document_is_compressed_too()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.SendAsync(Get("/api/v1/me", "gzip"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Content.Headers.ContentEncoding.ShouldBe(["gzip"]);
        JsonDocument.Parse(await DecodedAsync(response)).Dispose();
    }

    // A file download is bytes the caller saves; it must arrive exactly as written, whatever the
    // caller accepts.
    [Fact]
    public async Task A_zipped_download_is_not_compressed_again()
    {
        var response = await admin.SendAsync(Get("/api/v1/export/caves?format=shapefile", "br, gzip"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(4);
        bytes[0].ShouldBe((byte)'P');
        bytes[1].ShouldBe((byte)'K');
    }

    [Fact]
    public async Task Text_that_is_not_json_is_left_alone_even_when_the_caller_accepts_compression()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.SendAsync(Get("/health/live", "br, gzip"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }
}
