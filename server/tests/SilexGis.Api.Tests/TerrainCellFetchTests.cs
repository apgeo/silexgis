// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Obtaining one cell of elevation, and what is left on disk when it does not arrive.
///
/// <para>
/// No database and no host: what is under test is a transfer, and every assertion here is about a
/// file that is or is not there afterwards. The rules were learned against the real bucket over
/// real ground and each one fails silently if it is dropped — a fragment left at the name a
/// finished cell would have is read as a finished cell by the next run, bakes into terrain with a
/// hole in it, and a hole is drawn as smooth ground rather than as anything a person would notice.
/// </para>
/// </summary>
public class TerrainCellFetchTests : IDisposable
{
    private const string Cell = "N46_00_E022_00";

    private readonly string directory =
        Path.Combine(TestScratch.Root, $"silexgis-cells-{Guid.NewGuid():N}");

    public TerrainCellFetchTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task A_cell_that_arrives_whole_is_written_under_its_own_name()
    {
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        var written = await FetchAsync(Answering(HttpStatusCode.OK, payload, declared: payload.Length));

        written.ShouldBe(payload.Length);
        (await File.ReadAllBytesAsync(Target)).ShouldBe(payload);
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task A_transfer_that_stops_short_leaves_neither_the_cell_nor_the_fragment()
    {
        // A body that ends early usually throws while it is being read, but not always: a proxy can
        // close a connection cleanly on a byte boundary, and nothing about that reads as an error.
        // The length the server declared is the only thing that catches it.
        var partial = new byte[1000];

        var failure = await Should.ThrowAsync<TerrainBuildException>(
            () => FetchAsync(Answering(HttpStatusCode.OK, partial, declared: 4096)));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        failure.Message.ShouldContain("1000");
        failure.Message.ShouldContain("4096");

        // Neither name is occupied. The next attempt starts this cell again rather than trusting
        // what is lying there.
        File.Exists(Target).ShouldBeFalse();
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task A_link_that_drops_mid_body_leaves_neither_the_cell_nor_the_fragment()
    {
        var failure = await Should.ThrowAsync<TerrainBuildException>(
            () => FetchAsync(Dropping(new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely."))));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        File.Exists(Target).ShouldBeFalse();
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task A_socket_reset_mid_body_is_a_fetch_failure()
    {
        var reset = new IOException("reset", new System.Net.Sockets.SocketException());

        var failure = await Should.ThrowAsync<TerrainBuildException>(() => FetchAsync(Dropping(reset)));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task A_local_io_error_mid_transfer_is_not_blamed_on_the_server_and_leaves_no_fragment()
    {
        // A plain I/O error is what a failing local disk raises too, so it keeps its own type.
        await Should.ThrowAsync<IOException>(() => FetchAsync(Dropping(new IOException("The connection went away."))));

        File.Exists(Target).ShouldBeFalse();
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task A_cell_the_dataset_does_not_publish_is_an_ordinary_answer_and_not_a_failure()
    {
        // Cells that are entirely ocean are simply never published, so every rectangle drawn near a
        // coast asks for some that are not there. Treating that as an error would refuse every
        // coastal area on Earth while looking exactly like a broken download.
        var written = await FetchAsync(Answering(HttpStatusCode.NotFound, [], declared: 0));

        written.ShouldBeNull();
        File.Exists(Target).ShouldBeFalse();
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task Any_other_refusal_stops_the_build_with_a_reason_of_its_own()
    {
        var failure = await Should.ThrowAsync<TerrainBuildException>(
            () => FetchAsync(Answering(HttpStatusCode.InternalServerError, [], declared: 0)));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        failure.Message.ShouldContain("500");
        File.Exists(Target).ShouldBeFalse();
    }

    [Fact]
    public async Task A_connection_that_cannot_be_made_is_a_fetch_failure_and_not_an_unexpected_one()
    {
        // A refused or unreachable connection is the likeliest way this step fails. Escaping as the
        // raw transport exception, it was recorded as "something unexpected", which tells the
        // operator nothing about where to look.
        var failure = await Should.ThrowAsync<TerrainBuildException>(
            () => FetchAsync(new StubHandler(_ => throw new HttpRequestException("connection refused"))));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        failure.Message.ShouldContain(Cell);
        File.Exists(Target).ShouldBeFalse();
        File.Exists(Partial).ShouldBeFalse();
    }

    [Fact]
    public async Task A_cell_that_outlasts_the_clients_time_limit_is_a_fetch_failure()
    {
        var failure = await Should.ThrowAsync<TerrainBuildException>(
            () => FetchAsync(new StubHandler(_ => throw new TaskCanceledException("timed out"))));

        failure.Code.ShouldBe(TerrainBuildFailures.FetchFailed);
        failure.Message.ShouldContain("time allowed");
    }

    [Fact]
    public async Task A_stop_requested_by_the_caller_is_not_turned_into_a_failure()
    {
        // The positive case beside the two above: the same exception type, but the caller asked for
        // it, as a host shutdown or a called-off build does. It must reach the build handler as a
        // cancellation, or a stopped build would be recorded as having failed to fetch.
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var fetcher = new CopernicusFetcher(
            new OneClient(new StubHandler(_ => throw new TaskCanceledException("stopped"))),
            Options.Create(new TerrainBuildOptions()));

        await Should.ThrowAsync<OperationCanceledException>(
            () => fetcher.DownloadAsync(Cell, Target, stop.Token));
    }

    [Fact]
    public async Task A_body_of_undeclared_length_is_accepted_on_what_actually_arrived()
    {
        // Chunked answers carry no length to check against, which is not a reason to refuse them:
        // the check is "if the server said how much, that is how much there must be".
        var payload = new byte[2048];
        var written = await FetchAsync(Answering(HttpStatusCode.OK, payload, declared: null));

        written.ShouldBe(payload.Length);
        File.Exists(Target).ShouldBeTrue();
    }

    private string Target => Path.Combine(directory, CopernicusCoverage.FileName(Cell));

    private string Partial => Target + CopernicusFetcher.PartialSuffix;

    private Task<long?> FetchAsync(HttpMessageHandler handler)
    {
        var fetcher = new CopernicusFetcher(
            new OneClient(handler), Options.Create(new TerrainBuildOptions()));
        return fetcher.DownloadAsync(Cell, Target, CancellationToken.None);
    }

    private static HttpMessageHandler Answering(HttpStatusCode status, byte[] body, long? declared)
    {
        return new StubHandler(_ =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentLength = declared;
            return new HttpResponseMessage(status) { Content = content };
        });
    }

    /// <summary>A server that answers, starts sending, and then goes away.</summary>
    private static HttpMessageHandler Dropping(Exception error)
    {
        return new StubHandler(_ =>
        {
            var content = new StreamContent(new FailingStream(error));
            content.Headers.ContentLength = 4096;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A factory that hands out one client, over the handler a test supplied.</summary>
    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    /// <summary>Reads a little and then fails, the way a dropped connection does.</summary>
    private sealed class FailingStream(Exception error) : Stream
    {
        private int served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => served;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (served > 0)
            {
                throw error;
            }

            var given = Math.Min(count, 512);
            Array.Clear(buffer, offset, given);
            served += given;
            return given;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
