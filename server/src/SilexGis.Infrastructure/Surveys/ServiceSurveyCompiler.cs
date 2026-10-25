// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>The compiler service could not be reached, or said it cannot compile: a fact about the installation, not the survey.</summary>
public sealed class SurveyCompilerUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The words the application and the compiler service exchange.</summary>
public static class SurveyCompilerServiceProtocol
{
    /// <summary>Where a project is sent to be compiled; answers a tar of the log and the outputs.</summary>
    public const string CompilePath = "compile";

    /// <summary>Where the service says whether it can compile and with which Therion.</summary>
    public const string VersionPath = "version";

    /// <summary>The entry of the answer's tar that says how the compile ended; always the first.</summary>
    public const string ManifestName = "result.json";

    /// <summary>The query parameter naming the configuration file inside the project.</summary>
    public const string ConfigParameter = "config";

    /// <summary>The query parameter carrying the caller's time limit, in seconds.</summary>
    public const string TimeoutParameter = "timeoutSeconds";
}

/// <summary>How a compile in the service ended, as the manifest of its answer says.</summary>
/// <param name="Succeeded">Whether Therion exited saying the survey compiled.</param>
/// <param name="ExitCode">Therion's exit code, through the sandbox.</param>
/// <param name="OutputFiles">The outputs the log says were written in full, relative to the project.</param>
/// <param name="OutputTail">The end of what Therion printed.</param>
/// <param name="Version">The compiler and its version, such as <c>therion 6.3.4</c>.</param>
public sealed record SurveyCompilerServiceResult(
    bool Succeeded, int ExitCode, IReadOnlyList<string> OutputFiles, string OutputTail, string? Version);

/// <summary>What the service says about itself.</summary>
public sealed record SurveyCompilerServiceStatus(bool Available, string? Version);

