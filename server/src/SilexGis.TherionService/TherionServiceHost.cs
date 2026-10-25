// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.TherionService;

/// <summary>Limits of the compiler service itself, beside the compiler's own.</summary>
public sealed class TherionServiceOptions
{
    public const string SectionName = "TherionService";

    /// <summary>The most one project sent to be compiled may hold, in bytes (256 MB by default).</summary>
    public long MaxProjectBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// How many compiles run at once (one by default); the rest wait their turn. One at a time also
    /// means a run's files are the only survey files in the container while it compiles.
    /// </summary>
    public int MaxConcurrentCompiles { get; set; } = 1;

    /// <summary>The project limit, never below 1 MB.</summary>
    public long ProjectCap => Math.Max(1024L * 1024, MaxProjectBytes);
}

/// <summary>
/// The survey compiler service: takes a project as a tar, compiles it with Therion, and answers a
/// tar of how the compile ended, its log and its outputs.
/// </summary>
/// <remarks>
/// <para>
/// It runs in a container of its own beside the application and publishes no port: the application
/// is its only caller. It holds nothing worth taking — no database, no stored files, none of the
/// application's settings — so what a survey can make Therion do inside it reaches only the run in
/// hand. Each run is unpacked into a fresh directory under the work directory and deleted when it
/// ends, however it ends.
/// </para>
/// <para>
/// The paths Therion writes into its log are the run directory's, which mean nothing to the
/// application; they are made relative to the project before the log is sent back, so that the
/// outputs it names read the same on both sides.
/// </para>
/// </remarks>
public static class TherionServiceHost
{
    /// <summary>The most entries a project may hold: sources, drawings and the configuration around them.</summary>
    public const int MaxProjectEntries = 10_000;

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

        builder.Services.Configure<SurveyCompilerOptions>(builder.Configuration.GetSection(SurveyCompilerOptions.SectionName));
        builder.Services.Configure<TherionServiceOptions>(builder.Configuration.GetSection(TherionServiceOptions.SectionName));
        builder.Services.AddSingleton<ISurveyCompiler, LocalSurveyCompiler>();
        builder.Services.AddSingleton<CompileTurns>();
        builder.Services.AddProblemDetails();
        // A line per request says nothing an operator needs; what a compile did is in its answer.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        configure?.Invoke(builder);

