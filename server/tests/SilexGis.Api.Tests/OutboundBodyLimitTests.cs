// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Map;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Basemap;
using SilexGis.Infrastructure.Catalogue;
using SilexGis.Infrastructure.Documents.Conversion;
using SilexGis.Infrastructure.PhotoLibraries;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Answers from other servers whose body stalls or is cut off part-way.
///
/// <para>
/// Every client here asks with the headers read and the body still open, and the client's own time
/// limit stops counting once the headers arrive. So the two failures that matter are the ones that
/// happen after that point: a body that stops arriving without the connection closing, which used
/// to hold whatever was reading — a request, a background refresh holding a permit, a job on the
/// one-at-a-time lane — until the process restarted; and a connection dropped in the middle of a
/// body, which used to escape as an unexpected error rather than the far side being unavailable.
/// </para>
/// <para>
/// No database and no host: each client is built over a stub whose body is scripted. Every address
/// is on the reserved <c>.invalid</c> domain and every payload is invented.
/// </para>
/// </summary>
public sealed class OutboundBodyLimitTests : IDisposable
{
    /// <summary>Long enough for the shortest limit any of these clients allows, short of a hang.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static Envelope Rectangle => new(x1: 21.5, x2: 24.25, y1: 45.125, y2: 46.75);

    private readonly string directory = Path.Combine(
        TestScratch.Root, "outbound-body-" + Guid.NewGuid().ToString("N")[..8]);

    public OutboundBodyLimitTests() => Directory.CreateDirectory(directory);

    // ------------------------------------------------------------------------------ photo libraries

