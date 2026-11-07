// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Stands in for the point-cloud worker by being the far side of the hand-off directory: it
/// takes a job the way the worker does — <c>started</c> written, then whatever the test's answer
/// leaves beside the request — and writes nothing else. A test with no answer set is a worker
/// that never comes.
/// </summary>
internal sealed class CannedScanWorker : IAsyncDisposable
{
    private readonly string jobsRoot;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task watching;
    private int taken;

    /// <summary>What to do with a job: the directory and the request as written. Null ignores every job.</summary>
    public Func<string, JsonElement, Task>? Answer { get; set; }

    /// <summary>How many jobs this worker has taken.</summary>
    public int Taken => Volatile.Read(ref taken);

    /// <summary>The step of every job taken, in order.</summary>
    public List<string> Steps { get; } = [];

    public CannedScanWorker(string handoffRoot)
    {
        jobsRoot = Path.Combine(handoffRoot, "jobs");
        Directory.CreateDirectory(jobsRoot);
        watching = Task.Run(async () =>
        {
            while (!stopping.IsCancellationRequested)
            {
                if (Answer is { } answer)
                {
                    foreach (var directory in Directory.GetDirectories(jobsRoot))
                    {
                        var request = Path.Combine(directory, "request.json");
                        if (!File.Exists(request)
                            || File.Exists(Path.Combine(directory, "result.json"))
                            || File.Exists(Path.Combine(directory, "started")))
                        {
                            continue;
                        }

                        File.WriteAllText(Path.Combine(directory, "started"), "now");
                        using var document = JsonDocument.Parse(File.ReadAllBytes(request));
                        Interlocked.Increment(ref taken);
                        lock (Steps)
                        {
                            Steps.Add(document.RootElement.GetProperty("step").GetString()!);
                        }

                        await answer(directory, document.RootElement.Clone());
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), stopping.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    /// <summary>Writes the capabilities file the application reads to know a worker is there.</summary>
    public static void Announce(string handoffRoot, params string[] stepIds)
    {
        Directory.CreateDirectory(handoffRoot);
        var steps = stepIds.Select(id => new { id, tool = id.StartsWith("py3dtiles", StringComparison.Ordinal) ? "py3dtiles" : "laspy", version = "test-1.0" });
        File.WriteAllText(Path.Combine(handoffRoot, "capabilities.json"), JsonSerializer.Serialize(new { worker = "canned", steps }));
    }

    /// <summary>Writes one product into the job directory and answers how the result should name it.</summary>
    public static object Output(string directory, string kind, string file, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(directory, file), bytes);
        return new { kind, file, sizeBytes = bytes.LongLength, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }

    /// <summary>Writes the result the way the worker does: under a temporary name, then renamed into place.</summary>
    public static void Finish(string directory, string status, object[] outputs, object? data = null, string reason = "", string tool = "laspy", string toolVersion = "test-1.0")
    {
        var result = new
        {
            status,
            reason,
            outputs,
            tool,
            toolVersion,
            seconds = 1.5,
            logTail = new[] { "line one", "line two" },
            data = data ?? new { },
        };
        var part = Path.Combine(directory, "result.json.part");
        File.WriteAllText(part, JsonSerializer.Serialize(result));
        File.Move(part, Path.Combine(directory, "result.json"), overwrite: true);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        try
        {
            await watching;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
        }

        stopping.Dispose();
    }
}