        var app = builder.Build();
        app.MapGet("/" + SurveyCompilerServiceProtocol.VersionPath, (ISurveyCompiler compiler) =>
            Results.Json(new SurveyCompilerServiceStatus(compiler.IsAvailable, compiler.Version), JsonSerializerOptions.Web));
        app.MapPost("/" + SurveyCompilerServiceProtocol.CompilePath, CompileAsync);
        return app;
    }

    private static async Task<IResult> CompileAsync(
        HttpContext http,
        ISurveyCompiler compiler,
        IOptions<SurveyCompilerOptions> compilerOptions,
        IOptions<TherionServiceOptions> serviceOptions,
        CompileTurns turns,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(TherionServiceHost));
        if (!compiler.IsAvailable)
        {
            return Refusal(StatusCodes.Status503ServiceUnavailable, "therion_service.unavailable",
                "This service cannot compile: Therion, or the sandbox it is to run in, is missing.");
        }

        var config = http.Request.Query[SurveyCompilerServiceProtocol.ConfigParameter].ToString();
        if (SurveyProjectTar.SafeName(config) != config || config.Contains('/', StringComparison.Ordinal))
        {
            return Refusal(StatusCodes.Status400BadRequest, "therion_service.config_invalid",
                "Name the configuration file at the top of the project.");
        }

        if (!string.Equals(http.Request.ContentType, SurveyProjectTar.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return Refusal(StatusCodes.Status415UnsupportedMediaType, "therion_service.not_a_tar",
                $"Send the project as {SurveyProjectTar.MediaType}.");
        }

        var settings = compilerOptions.Value;
        var limits = serviceOptions.Value;
        var timeout = settings.Timeout;
        if (long.TryParse(http.Request.Query[SurveyCompilerServiceProtocol.TimeoutParameter], out var asked) && asked > 0)
        {
            timeout = TimeSpan.FromSeconds(Math.Min(timeout.TotalSeconds, Math.Max(10, asked)));
        }

        // The tar's own headers ride on top of the files' bytes, which are what the cap is about.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = limits.ProjectCap + 16L * 1024 * 1024;
        }

        var work = settings.WorkDir;
        if (string.IsNullOrWhiteSpace(work))
        {
            throw new InvalidOperationException("The compiler service needs a work directory: set SILEXGIS__SurveyCompiler__WorkDir.");
        }

        await turns.WaitAsync(ct);
        var run = Path.Combine(Path.GetFullPath(work), Guid.NewGuid().ToString("N"));
        try
        {
            try
            {
                await SurveyProjectTar.ExtractAsync(http.Request.Body, run, limits.ProjectCap, MaxProjectEntries, ct);
            }
            catch (SurveyProjectTarException e)
            {
                return e.TooLarge
                    ? Refusal(StatusCodes.Status413PayloadTooLarge, "therion_service.project_too_large", e.Message)
                    : Refusal(StatusCodes.Status400BadRequest, "therion_service.project_invalid", e.Message);
            }
            catch (Exception e) when (e is InvalidDataException or FormatException or EndOfStreamException)
            {
                return Refusal(StatusCodes.Status400BadRequest, "therion_service.project_invalid", "The project is not a readable tar.");
            }
            catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Refusal(StatusCodes.Status413PayloadTooLarge, "therion_service.project_too_large", "The project is too large.");
            }

            if (!File.Exists(Path.Combine(run, config)))
            {
                return Refusal(StatusCodes.Status400BadRequest, "therion_service.config_missing",
                    "The project holds no file of the configuration's name.");
            }

            SurveyCompileResult result;
            try
            {
                result = await compiler.CompileAsync(
                    new SurveyCompileRequest(run, config, timeout), new Progress<SurveyCompileProgress>(), ct);
            }
            catch (TimeoutException)
            {
                return Refusal(StatusCodes.Status504GatewayTimeout, "therion_service.timeout",
                    $"Therion did not finish within {timeout.TotalSeconds:0} seconds and was stopped.");
            }

            var logPath = Path.Combine(run, SandboxedTherionRunner.LogFileName);
            var answered = new List<string>();
            if (File.Exists(logPath))
            {
                await File.WriteAllTextAsync(logPath, RelativeToProject(result.LogText, run), ct);
                answered.Add(SandboxedTherionRunner.LogFileName);
            }

            answered.AddRange(result.OutputFiles.Where(o => o != SandboxedTherionRunner.LogFileName));
            var manifest = JsonSerializer.SerializeToUtf8Bytes(
                new SurveyCompilerServiceResult(result.Succeeded, result.ExitCode, result.OutputFiles, result.OutputTail, compiler.Version),
                JsonSerializerOptions.Web);

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = SurveyProjectTar.MediaType;
            await SurveyProjectTar.WriteAsync(
                http.Response.Body, run, answered, ct, SurveyCompilerServiceProtocol.ManifestName, manifest);
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
                logger.LogWarning("A compile run's directory could not be removed: {Reason}", e.Message);
            }
        }
    }

    /// <summary>The log with the run directory's paths written relative to the project.</summary>
    public static string RelativeToProject(string log, string run) =>
        log.Replace(run + "/", "", StringComparison.Ordinal)
            .Replace(run + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
            .Replace(run, ".", StringComparison.Ordinal);

    private static IResult Refusal(int status, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>The turns compiles take, so no more than the configured number run at once.</summary>
    public sealed class CompileTurns(IOptions<TherionServiceOptions> options) : IDisposable
    {
        private readonly SemaphoreSlim turns = new(Math.Max(1, options.Value.MaxConcurrentCompiles));

        public Task WaitAsync(CancellationToken ct) => turns.WaitAsync(ct);

        public void Release() => turns.Release();

        public void Dispose() => turns.Dispose();
    }
}