    [Fact]
    public async Task An_Immich_position_reading_whose_body_stalls_ends_as_unavailable_within_the_call_limit()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Stalls("[")));
        var immich = Immich(stub);

        var clock = Stopwatch.StartNew();
        var failure = await Should.ThrowAsync<PhotoLibraryException>(
            () => immich.PhotosInAsync(Rectangle, 100, default).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(PhotoLibraryException.UnavailableCode);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task An_Immich_position_reading_cut_off_part_way_is_unavailable_rather_than_unexpected()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Drops("""[{"id":"11111111-1111-4111-8111-111111111111","lat":45.5,""")));

        var failure = await Should.ThrowAsync<PhotoLibraryException>(
            () => Immich(stub).PhotosInAsync(Rectangle, 100, default).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    [Fact]
    public async Task An_Immich_album_list_cut_off_part_way_is_unavailable_rather_than_unexpected()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Drops("""[{"id":""")));

        var failure = await Should.ThrowAsync<PhotoLibraryException>(
            () => Immich(stub).AlbumsAsync(default).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    [Fact]
    public async Task A_PhotoPrism_answer_cut_off_part_way_is_unavailable_rather_than_unexpected()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Drops("""[{"UID":""")));

        var failure = await Should.ThrowAsync<PhotoLibraryException>(
            () => Prism(stub).PhotosInAsync(Rectangle, 100, default).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    [Fact]
    public async Task A_PhotoPrism_answer_whose_body_stalls_ends_as_unavailable_within_the_call_limit()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Stalls("[")));

        var clock = Stopwatch.StartNew();
        var failure = await Should.ThrowAsync<PhotoLibraryException>(
            () => Prism(stub).PhotosInAsync(Rectangle, 100, default).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(PhotoLibraryException.UnavailableCode);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_photo_library_read_the_caller_abandons_is_still_a_cancellation()
    {
        // The positive case beside the four above: the same stalled body, but the caller gives up
        // first — a closed tab. That says nothing about the library and must not be reported as
        // the library being unavailable.
        var stub = new Stub(_ => JsonAnswer(Body.Stalls("[")));
        using var leave = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(
            () => Prism(stub).PhotosInAsync(Rectangle, 100, leave.Token).WaitAsync(Patience, TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------------------- cave catalogue

    [Fact]
    public async Task A_catalogue_answer_cut_off_part_way_is_retried_and_then_unavailable()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Drops("""{"data":{"pestera":""")));
        var catalogue = Speologie(stub, retries: 1);

        var failure = await Should.ThrowAsync<SpeologieException>(
            () => catalogue.GetAsync(7, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(SpeologieException.UnavailableCode);
        stub.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_catalogue_answer_whose_body_stalls_ends_as_unavailable_within_the_call_limit()
    {
        var stub = new Stub(_ => JsonAnswer(Body.Stalls("{")));
        var catalogue = Speologie(stub, retries: 0);

        var clock = Stopwatch.StartNew();
        var failure = await Should.ThrowAsync<SpeologieException>(
            () => catalogue.GetAsync(7, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(SpeologieException.UnavailableCode);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_catalogue_answer_larger_than_the_ceiling_is_refused_without_being_held()
    {
        // Declared far larger than anything the catalogue sends; the body itself is never read.
        var stub = new Stub(_ =>
        {
            var answer = JsonAnswer(Body.Stalls("{"));
            answer.Content.Headers.ContentLength = 1L << 40;
            return answer;
        });

        var failure = await Should.ThrowAsync<SpeologieException>(
            () => Speologie(stub, retries: 2).GetAsync(7, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(SpeologieException.RejectedCode);
        stub.Calls.ShouldBe(1);
    }

    // ------------------------------------------------------------------------------- elevation cells

    [Fact]
    public async Task An_elevation_cell_whose_body_stalls_is_a_fetch_failure_and_leaves_no_fragment()
    {
        var target = Path.Combine(directory, "cell.tif");
        var fetcher = new CopernicusFetcher(
            new OneClient(new Stub(_ =>
            {
                var content = new StreamContent(Body.Stalls("II*\0"));
                content.Headers.ContentLength = 4096;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            })),
            Options.Create(new TerrainBuildOptions()))
        {
            CellLimit = TimeSpan.FromSeconds(1),
        };

        var failure = await Should.ThrowAsync<TerrainBuildException>(
            () => fetcher.DownloadAsync("N45_00_E022_00", target, TestContext.Current.CancellationToken)
                .WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        File.Exists(target).ShouldBeFalse();
        File.Exists(target + CopernicusFetcher.PartialSuffix).ShouldBeFalse();
    }

    // ------------------------------------------------------------------------------ basemap extract

    [Fact]
    public async Task An_extract_whose_program_falls_silent_is_stopped_as_an_unreachable_source()
    {
        var pidFile = Path.Combine(directory, "stand-in.pid");
        var extractor = new PmtilesCliExtractor(
            Options.Create(new BasemapOptions { ExtractorPath = SilentStandIn(pidFile), ExtractStallSeconds = 2 }),
            NullLogger<PmtilesCliExtractor>.Instance);

        var failure = await Should.ThrowAsync<BasemapException>(
            () => extractor.ExtractAsync(
                    "https://example.invalid/20261024.pmtiles",
                    new BasemapRegion(22.2, 46.2, 23.4, 47.0),
                    12,
                    Path.Combine(directory, "out.pmtiles"),
                    _ => Task.CompletedTask,
                    TestContext.Current.CancellationToken)
                .WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Code.ShouldBe(SelfHostedBasemap.Codes.SourceUnreachable);

        if (!OperatingSystem.IsWindows() && File.Exists(pidFile))
        {
            // The silent program is stopped, not left behind holding its connection.
            var pid = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            var clock = Stopwatch.StartNew();
            while (Directory.Exists($"/proc/{pid}") && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Directory.Exists($"/proc/{pid}").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task An_extract_that_keeps_printing_is_not_stopped_by_the_silence_limit()
    {
        // The positive case beside the one above: progress printed more often than the limit, for
        // longer than the limit, finishes normally.
        var extractor = new PmtilesCliExtractor(
            Options.Create(new BasemapOptions { ExtractorPath = SteadyStandIn(), ExtractStallSeconds = 2 }),
            NullLogger<PmtilesCliExtractor>.Instance);

        await Should.NotThrowAsync(() => extractor.ExtractAsync(
                "https://example.invalid/20261024.pmtiles",
                new BasemapRegion(22.2, 46.2, 23.4, 47.0),
                12,
                Path.Combine(directory, "out.pmtiles"),
                _ => Task.CompletedTask,
                TestContext.Current.CancellationToken)
            .WaitAsync(Patience, TestContext.Current.CancellationToken));
    }

    // ------------------------------------------------------------------------------ document converter

    [Fact]
    public async Task A_document_whose_name_holds_quotation_marks_is_still_sent_to_the_converter()
    {
        string? sentName = null;
        var stub = new Stub(request =>
        {
            var part = ((MultipartFormDataContent)request.Content!).Single();
            sentName = part.Headers.ContentDisposition?.FileName;
            return PdfAnswer(new MemoryStream("%PDF-1.4\n"u8.ToArray()));
        });

        using var destination = new MemoryStream();
        await Converter(stub).ConvertToPortableAsync(
            new MemoryStream([1, 2, 3]), "Raport \"Avenul\"\tfinal.docx", destination,
            TestContext.Current.CancellationToken);

        stub.Calls.ShouldBe(1);
        sentName.ShouldNotBeNull().Trim('"').ShouldBe("Raport _Avenul__final.docx");
        destination.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_converted_document_whose_body_stalls_is_an_unreachable_converter_within_its_time_limit()
    {
        var stub = new Stub(_ => PdfAnswer(Body.Stalls("%PDF-1.4\n")));

        var clock = Stopwatch.StartNew();
        using var destination = new MemoryStream();
        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => Converter(stub)
                .ConvertToPortableAsync(new MemoryStream([1]), "raport.docx", destination, default)
                .WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("could not be reached");
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_converted_document_cut_off_part_way_is_an_unreachable_converter()
    {
        var stub = new Stub(_ => PdfAnswer(Body.Drops("%PDF-1.4\n")));

        using var destination = new MemoryStream();
        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => Converter(stub)
                .ConvertToPortableAsync(new MemoryStream([1]), "raport.docx", destination, default)
                .WaitAsync(Patience, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("could not be reached");
    }

    // ------------------------------------------------------------------------------------ support

    private static HttpDocumentConverter Converter(Stub stub) =>
        new(
            new OneClient(stub),
            Options.Create(new ConversionOptions
            {
                Enabled = true,
                Url = "http://converter.example.invalid",
                TimeoutSeconds = 5,
            }));

    private static HttpResponseMessage PdfAnswer(Stream body)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static ImmichClient Immich(Stub stub) =>
        new(
            new OneClient(stub),
            Options.Create(new ImmichOptions
            {
                Enabled = true,
                BaseUrl = "http://photo-library.invalid:2283",
                ApiKey = "not-a-real-key-0000",
                TimeoutSeconds = 5,
            }),
            new NothingStopped(),
            new NeverStopping(),
            NullLogger<ImmichClient>.Instance);

    private static PhotoPrismClient Prism(Stub stub) =>
        new(
            new OneClient(stub),
            Options.Create(new PhotoPrismOptions
            {
                Enabled = true,
                BaseUrl = "http://photo-prism.invalid:2342",
                AccessToken = "not-a-real-token-0000",
                TimeoutSeconds = 5,
                MaxRetries = 0,
            }),
            new NothingStopped(),
            NullLogger<PhotoPrismClient>.Instance);

    private static SpeologieClient Speologie(Stub stub, int retries) =>
        new(
            new OneClient(stub),
            Options.Create(new SpeologieOptions
            {
                ApiKey = "not-a-real-key-0000",
                Endpoint = "https://catalogue.invalid/api/graphql",
                TimeoutSeconds = 5,
                MinRequestIntervalMs = 0,
                MaxRetries = retries,
            }),
            NullLogger<SpeologieClient>.Instance);

    private static HttpResponseMessage JsonAnswer(Stream body)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>A program that prints one progress line, writes its pid and then says nothing.</summary>
    private string SilentStandIn(string pidFile)
    {
        if (OperatingSystem.IsWindows())
        {
            return Script("pmtiles.cmd", "@echo off\r\necho 1%% fetching\r\nping -n 120 127.0.0.1 >nul\r\nexit /b 0\r\n");
        }

        return Script("pmtiles", $"#!/bin/sh\necho \"1% fetching\"\necho $$ > '{pidFile}'\nexec sleep 120\n");
    }

    /// <summary>A program that prints a progress line every half second for four seconds and exits 0.</summary>
    private string SteadyStandIn()
    {
        if (OperatingSystem.IsWindows())
        {
            return Script(
                "pmtiles.cmd",
                "@echo off\r\nfor /L %%i in (1,1,8) do (\r\necho %%i%% fetching\r\nping -n 2 127.0.0.1 >nul\r\n)\r\nexit /b 0\r\n");
        }

        return Script(
            "pmtiles",
            "#!/bin/sh\ni=0\nwhile [ $i -lt 8 ]; do echo \"$i% fetching\"; sleep 0.5; i=$((i + 1)); done\nexit 0\n");
    }

    private string Script(string name, string text)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>Answers every request the same way, counting them.</summary>
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(answer(request));
        }
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class NothingStopped : IPhotoLibraryBrake
    {
        public ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    private sealed class NeverStopping : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    /// <summary>
    /// A body that sends a few bytes and then either stops sending without ending — waiting until
    /// the reader gives up, as a half-open connection does — or fails as a dropped connection does.
    /// </summary>
    private sealed class Body : Stream
    {
        private readonly byte[] prefix;
        private readonly Exception? failure;
        private int served;

        private Body(string prefix, Exception? failure)
        {
            this.prefix = Encoding.UTF8.GetBytes(prefix);
            this.failure = failure;
        }

        public static Body Stalls(string prefix) => new(prefix, null);

        public static Body Drops(string prefix) =>
            new(prefix, new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely."));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (served < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - served);
                prefix.AsMemory(served, count).CopyTo(buffer);
                served += count;
                return count;
            }

            if (failure is not null)
            {
                throw failure;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