/// <summary>
/// Compiles uploaded surveys by handing each project to the compiler service, a container of its
/// own beside the application, and unpacking the log and the outputs it answers into the project.
/// </summary>
/// <remarks>
/// <para>
/// Available whenever the installation named a service: whether the service is up is asked when a
/// compile needs it, and a service that does not answer fails that run as "the compiler is not
/// available" — which a <i>Rerun</i> recovers from — rather than letting the run end as if the
/// installation had chosen not to compile. The version shown is the one the service last gave,
/// asked in the background so that showing it never waits on the network.
/// </para>
/// <para>
/// What comes back is read as the work of a program that ran over somebody else's upload: only
/// ordinary files, inside the project, never over a file already there, within the installation's
/// output budget.
/// </para>
/// </remarks>
public sealed class ServiceSurveyCompiler : ISurveyCompiler
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "survey-compiler";

    /// <summary>The most entries an answer may hold: a log, a handful of outputs and a shapefile's parts.</summary>
    private const int MaxAnswerEntries = 10_000;

    /// <summary>Time the service may spend past the compile's own limit: waiting its turn, and the transfers.</summary>
    private static readonly TimeSpan Allowance = TimeSpan.FromMinutes(2);

    /// <summary>How long a version answer, or the lack of one, is believed before it is asked again.</summary>
    private static readonly TimeSpan VersionLifetime = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly SurveyCompilerOptions options;
    private readonly ILogger<ServiceSurveyCompiler> logger;
    private readonly Uri baseAddress;
    private readonly Lock gate = new();
    private string? version;
    private DateTimeOffset versionAskedAt = DateTimeOffset.MinValue;
    private Task? asking;

    public ServiceSurveyCompiler(
        IHttpClientFactory httpClientFactory, IOptions<SurveyCompilerOptions> options, ILogger<ServiceSurveyCompiler> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options.Value;
        this.logger = logger;
        var url = this.options.Url.Trim();
        baseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
    }

    public bool IsAvailable => true;

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

    /// <summary>Asks the service which Therion it runs, and remembers the answer.</summary>
    public async Task<SurveyCompilerServiceStatus?> AskVersionAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var client = httpClientFactory.CreateClient(HttpClientName);
            var status = await client.GetFromJsonAsync<SurveyCompilerServiceStatus>(
                new Uri(baseAddress, SurveyCompilerServiceProtocol.VersionPath), JsonSerializerOptions.Web, timeout.Token);
            Remember(status is { Available: true } ? status.Version : null);
            return status;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning("The survey compiler service at {Url} did not say which Therion it runs: {Reason}", options.Url, e.Message);
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

    public async Task<SurveyCompileResult> CompileAsync(
        SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct)
    {
        var project = Path.GetFullPath(request.ProjectDirectory);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"{SurveyCompilerServiceProtocol.CompilePath}?{SurveyCompilerServiceProtocol.ConfigParameter}={Uri.EscapeDataString(request.ConfigFileName)}"
            + $"&{SurveyCompilerServiceProtocol.TimeoutParameter}={(long)Math.Ceiling(request.Timeout.TotalSeconds)}");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout + Allowance);

        var files = SurveyProjectTar.FilesUnder(project, SandboxedTherionRunner.ScratchDirectoryName);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, query))
        {
            Content = new ProjectContent(project, files),
        };

        progress.Report(new SurveyCompileProgress("handed to the compiler service", 5));

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The compiler service did not answer within {(request.Timeout + Allowance).TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException e)
        {
            Remember(null);
            throw new SurveyCompilerUnavailableException($"The survey compiler service at {options.Url} could not be reached.", e);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.GatewayTimeout:
                    throw new TimeoutException($"Therion did not finish within {request.Timeout.TotalSeconds:0} seconds.");
                case HttpStatusCode.ServiceUnavailable:
                    Remember(null);
                    throw new SurveyCompilerUnavailableException($"The survey compiler service at {options.Url} says it cannot compile.");
                default:
                    throw new InvalidOperationException(
                        $"The survey compiler service answered {(int)response.StatusCode} to a compile.");
            }

            SurveyProjectTarReading reading;
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
                reading = await SurveyProjectTar.ExtractAsync(
                    body, project, options.OutputCap, MaxAnswerEntries, deadline.Token, SurveyCompilerServiceProtocol.ManifestName);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("The compiler service's answer did not arrive in time.");
            }
            catch (Exception e) when (e is SurveyProjectTarException or InvalidDataException or FormatException or EndOfStreamException)
            {
                throw new InvalidOperationException($"The survey compiler service's answer could not be read: {e.Message}", e);
            }

            var result = reading.Manifest is null
                ? null
                : JsonSerializer.Deserialize<SurveyCompilerServiceResult>(reading.Manifest, JsonSerializerOptions.Web);
            if (result is null)
            {
                throw new InvalidOperationException("The survey compiler service's answer said nothing about how the compile ended.");
            }

            if (result.Version is { } said)
            {
                Remember(said);
            }

            var received = reading.Files.ToHashSet(StringComparer.Ordinal);
            var log = received.Contains(SandboxedTherionRunner.LogFileName)
                ? await File.ReadAllTextAsync(Path.Combine(project, SandboxedTherionRunner.LogFileName), ct)
                : "";

            // Only outputs that arrived: a file by its own name, a directory output (a shapefile
            // set) by any file under it.
            var outputs = result.OutputFiles
                .Select(SurveyProjectTar.SafeName)
                .OfType<string>()
                .Where(o => received.Contains(o) || received.Any(f => f.StartsWith(o + "/", StringComparison.Ordinal)))
                .ToList();

            progress.Report(new SurveyCompileProgress("answered by the compiler service", 100));
            return new SurveyCompileResult(result.Succeeded, result.ExitCode, log, outputs, result.OutputTail ?? "");
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

    /// <summary>A project's files, written as a tar straight onto the request as it is sent.</summary>
    private sealed class ProjectContent : HttpContent
    {
        private readonly string root;
        private readonly IReadOnlyList<string> files;

        public ProjectContent(string root, IReadOnlyList<string> files)
        {
            this.root = root;
            this.files = files;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(SurveyProjectTar.MediaType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            SurveyProjectTar.WriteAsync(stream, root, files, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
