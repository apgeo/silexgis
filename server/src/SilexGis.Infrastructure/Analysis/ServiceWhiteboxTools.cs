// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>The words the application and the analysis service exchange.</summary>
public static class WhiteboxServiceProtocol
{
    /// <summary>Where a run is sent: a tar of the request and its inputs; answers a tar of how it ended and its outputs.</summary>
    public const string RunPath = "run";

    /// <summary>Where the service says whether it can run the program and which version it has.</summary>
    public const string VersionPath = "version";

    /// <summary>The first entry of a request's tar: the tool and its arguments.</summary>
    public const string RequestName = "request.json";

    /// <summary>The first entry of an answer's tar: how the run ended.</summary>
    public const string ResultName = "result.json";

    /// <summary>The query parameter carrying the caller's time limit, in seconds.</summary>
    public const string TimeoutParameter = "timeoutSeconds";
}

/// <summary>The tool and arguments of a run, as the request's manifest carries them.</summary>
public sealed record WhiteboxServiceRequest(string Tool, IReadOnlyList<WhiteboxArgument> Arguments);

/// <summary>How a run in the service ended, as the answer's manifest says.</summary>
public sealed record WhiteboxServiceResult(
    bool Succeeded, int ExitCode, IReadOnlyList<string> OutputFiles, string OutputTail, string? Version);

/// <summary>What the service says about itself.</summary>
public sealed record WhiteboxServiceStatus(bool Available, string? Version);

