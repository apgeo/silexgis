// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Documents;

namespace SilexGis.Api.Tests;

/// <summary>
/// A write-up asked for as a portable document: a trip's, a trip's with its reader's map, and a
/// camp's.
///
/// The application writes one format itself and hands that to the installation's optional
/// conversion service for the other, inside the request. So there are three installations to
/// tell apart, and each test names the one it stands in: one whose service answers, one that
/// runs none, and one whose service is there and does not answer. The last two are different
/// refusals on purpose — the first is a fact about the installation that the page reads in
/// advance so as not to offer the choice at all, the second a fact about one attempt.
///
/// The service is stood in for throughout. What is under test is what this application does
/// with a converter, not whether an office suite can lay out a page.
/// </summary>
public sealed class WriteUpPdfTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string PdfUrlSuffix = "format=pdf";

    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly StandInConverter converter = new();

    private HttpClient owner = null!;   // Editor — owns the trip and the camp
    private HttpClient reader = null!;  // Viewer — holds nothing over a private trip

    public WriteUpPdfTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-writeup-pdf-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            Settings("converting"),
            services => services.AddSingleton<IDocumentConverter>(converter));
    }

    public async Task InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OwnerEmail);
        owner = await AuthHelper.BearerClientAsync(factory, OwnerEmail);

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, ReaderEmail);
        reader = await AuthHelper.BearerClientAsync(factory, ReaderEmail);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    private string OwnerEmail => $"wpdf-own-{suffix}@t.local";

    private string ReaderEmail => $"wpdf-read-{suffix}@t.local";

    /// <summary>
    /// With a conversion service, each of the three downloads answers a portable document — and
    /// what was handed to the service was the word-processor document this application built.
    /// </summary>
    [Fact]
    public async Task A_write_up_asked_for_as_a_pdf_is_the_generated_document_passed_through_the_converter()
    {
        var tripId = await CreateTripAsync(owner, "Pdf trip", "authenticated");
        var campId = await CreateCampAsync(owner);

        using var trip = await owner.GetAsync($"/api/v1/trip-logs/{tripId}/report?{PdfUrlSuffix}");
        await ShouldBeAPdfAsync(trip, "trip-report-");

        // The service was given a word-processor document, by a name ending in its extension —
        // a converter chooses its reader by the extension, so the name is content.
        converter.LastName.ShouldNotBeNull().ShouldEndWith(".docx");
        converter.LastSourceWasAZipPackage.ShouldBeTrue();

        using var camp = await owner.GetAsync($"/api/v1/expeditions/{campId}/report?{PdfUrlSuffix}");
        await ShouldBeAPdfAsync(camp, "expedition-report-");

        // The download that carries its reader's map, with no map sent: still the document, as
        // a portable one. And with a map: the same, the picture having gone in before conversion.
        using var bare = await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report/download?{PdfUrlSuffix}", null);
        await ShouldBeAPdfAsync(bare, "trip-report-");

        using var form = new MultipartFormDataContent();
        var picture = new ByteArrayContent(SmallPng());
        picture.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(picture, "map", "map.png");
        using var mapped = await owner.PostAsync(
            $"/api/v1/trip-logs/{tripId}/report/download?{PdfUrlSuffix}", form);
        await ShouldBeAPdfAsync(mapped, "trip-report-");

        // Naming the other format, or none, is the document as it always was.
        using var word = await owner.GetAsync($"/api/v1/trip-logs/{tripId}/report?format=docx");
        word.StatusCode.ShouldBe(HttpStatusCode.OK);
        word.Content.Headers.ContentType!.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        using var plain = await owner.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        plain.Content.Headers.ContentType!.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    /// <summary>
    /// The installation says of itself whether it can do this, in the answer a page already reads
    /// about what this installation does with files — so a page can leave the choice out.
    /// </summary>
    [Fact]
    public async Task The_file_configuration_says_whether_this_installation_can_make_a_pdf()
    {
        var withOne = await owner.GetFromJsonAsync<JsonElement>("/api/v1/files/config");
        withOne.GetProperty("conversionAvailable").GetBoolean().ShouldBeTrue();

        using var bare = WithoutAConverter();
        using var client = await AuthHelper.BearerClientAsync(bare, OwnerEmail);
        var withNone = await client.GetFromJsonAsync<JsonElement>("/api/v1/files/config");
        withNone.GetProperty("conversionAvailable").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// Without a conversion service the request is refused by a code of its own, on every one of
    /// the three downloads, and the word-processor document is still there to be had.
    /// </summary>
    [Fact]
    public async Task An_installation_without_a_converter_refuses_a_pdf_by_name_and_still_writes_the_document()
    {
        var tripId = await CreateTripAsync(owner, "No converter here", "authenticated");
        var campId = await CreateCampAsync(owner);

        using var bare = WithoutAConverter();
        using var client = await AuthHelper.BearerClientAsync(bare, OwnerEmail);

        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, $"/api/v1/trip-logs/{tripId}/report?{PdfUrlSuffix}"),
            (HttpMethod.Post, $"/api/v1/trip-logs/{tripId}/report/download?{PdfUrlSuffix}"),
            (HttpMethod.Get, $"/api/v1/expeditions/{campId}/report?{PdfUrlSuffix}"),
        })
        {
            using var request = new HttpRequestMessage(method, url);
            using var refused = await client.SendAsync(request);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, url);
            (await CodeOfAsync(refused)).ShouldBe("report.pdf_unavailable", url);
        }

        using var word = await client.GetAsync($"/api/v1/trip-logs/{tripId}/report");
        word.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A conversion service that is deployed and does not answer is refused within the time a
    /// person is kept waiting — a different refusal from an installation that runs none.
    /// </summary>
    [Fact]
    public async Task A_converter_that_does_not_answer_is_refused_in_bounded_time_and_by_another_name()
    {
        var tripId = await CreateTripAsync(owner, "Slow converter", "authenticated");
        var campId = await CreateCampAsync(owner);

        var silent = new StandInConverter { NeverAnswers = true };
        var settings = Settings("silent");
        settings["Reports:PdfTimeoutSeconds"] = "1";
        using var slow = new SilexGisApiFactory(
            postgres.ConnectionString, settings, services => services.AddSingleton<IDocumentConverter>(silent));
        using var client = await AuthHelper.BearerClientAsync(slow, OwnerEmail);

        foreach (var url in new[]
        {
            $"/api/v1/trip-logs/{tripId}/report?{PdfUrlSuffix}",
            $"/api/v1/expeditions/{campId}/report?{PdfUrlSuffix}",
        })
        {
            var waited = Stopwatch.StartNew();
            using var refused = await client.GetAsync(url);
            waited.Stop();

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, url);
            (await CodeOfAsync(refused)).ShouldBe("report.pdf_no_answer", url);

            // The stand-in would wait for ever. One second of patience was configured; the
            // margin is for building the document and for a busy machine, not for the wait.
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20), url);
        }

        silent.Asked.ShouldBe(2);
    }

    /// <summary>
    /// A service that answers without a portable document — a refusal of the bytes, or a success
    /// status carrying something else — is neither a missing service nor a silent one.
    /// </summary>
    [Fact]
    public async Task A_converter_that_answers_without_a_pdf_is_a_third_refusal()
    {
        var tripId = await CreateTripAsync(owner, "Refused by the converter", "authenticated");

        converter.Refuses = true;
        try
        {
            using var refused = await owner.GetAsync($"/api/v1/trip-logs/{tripId}/report?{PdfUrlSuffix}");
            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await CodeOfAsync(refused)).ShouldBe("report.pdf_refused");
        }
        finally
        {
            converter.Refuses = false;
        }

        // An error page answered with a success status is not a document, whatever the status says.
        converter.Produce = Encoding.UTF8.GetBytes("<html><body>Bad gateway</body></html>");
        try
        {
            using var notOne = await owner.GetAsync($"/api/v1/trip-logs/{tripId}/report?{PdfUrlSuffix}");
            notOne.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await CodeOfAsync(notOne)).ShouldBe("report.pdf_refused");
        }
        finally
        {
            converter.Produce = StandInConverter.APdf;
        }
    }

    /// <summary>
    /// Who is refused, and how, is the download's own ladder: asking for another format opens
    /// nothing and tells nobody anything the plain download would not.
    /// </summary>
    [Fact]
    public async Task Asking_for_a_pdf_is_refused_to_whoever_the_download_itself_is_refused_to()
    {
        var closedTrip = await CreateTripAsync(owner, "Private trip", "private");
        var closedCamp = await CreateCampAsync(owner, "private");

        using var anonymous = factory.CreateClient();
        foreach (var url in new[]
        {
            $"/api/v1/trip-logs/{closedTrip}/report?{PdfUrlSuffix}",
            $"/api/v1/expeditions/{closedCamp}/report?{PdfUrlSuffix}",
        })
        {
            (await anonymous.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, url);
        }

        (await anonymous.PostAsync($"/api/v1/trip-logs/{closedTrip}/report/download?{PdfUrlSuffix}", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Not readable: the same 404, by the same code, as the plain download — and before the
        // format is looked at, so a word that names no format changes nothing about the answer.
        var before = converter.Asked;
        foreach (var format in new[] { "pdf", "porridge" })
        {
            using var trip = await reader.GetAsync($"/api/v1/trip-logs/{closedTrip}/report?format={format}");
            trip.StatusCode.ShouldBe(HttpStatusCode.NotFound, format);
            (await CodeOfAsync(trip)).ShouldBe("trip_log.not_found", format);

            using var withMap = await reader.PostAsync(
                $"/api/v1/trip-logs/{closedTrip}/report/download?format={format}", null);
            withMap.StatusCode.ShouldBe(HttpStatusCode.NotFound, format);
            (await CodeOfAsync(withMap)).ShouldBe("trip_log.not_found", format);

            using var camp = await reader.GetAsync($"/api/v1/expeditions/{closedCamp}/report?format={format}");
            camp.StatusCode.ShouldBe(HttpStatusCode.NotFound, format);
            (await CodeOfAsync(camp)).ShouldBe("expedition.not_found", format);
        }

        // Nothing was sent to the service on behalf of somebody who was refused.
        converter.Asked.ShouldBe(before);

        // And the entitled caller, over the same fixture, gets the document.
        using var entitled = await owner.GetAsync($"/api/v1/trip-logs/{closedTrip}/report?{PdfUrlSuffix}");
        await ShouldBeAPdfAsync(entitled, "trip-report-");
    }

    /// <summary>
    /// A word that names no format is refused by name, for somebody who may read the trip, and
    /// nothing is built or converted for it.
    /// </summary>
    [Fact]
    public async Task A_word_that_names_no_format_is_refused_by_name()
    {
        var tripId = await CreateTripAsync(owner, "Odd format", "authenticated");
        var campId = await CreateCampAsync(owner);
        var before = converter.Asked;

        foreach (var url in new[]
        {
            $"/api/v1/trip-logs/{tripId}/report?format=porridge",
            $"/api/v1/trip-logs/{tripId}/report?format=1",
            $"/api/v1/expeditions/{campId}/report?format=porridge",
        })
        {
            using var refused = await owner.GetAsync(url);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, url);
            (await CodeOfAsync(refused)).ShouldBe("report.format_invalid", url);
        }

        converter.Asked.ShouldBe(before);
    }

    /// <summary>
    /// The copy filed against a trip or a camp stays the word-processor document: a request to
    /// file one as a portable document is refused rather than quietly filed as the other.
    /// </summary>
    [Fact]
    public async Task The_filed_copy_stays_a_word_document_and_a_request_to_file_a_pdf_is_refused()
    {
        var tripId = await CreateTripAsync(owner, "Filed as Word", "authenticated");
        var campId = await CreateCampAsync(owner);
        var before = converter.Asked;

        foreach (var (url, entityType, id) in new[]
        {
            ($"/api/v1/trip-logs/{tripId}/report", "tripLog", tripId),
            ($"/api/v1/expeditions/{campId}/report", "expedition", campId),
        })
        {
            using var refused = await owner.PostAsync($"{url}?{PdfUrlSuffix}", null);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, url);
            (await CodeOfAsync(refused)).ShouldBe("report.pdf_not_filed", url);
            (await ReportsOnAsync(owner, entityType, id)).ShouldBeEmpty();

            using var kept = await owner.PostAsync(url, null);
            kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
            var filed = await ReportsOnAsync(owner, entityType, id);
            filed.Count.ShouldBe(1);
            filed[0].ShouldEndWith(".docx");
        }

        // Filing a write-up never goes near the conversion service.
        converter.Asked.ShouldBe(before);
    }

    private Dictionary<string, string?> Settings(string name) => new()
    {
        ["Files:Root"] = Path.Combine(filesRoot, name),
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    /// <summary>The ordinary installation: no conversion service, and the real client saying so.</summary>
    private SilexGisApiFactory WithoutAConverter() =>
        new(postgres.ConnectionString, Settings("bare"), ownHost: true);

    private static async Task ShouldBeAPdfAsync(HttpResponseMessage response, string namePrefix)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var name = response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().Trim('"');
        name.ShouldStartWith(namePrefix);
        name.ShouldEndWith(".pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("%PDF");
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var parsed = JsonDocument.Parse(body);
        return parsed.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<List<string>> ReportsOnAsync(HttpClient client, string entityType, Guid id)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType={entityType}&entityId={id}");
        return [.. attachments.EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "report")
            .Select(a => a.GetProperty("file").GetProperty("originalName").GetString() ?? string.Empty)];
    }

    private static async Task<Guid> CreateTripAsync(HttpClient client, string title, string visibility)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-03",
            description = "Written up to be converted.",
            participants = Array.Empty<object>(),
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCampAsync(HttpClient client, string visibility = "authenticated")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Pdf camp {suffix} {Guid.NewGuid():N}"[..40],
            description = "A camp to be converted.",
            startDate = "2026-07-01",
            endDate = "2026-07-14",
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A small picture standing in for the map a reader's browser drew, as a PNG.</summary>
    private static byte[] SmallPng()
    {
        using var image = new MagickImage(MagickColors.WhiteSmoke, 120, 80);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>
    /// Stands in for the office suite. It answers with a portable document unless told to do
    /// one of the three other things a real service does: refuse the bytes, answer with
    /// something that is not a document, or not answer at all.
    /// </summary>
    private sealed class StandInConverter : IDocumentConverter
    {
        public static readonly byte[] APdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% a stand-in, not a document\n%%EOF\n");

        private int asked;

        public byte[] Produce { get; set; } = APdf;

        public bool Refuses { get; set; }

        public bool NeverAnswers { get; set; }

        public int Asked => Volatile.Read(ref asked);

        public string? LastName { get; private set; }

        public bool LastSourceWasAZipPackage { get; private set; }

        public bool IsConfigured => true;

        public async Task ConvertToPortableAsync(
            Stream source, string originalName, Stream destination, CancellationToken ct = default)
        {
            Interlocked.Increment(ref asked);
            if (NeverAnswers)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            // Read the way a real converter would, so nothing passes because nobody opened it.
            using var given = new MemoryStream();
            await source.CopyToAsync(given, ct);
            var bytes = given.ToArray();
            LastName = originalName;
            LastSourceWasAZipPackage = bytes.Length > 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K';

            if (Refuses)
            {
                throw new DocumentConversionException("The stand-in converter refuses these bytes.");
            }

            await destination.WriteAsync(Produce, ct);
        }
    }
}
