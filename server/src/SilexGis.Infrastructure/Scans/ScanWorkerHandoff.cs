// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Scans;

/// <summary>A step the worker announced it can run, with the tool and version it runs it with.</summary>
public sealed record ScanWorkerStep(string Id, string Tool, string Version);

/// <summary>A step's default parameters as a worker dumped them from the program it runs.</summary>
/// <param name="Version">The version the worker names the step's tool by; empty when the file names none.</param>
/// <param name="SoftwareVersion">The program's own version word.</param>
/// <param name="Values">The defaults by the program's own sections.</param>
/// <param name="NativeText">The program's own text of the dump.</param>
/// <param name="WrittenAt">When the worker wrote the file.</param>
public sealed record ScanWorkerDefaults(
    string StepId, string Version, string? SoftwareVersion, JsonElement Values, string? NativeText, DateTimeOffset? WrittenAt);

/// <summary>What the worker wrote at its start: who it is and what it can do.</summary>
public sealed record ScanWorkerCapabilities(string Worker, IReadOnlyList<ScanWorkerStep> Steps)
{
    public ScanWorkerStep? Find(string stepId) => Steps.FirstOrDefault(s => s.Id == stepId);
}

/// <summary>One product the worker left in the job directory, as its result named it.</summary>
public sealed record ScanJobOutput(string Kind, string File, long SizeBytes, string Sha256);

/// <summary>How a job ended, by the worker's own account.</summary>
public enum ScanJobStatus
{
    /// <summary>The result could not be read: no status, or one this side does not know.</summary>
    Unreadable = 0,
    Ok = 1,
    Failed = 2,
    Refused = 3,
    Cancelled = 4,
    Interrupted = 5,
}

/// <summary>The worker's answer to a job, read from its result file.</summary>
/// <param name="Data">What the step reported beyond its files — the estimate's rows, the tiling's counts — as the worker wrote it.</param>
public sealed record ScanJobResult(
    ScanJobStatus Status,
    string Reason,
    IReadOnlyList<ScanJobOutput> Outputs,
    string Tool,
    string ToolVersion,
    double Seconds,
    IReadOnlyList<string> LogTail,
    JsonElement Data);

/// <summary>The hand-off failed in a way the run reports under a stable code.</summary>
public sealed class ScanWorkerException(string code, string message, IReadOnlyList<string>? logTail = null) : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyList<string> LogTail { get; } = logTail ?? [];
}

/// <summary>
/// The application's side of the directory it shares with the point-cloud worker. The worker runs
/// in a container of its own with no network and no database; the two meet on files, and this is
/// the only place the application writes or reads them.
/// </summary>
/// <remarks>
/// <para>
/// <c>capabilities.json</c> at the root says which steps the worker can run; it is rewritten
/// every time the worker starts and read here before every step, so a worker stopped or replaced
/// between two steps of one run is noticed. One job is one directory under <c>jobs/</c>: the
/// inputs copied in, <c>request.json</c> written last and renamed into place, then
/// <c>started</c>, <c>progress.json</c> and the outputs from the worker, and <c>result.json</c>
/// last of all, renamed into place as well, so neither side ever reads half a file.
/// </para>
/// <para>
/// A job that nothing takes within the pickup wait is given up as having no worker; one that runs
/// past the time limit is called off by writing <c>cancel</c>, which the worker answers by
/// stopping the tool and writing a cancelled result. The job's directory is the application's to
/// remove, and it is removed once the products have been stored or the failure recorded — except
/// when the host is shutting down mid-wait, when it is left as it is so the resumed run can take
/// it up again.
/// </para>
/// </remarks>
public sealed class ScanWorkerHandoff(IOptions<ScanOptions> options, TimeProvider clock, ILogger<ScanWorkerHandoff> logger)
{
    public const string CapabilitiesFile = "capabilities.json";
    public const string JobsDirectory = "jobs";
    public const string RequestFile = "request.json";
    public const string StartedFile = "started";
    public const string ProgressFile = "progress.json";
    public const string CancelFile = "cancel";
    public const string ResultFile = "result.json";
    public const string LogFile = "worker.log";
    public const string DefaultsDirectory = "defaults";
    public const string OdometryDefaultsFile = "hdmapping.odometry.json";
    private const string PartialSuffix = ".part";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>How often the job directory is looked at while a job runs. Short in tests, which set it.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a called-off job is given to confirm it stopped before its directory is removed anyway.</summary>
    public TimeSpan WithdrawalWait { get; set; } = TimeSpan.FromSeconds(15);

    public string Root { get; } = Path.GetFullPath(options.Value.HandoffRoot, AppContext.BaseDirectory);

    /// <summary>Whether this installation says it has a worker at all.</summary>
    public bool Enabled => options.Value.ProcessingEnabled;