/// <summary>
/// Runs WhiteboxTools by handing each run to the analysis service, a container of its own beside
/// the application with no database, no stored files and no way out to the network, and unpacking
/// the outputs it answers into the run directory.
/// </summary>
/// <remarks>
/// <para>
/// Only the files a run names as inputs travel, with the tool and its arguments; nothing the
/// application holds besides them reaches the service. Available whenever the installation named a
/// service: whether it is up is found out when a run needs it, and a service that does not answer
/// fails that run as "not available" (which the job queue retries) rather than as a fault in the
/// input. The version shown is the one the service last gave, asked in the background so that
/// showing it never waits on the network.
/// </para>
/// <para>
/// What comes back is read as the work of a program that may have misbehaved: only ordinary files,
/// only under the names the run asked for, never over a file already there, within the
/// installation's output budget.
/// </para>
/// </remarks>
public sealed class ServiceWhiteboxTools : IWhiteboxTools
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "whitebox-service";

    /// <summary>The most entries an answer may hold: the manifest and the outputs a run asked for, with room to spare.</summary>
    private const int MaxAnswerEntries = WhiteboxRequestRules.MaxArguments + 1;

    /// <summary>Time the service may spend past the run's own limit: waiting its turn, and the transfers.</summary>
    private static readonly TimeSpan Allowance = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan VersionLifetime = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly WhiteboxOptions options;
    private readonly ILogger<ServiceWhiteboxTools> logger;
    private readonly Uri baseAddress;
    private readonly Lock gate = new();
    private string? version;
    private DateTimeOffset versionAskedAt = DateTimeOffset.MinValue;
    private Task? asking;

    public ServiceWhiteboxTools(
        IHttpClientFactory httpClientFactory, IOptions<WhiteboxOptions> options, ILogger<ServiceWhiteboxTools> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options.Value;
        this.logger = logger;
        var url = this.options.Url.Trim();
        baseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
    }

    public string Mode => WhiteboxOptions.ModeService;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public string? Version
    {
        get
        {
            lock (gate)
            {
                if (asking is null && DateTimeOffset.UtcNow - versionAskedAt > VersionLifetime)
                {
                    versionAskedAt = DateTimeOffset.UtcNow;
                    asking = Task.Run(AskVersionAsync);
                }

                return version;
            }
        }
    }

    /// <summary>Asks the service which WhiteboxTools it runs, and remembers the answer.</summary>
    public async Task<WhiteboxServiceStatus?> AskVersionAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var client = httpClientFactory.CreateClient(HttpClientName);
            var status = await client.GetFromJsonAsync<WhiteboxServiceStatus>(
                new Uri(baseAddress, WhiteboxServiceProtocol.VersionPath), JsonSerializerOptions.Web, timeout.Token);
            Remember(status is { Available: true } ? status.Version : null);
            return status;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning("The analysis service at {Url} did not say which WhiteboxTools it runs: {Reason}", options.Url, e.Message);
            Remember(null);
            return null;
        }
        finally
        {
            lock (gate)
            {
                asking = null;
            }
        }
    }

    public async Task<WhiteboxRunResult> RunAsync(WhiteboxRunRequest request, CancellationToken ct)
    {
        if (WhiteboxRequestRules.Problem(request.Tool, request.Arguments) is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        var run = Path.GetFullPath(request.WorkDirectory);
        var inputs = request.Arguments.Where(a => a.Kind == WhiteboxArgumentKind.Input).Select(a => a.Value!).ToList();
        var outputs = request.Arguments.Where(a => a.Kind == WhiteboxArgumentKind.Output).Select(a => a.Value!).ToList();
        foreach (var input in inputs)
        {
            var file = new FileInfo(Path.Combine(run, input));
            if (!file.Exists || file.LinkTarget is not null)
            {
                throw new ArgumentException($"The input '{input}' is not an ordinary file in the run directory.", nameof(request));
            }
        }

        foreach (var output in outputs.Where(o => File.Exists(Path.Combine(run, o)) || Directory.Exists(Path.Combine(run, o))))
        {
            throw new ArgumentException($"The output '{output}' is already there.", nameof(request));
        }

        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"{WhiteboxServiceProtocol.RunPath}?{WhiteboxServiceProtocol.TimeoutParameter}={(long)Math.Ceiling(request.Timeout.TotalSeconds)}");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new WhiteboxServiceRequest(request.Tool, request.Arguments), JsonSerializerOptions.Web);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout + Allowance);

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, query))
        {
            Content = new RunContent(run, inputs, manifest),
        };

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The analysis service did not answer within {(request.Timeout + Allowance).TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException e)
        {
            Remember(null);
            throw new WhiteboxUnavailableException($"The analysis service at {options.Url} could not be reached.", e);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.GatewayTimeout:
                    throw new TimeoutException($"WhiteboxTools did not finish within {request.Timeout.TotalSeconds:0} seconds.");
                case HttpStatusCode.ServiceUnavailable:
                    Remember(null);
                    throw new WhiteboxUnavailableException($"The analysis service at {options.Url} says it cannot run WhiteboxTools.");
                default:
                    throw new InvalidOperationException($"The analysis service answered {(int)response.StatusCode} to a run.");
            }

            // Unpacked beside the run first, so that nothing but the asked-for outputs ever lands
            // in the run directory, whatever names the answer holds.
            var landing = Path.Combine(run, SandboxedWhiteboxRunner.ScratchDirectoryName, "answer-" + Guid.NewGuid().ToString("N"));
            try
            {
                SurveyProjectTarReading reading;
                try
                {
                    await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
                    reading = await SurveyProjectTar.ExtractAsync(
                        body, landing, options.OutputCap, MaxAnswerEntries, deadline.Token, WhiteboxServiceProtocol.ResultName);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("The analysis service's answer did not arrive in time.");
                }
                catch (Exception e) when (e is SurveyProjectTarException or InvalidDataException or FormatException or EndOfStreamException)
                {
                    throw new InvalidOperationException($"The analysis service's answer could not be read: {e.Message}", e);
                }

                var result = reading.Manifest is null
                    ? null
                    : JsonSerializer.Deserialize<WhiteboxServiceResult>(reading.Manifest, JsonSerializerOptions.Web);
                if (result is null)
                {
                    throw new InvalidOperationException("The analysis service's answer said nothing about how the run ended.");
                }

                if (result.Version is { } said)
                {
                    Remember(said);
                }

                var received = reading.Files.ToHashSet(StringComparer.Ordinal);
                var kept = new List<string>();
                foreach (var output in outputs.Where(received.Contains))
                {
                    var arrived = new FileInfo(Path.Combine(landing, output));
                    if (arrived.Length <= options.OutputFileCap)
                    {
                        arrived.MoveTo(Path.Combine(run, output));
                        kept.Add(output);
                    }
                }

                return new WhiteboxRunResult(
                    result.Succeeded && kept.Count == outputs.Count, result.ExitCode, kept, result.OutputTail ?? "");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(landing))
                    {
                        Directory.Delete(landing, recursive: true);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("An analysis answer's landing directory could not be removed: {Reason}", e.Message);
                }
            }
        }
    }

    private void Remember(string? said)
    {
        lock (gate)
        {
            version = said;
            versionAskedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>The request's manifest and its input files, written as a tar straight onto the request as it is sent.</summary>
    private sealed class RunContent : HttpContent
    {
        private readonly string root;
        private readonly IReadOnlyList<string> files;
        private readonly byte[] manifest;

        public RunContent(string root, IReadOnlyList<string> files, byte[] manifest)
        {
            this.root = root;
            this.files = files;
            this.manifest = manifest;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(SurveyProjectTar.MediaType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            SurveyProjectTar.WriteAsync(stream, root, files, cancellationToken, WhiteboxServiceProtocol.RequestName, manifest);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
