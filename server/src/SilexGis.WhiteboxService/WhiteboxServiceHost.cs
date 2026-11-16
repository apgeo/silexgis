// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.WhiteboxService;

/// <summary>Limits of the analysis service itself, beside the program's own.</summary>
public sealed class WhiteboxServiceOptions
{
    public const string SectionName = "WhiteboxService";

    /// <summary>Where each run is unpacked, in a directory of its own that is deleted when the run ends. Required.</summary>
    public string WorkDir { get; set; } = "";

    /// <summary>The most the inputs of one run may hold, in bytes (2 GB by default).</summary>
    public long MaxInputBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>How many runs go at once (one by default); the rest wait their turn.</summary>
    public int MaxConcurrentRuns { get; set; } = 1;

    /// <summary>The input limit, never below 1 MB.</summary>
    public long InputCap => Math.Max(1024L * 1024, MaxInputBytes);
}

/// <summary>
/// The analysis service: takes a run — a manifest naming one tool and its arguments, and the input
/// files — as a tar, runs WhiteboxTools over it, and answers a tar of how the run ended and the
/// outputs it asked for.
/// </summary>
/// <remarks>
/// <para>
/// It runs in a container of its own beside the application and publishes no port: the application
/// is its only caller. It holds nothing worth taking — no database, no stored files, none of the
/// application's settings — and its network has no way out. It checks every request against the
/// same rules the application checks before sending one, because a service that trusted its caller
/// to have checked would run whatever reached it.
/// </para>
/// <para>
/// The output tail is the program's own words and can name the run directory's paths; they are
/// written relative to the run before it is answered, so nothing about the service's filesystem
/// reaches the application's log.
/// </para>
/// </remarks>
public static class WhiteboxServiceHost
{
    /// <summary>The most entries a run's tar may hold: the manifest and one input per argument.</summary>
    public const int MaxRunEntries = WhiteboxRequestRules.MaxArguments + 1;

    public static async Task Main(string[] args)
    {
        var app = Build(args);
        await app.RunAsync();
    }

    /// <summary>The service, configured from the arguments and the environment; <paramref name="configure"/> runs last.</summary>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddEnvironmentVariables("SILEXGIS__");

        builder.Services.Configure<WhiteboxOptions>(builder.Configuration.GetSection(WhiteboxOptions.SectionName));
        builder.Services.Configure<WhiteboxServiceOptions>(builder.Configuration.GetSection(WhiteboxServiceOptions.SectionName));
        builder.Services.AddSingleton<IWhiteboxTools, LocalWhiteboxTools>();
        builder.Services.AddSingleton<RunTurns>();
        builder.Services.AddProblemDetails();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        configure?.Invoke(builder);