    /// <summary>
    /// What the worker last announced, or null when nothing has: no file, or one that does not
    /// read, which from here looks the same as no worker.
    /// </summary>
    public ScanWorkerCapabilities? ReadCapabilities()
    {
        var path = Path.Combine(Root, CapabilitiesFile);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var steps = new List<ScanWorkerStep>();
            if (root.TryGetProperty("steps", out var announced) && announced.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in announced.EnumerateArray())
                {
                    var id = Text(step, "id");
                    if (!string.IsNullOrEmpty(id))
                    {
                        steps.Add(new ScanWorkerStep(id, Text(step, "tool") ?? "", Text(step, "version") ?? ""));
                    }
                }
            }

            return new ScanWorkerCapabilities(Text(root, "worker") ?? "", steps);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "The point-cloud worker's capabilities at {Path} could not be read", path);
            return null;
        }
    }

    /// <summary>
    /// HDMapping's default odometry parameters as the HDMapping worker dumped them from the program
    /// it runs, by TOML section; null when no such worker has started, or what it wrote does not
    /// read. The worker writes them under <c>defaults/</c> beside the capabilities at every start.
    /// </summary>
    public JsonElement? ReadOdometryDefaults()
    {
        var path = Path.Combine(Root, DefaultsDirectory, OdometryDefaultsFile);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.TryGetProperty("values", out var values)
                && values.ValueKind == JsonValueKind.Object
                && values.EnumerateObject().Any()
                ? values.Clone()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "HDMapping's default parameters at {Path} could not be read", path);
            return null;
        }
    }

    /// <summary>
    /// The whole of HDMapping's default-parameter dump as the worker wrote it — the values, the
    /// version it names, the program's own TOML text and when it was written; null when no such
    /// worker has started, or what it wrote does not read.
    /// </summary>
    public ScanWorkerDefaults? ReadOdometryDefaultsDocument()
    {
        var path = Path.Combine(Root, DefaultsDirectory, OdometryDefaultsFile);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("values", out var values)
                || values.ValueKind != JsonValueKind.Object
                || !values.EnumerateObject().Any())
            {
                return null;
            }

            DateTimeOffset? writtenAt = Text(root, "writtenAt") is { } written
                && DateTimeOffset.TryParse(written, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
            return new ScanWorkerDefaults(
                Text(root, "step") ?? Domain.Scans.HdmappingOdometryStep.StepId,
                Text(root, "version") ?? "",
                Text(root, "softwareVersion"),
                values.Clone(),
                Text(root, "toml"),
                writtenAt);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "HDMapping's default parameters at {Path} could not be read", path);
            return null;
        }
    }

    /// <summary>
    /// What the worker announced, waiting up to the pickup wait for it to do so. A worker writes
    /// its capabilities when it starts, and a job taken up right after the stack restarted can
    /// come before that; so an absent announcement is given the same grace a job nothing has
    /// taken yet gets, re-read at the poll interval, and is null only once that has passed.
    /// </summary>
    public async Task<ScanWorkerCapabilities?> AwaitCapabilitiesAsync(CancellationToken ct)
    {
        var by = clock.GetUtcNow() + options.Value.PickupWait;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadCapabilities() is { } announced)
            {
                return announced;
            }

            if (clock.GetUtcNow() >= by)
            {
                return null;
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>The directory a job of this name runs in.</summary>
    public string JobDirectory(string jobName) => Path.Combine(Root, JobsDirectory, jobName);

    /// <summary>
    /// Makes the job directory afresh. A directory left by an earlier attempt is cleared first;
    /// if the worker is still on it, the job is called off and the worker given a moment to
    /// confirm, so the directory is not pulled out from under a running tool.
    /// </summary>
    public async Task<string> BeginAsync(string jobName, CancellationToken ct)
    {
        var directory = JobDirectory(jobName);
        if (Directory.Exists(directory))
        {
            if (InFlight(directory))
            {
                await WithdrawAsync(directory, ct);
            }

            Forget(directory);
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Copies an input file into the job directory under a plain name.</summary>
    public static async Task CopyInAsync(string directory, string absoluteSource, string fileName, CancellationToken ct)
    {
        await using var source = File.OpenRead(absoluteSource);
        await using var target = File.Create(Path.Combine(directory, fileName));
        await source.CopyToAsync(target, ct);
    }

    /// <summary>Writes the request last, under a temporary name and then renamed into place, which is what makes the job visible to the worker.</summary>
    public static async Task IssueAsync(
        string directory, string stepId, JsonElement parameters, IReadOnlyDictionary<string, string> inputs, TimeSpan timeout, CancellationToken ct)
    {
        var request = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["step"] = stepId,
            ["parameters"] = parameters,
            ["inputs"] = inputs,
            ["timeoutSeconds"] = (int)timeout.TotalSeconds,
        };
        var part = Path.Combine(directory, RequestFile + PartialSuffix);
        await File.WriteAllBytesAsync(part, JsonSerializer.SerializeToUtf8Bytes(request, Json), ct);
        File.Move(part, Path.Combine(directory, RequestFile), overwrite: true);
    }

    /// <summary>
    /// Waits for the worker's answer, reporting progress as it goes. Throws
    /// <see cref="ScanWorkerException"/> when nothing takes the job in time or the job runs past
    /// its limit; a host shutdown's cancellation is let through untouched, the directory left as
    /// it is for the resumed run.
    /// </summary>
    public async Task<ScanJobResult> WaitAsync(string directory, Func<double, string?, CancellationToken, Task> progress, CancellationToken ct)
    {
        var settings = options.Value;
        var began = clock.GetUtcNow();
        var pickupBy = began + settings.PickupWait;
        var limit = began + settings.JobTimeout;
        var lastProgress = (-1d, (string?)null);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(directory, ResultFile)))
            {
                return await ReadResultAsync(directory, ct);
            }

            var now = clock.GetUtcNow();
            var started = File.Exists(Path.Combine(directory, StartedFile));
            if (!started && now >= pickupBy)
            {
                throw new ScanWorkerException(
                    Domain.Surveys.SurveyPipelineFailures.WorkerSilent,
                    $"Nothing took the job in {(int)settings.PickupWait.TotalSeconds} seconds: processing is switched on, but no point-cloud worker is running.");
            }

            if (now >= limit)
            {
                await WithdrawAsync(directory, ct);
                throw new ScanWorkerException(
                    Domain.Surveys.SurveyPipelineFailures.WorkerTimeout,
                    $"The step ran past the {(int)settings.JobTimeout.TotalSeconds} seconds this installation allows one step, and was stopped.",
                    LogTailOf(directory));
            }

            var reported = ReadProgress(directory);
            if (reported is { } p && (p.Fraction, p.Message) != lastProgress)
            {
                lastProgress = (p.Fraction, p.Message);
                await progress(p.Fraction, p.Message, ct);
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>
    /// Calls a job off and waits a moment for the worker to confirm. Answers whether it did; a
    /// job nothing had taken, or one already answered, counts as confirmed.
    /// </summary>
    public async Task<bool> WithdrawAsync(string directory, CancellationToken ct)
    {
        if (!InFlight(directory))
        {
            return true;
        }

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, CancelFile), clock.GetUtcNow().ToString("O"), ct);
        }
        catch (IOException e)
        {
            logger.LogWarning(e, "The point-cloud job in {Directory} could not be called off", directory);
            return false;
        }

        var until = clock.GetUtcNow() + WithdrawalWait;
        while (clock.GetUtcNow() < until)
        {
            if (File.Exists(Path.Combine(directory, ResultFile)))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500, PollInterval.TotalMilliseconds)), ct);
        }

        return false;
    }

    /// <summary>Removes a job directory and everything in it, tolerating a file the worker still holds.</summary>
    public void Forget(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "The point-cloud job directory {Directory} could not be removed", directory);
        }
    }

    private static bool InFlight(string directory) =>
        File.Exists(Path.Combine(directory, RequestFile)) && !File.Exists(Path.Combine(directory, ResultFile));

    private static (double Fraction, string? Message)? ReadProgress(string directory)
    {
        var path = Path.Combine(directory, ProgressFile);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var fraction = root.TryGetProperty("fraction", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetDouble() : 0;
            return (Math.Clamp(fraction, 0, 1), Text(root, "message"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Half-written by the worker at this instant; the next look reads it whole.
            return null;
        }
    }

    private static IReadOnlyList<string> LogTailOf(string directory)
    {
        try
        {
            var path = Path.Combine(directory, LogFile);
            return File.Exists(path) ? File.ReadAllLines(path).TakeLast(15).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static async Task<ScanJobResult> ReadResultAsync(string directory, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(Path.Combine(directory, ResultFile), ct);
        }
        catch (IOException e)
        {
            return Unreadable("the result file could not be read: " + e.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var status = Text(root, "status") switch
            {
                "ok" => ScanJobStatus.Ok,
                "failed" => ScanJobStatus.Failed,
                "refused" => ScanJobStatus.Refused,
                "cancelled" => ScanJobStatus.Cancelled,
                "interrupted" => ScanJobStatus.Interrupted,
                _ => ScanJobStatus.Unreadable,
            };
            var outputs = new List<ScanJobOutput>();
            if (root.TryGetProperty("outputs", out var listed) && listed.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in listed.EnumerateArray())
                {
                    var kind = Text(output, "kind");
                    var file = Text(output, "file");
                    if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(file))
                    {
                        outputs.Add(new ScanJobOutput(
                            kind,
                            file,
                            output.TryGetProperty("sizeBytes", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                            Text(output, "sha256") ?? ""));
                    }
                }
            }

            var tail = new List<string>();
            if (root.TryGetProperty("logTail", out var lines) && lines.ValueKind == JsonValueKind.Array)
            {
                tail.AddRange(lines.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()!));
            }

            var data = root.TryGetProperty("data", out var d) ? d.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
            return new ScanJobResult(
                status,
                Text(root, "reason") ?? "",
                outputs,
                Text(root, "tool") ?? "",
                Text(root, "toolVersion") ?? "",
                root.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0,
                tail,
                data);
        }
        catch (JsonException e)
        {
            return Unreadable("the result file is not JSON: " + e.Message);
        }
    }

    private static ScanJobResult Unreadable(string reason) =>
        new(ScanJobStatus.Unreadable, reason, [], "", "", 0, [], JsonDocument.Parse("{}").RootElement.Clone());

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
