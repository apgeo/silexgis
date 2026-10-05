// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Stands in for the service that makes tiles, by being the far side of the directory the two
/// meet on: it claims a request the way that service does, and leaves whatever answer the test
/// wants beside it.
/// </summary>
internal sealed class FakeBakeWorker : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly Task watching;

    public FakeBakeWorker(
        string spoolRoot, Action<string, IReadOnlyDictionary<string, string>> bake)
    {
        Directory.CreateDirectory(spoolRoot);
        watching = Task.Run(async () =>
        {
            while (!stopping.IsCancellationRequested)
            {
                foreach (var directory in Directory.GetDirectories(spoolRoot))
                {
                    var request = Path.Combine(directory, "request");
                    if (!File.Exists(request)
                        || File.Exists(Path.Combine(directory, "result"))
                        || File.Exists(Path.Combine(directory, "started")))
                    {
                        continue;
                    }

                    File.WriteAllText(Path.Combine(directory, "started"), "now");
                    bake(directory, Fields(File.ReadAllText(request)));
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

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        try
        {
            await watching;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            // Stopping while a directory is being removed underneath it is an ordinary end.
        }

        stopping.Dispose();
    }

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                fields[line[..separator]] = line[(separator + 1)..].TrimEnd('\r');
            }
        }

        return fields;
    }
}