        var app = builder.Build();
        app.MapGet("/" + WhiteboxServiceProtocol.VersionPath, (IWhiteboxTools tools) =>
            Results.Json(new WhiteboxServiceStatus(tools.IsAvailable, tools.Version), JsonSerializerOptions.Web));
        app.MapPost("/" + WhiteboxServiceProtocol.RunPath, RunAsync);
        return app;
    }

    private static async Task<IResult> RunAsync(
        HttpContext http,
        IWhiteboxTools tools,
        IOptions<WhiteboxOptions> toolOptions,
        IOptions<WhiteboxServiceOptions> serviceOptions,
        RunTurns turns,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(WhiteboxServiceHost));
        if (!tools.IsAvailable)
        {
            return Refusal(StatusCodes.Status503ServiceUnavailable, "whitebox_service.unavailable",
                "This service cannot run WhiteboxTools: the program, or the sandbox it is to run in, is missing.");
        }

        if (!string.Equals(http.Request.ContentType, SurveyProjectTar.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return Refusal(StatusCodes.Status415UnsupportedMediaType, "whitebox_service.not_a_tar",
                $"Send the run as {SurveyProjectTar.MediaType}.");
        }

        var limits = serviceOptions.Value;
        var timeout = toolOptions.Value.Timeout;
        if (long.TryParse(http.Request.Query[WhiteboxServiceProtocol.TimeoutParameter], out var asked) && asked > 0)
        {
            timeout = TimeSpan.FromSeconds(Math.Min(timeout.TotalSeconds, Math.Max(10, asked)));
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = limits.InputCap + 1024L * 1024;
        }

        if (string.IsNullOrWhiteSpace(limits.WorkDir))
        {
            throw new InvalidOperationException("The analysis service needs a work directory: set SILEXGIS__WhiteboxService__WorkDir.");
        }

        await turns.WaitAsync(ct);
        var run = Path.Combine(Path.GetFullPath(limits.WorkDir), Guid.NewGuid().ToString("N"));
        try
        {
            SurveyProjectTarReading reading;
            try
            {
                reading = await SurveyProjectTar.ExtractAsync(
                    http.Request.Body, run, limits.InputCap, MaxRunEntries, ct, WhiteboxServiceProtocol.RequestName);
            }
            catch (SurveyProjectTarException e)
            {
                return e.TooLarge
                    ? Refusal(StatusCodes.Status413PayloadTooLarge, "whitebox_service.input_too_large", e.Message)
                    : Refusal(StatusCodes.Status400BadRequest, "whitebox_service.input_invalid", e.Message);
            }
            catch (Exception e) when (e is InvalidDataException or FormatException or EndOfStreamException)
            {
                return Refusal(StatusCodes.Status400BadRequest, "whitebox_service.input_invalid", "The run is not a readable tar.");
            }
            catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Refusal(StatusCodes.Status413PayloadTooLarge, "whitebox_service.input_too_large", "The run is too large.");
            }

            WhiteboxServiceRequest? request;
            try
            {
                request = reading.Manifest is null
                    ? null
                    : JsonSerializer.Deserialize<WhiteboxServiceRequest>(reading.Manifest, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                request = null;
            }

            if (request is null)
            {
                return Refusal(StatusCodes.Status400BadRequest, "whitebox_service.request_invalid",
                    $"The run's first entry must be {WhiteboxServiceProtocol.RequestName}, naming the tool and its arguments.");
            }

            if (WhiteboxRequestRules.Problem(request.Tool, request.Arguments) is { } problem)
            {
                return Refusal(StatusCodes.Status400BadRequest, "whitebox_service.request_refused", problem);
            }

            // Every file that arrived must be an input the request names, and every input it names
            // must have arrived: nothing else is ever in a run's directory.
            var inputs = request.Arguments.Where(a => a.Kind == WhiteboxArgumentKind.Input).Select(a => a.Value!).ToHashSet(StringComparer.Ordinal);
            if (!reading.Files.ToHashSet(StringComparer.Ordinal).SetEquals(inputs))
            {
                return Refusal(StatusCodes.Status400BadRequest, "whitebox_service.input_mismatch",
                    "The files sent must be exactly the inputs the request names.");
            }

            WhiteboxRunResult result;
            try
            {
                result = await tools.RunAsync(new WhiteboxRunRequest(run, request.Tool, request.Arguments, timeout), ct);
            }
            catch (TimeoutException)
            {
                return Refusal(StatusCodes.Status504GatewayTimeout, "whitebox_service.timeout",
                    $"WhiteboxTools did not finish within {timeout.TotalSeconds:0} seconds and was stopped.");
            }
            catch (ArgumentException e)
            {
                return Refusal(StatusCodes.Status400BadRequest, "whitebox_service.request_refused", e.Message);
            }

            var manifest = JsonSerializer.SerializeToUtf8Bytes(
                new WhiteboxServiceResult(
                    result.Succeeded, result.ExitCode, result.OutputFiles, RelativeToRun(result.OutputTail, run), tools.Version),
                JsonSerializerOptions.Web);

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = SurveyProjectTar.MediaType;
            await SurveyProjectTar.WriteAsync(http.Response.Body, run, result.OutputFiles, ct, WhiteboxServiceProtocol.ResultName, manifest);
            return Results.Empty;
        }
        finally
        {
            turns.Release();
            try
            {
                if (Directory.Exists(run))
                {
                    Directory.Delete(run, recursive: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("An analysis run's directory could not be removed: {Reason}", e.Message);
            }
        }
    }

    /// <summary>The program's words with the run directory's paths written relative to the run.</summary>
    public static string RelativeToRun(string text, string run) =>
        text.Replace(run + "/", "", StringComparison.Ordinal)
            .Replace(run + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
            .Replace(run, ".", StringComparison.Ordinal);

    private static IResult Refusal(int status, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>The turns runs take, so no more than the configured number run at once.</summary>
    public sealed class RunTurns(IOptions<WhiteboxServiceOptions> options) : IDisposable
    {
        private readonly SemaphoreSlim turns = new(Math.Max(1, options.Value.MaxConcurrentRuns));

        public Task WaitAsync(CancellationToken ct) => turns.WaitAsync(ct);

        public void Release() => turns.Release();

        public void Dispose() => turns.Dispose();
    }
}
