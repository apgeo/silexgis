// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>
/// Starts WhiteboxTools over the files of one run directory, inside a sandbox. The one class in the
/// application that starts the program.
/// </summary>
/// <remarks>
/// <para>
/// The same boundary the survey compiler's local mode draws, for the same reasons: through
/// bubblewrap the program gets the system's programs and libraries read-only, its own directory
/// read-only, and the run directory as the one place it can read inputs from and write to — bound
/// at its own path, so the full paths it is given mean the same inside. No network (two of the
/// programs it can start download and install software), none of the server's environment (which
/// holds the database password), and a <c>/tmp</c> that is a directory inside the run rather than
/// the host's. Around it, <c>prlimit</c> bounds the address space, processor time and the size of
/// any one written file, and <c>timeout</c> stops the whole run a little after the caller's own
/// deadline in case the caller itself is stuck.
/// </para>
/// <para>
/// Files are always handed over as full paths. The program prefixes a bare name with a working
/// directory read from a <c>settings.json</c> beside it — the vendor ships one naming a directory
/// on its author's own machine — and the option that changes that directory rewrites that file,
/// which the read-only sandbox refuses and which concurrent runs would race on. The program splits
/// each argument at <c>=</c> and drops quotes, so a run directory whose path holds either is refused
/// rather than handed over mangled.
/// </para>
/// </remarks>
public sealed class SandboxedWhiteboxRunner
{
    /// <summary>The program's file name.</summary>
    public const string ProgramName = "whitebox_tools";

    /// <summary>The directory, inside the run directory, the sandbox's <c>/tmp</c> is.</summary>
    public const string ScratchDirectoryName = ".tmp";

    /// <summary>How much of what the program printed is kept.</summary>
    public const int OutputTailLength = 4096;

    private const string SystemPath = "/usr/bin:/bin";

    private static readonly string[] ReadOnlyConfiguration = ["/etc/ld.so.cache"];

    private readonly string programPath;
    private readonly string? bwrapPath;
    private readonly string? prlimitPath;
    private readonly string? timeoutPath;
    private readonly WhiteboxOptions options;

    public SandboxedWhiteboxRunner(string programPath, string? bwrapPath, WhiteboxOptions options)
    {
        this.programPath = Path.GetFullPath(programPath);
        this.bwrapPath = bwrapPath;
        this.options = options;
        // util-linux and coreutils programs; Windows has a timeout.exe of its own that is a
        // different program entirely, so neither is looked for there.
        prlimitPath = OperatingSystem.IsWindows() ? null : ExecutableSearch.OnPath("prlimit");
        timeoutPath = OperatingSystem.IsWindows() ? null : ExecutableSearch.OnPath("timeout");
    }

    /// <summary>Whether runs go through the sandbox.</summary>
    public bool Sandboxed => bwrapPath is not null;

    /// <summary>
    /// Runs <paramref name="tool"/> in <paramref name="runDirectory"/>, keeping the end of what it
    /// printed. Cancelling stops the whole run and throws.
    /// </summary>
    public async Task<(int ExitCode, string OutputTail)> RunAsync(
        string runDirectory, string tool, IReadOnlyList<WhiteboxArgument> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var start = Command(runDirectory, tool, arguments, timeout);
        Directory.CreateDirectory(Path.Combine(Path.GetFullPath(runDirectory), ScratchDirectoryName));

        var tail = new OutputTail(OutputTailLength);
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => tail.Add(e.Data);
        process.ErrorDataReceived += (_, e) => tail.Add(e.Data);
        try
        {
            if (!process.Start())
            {
                return (-1, "");
            }
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new WhiteboxUnavailableException("WhiteboxTools could not be started.", e);
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended between the cancel and the kill.
            }

            throw;
        }

        // The exit wait returns once the program ends; the last lines arrive with the end of the pipes.
        process.WaitForExit();
        return (process.ExitCode, tail.Text);
    }

    /// <summary>
    /// The program's own words about which version it is, such as <c>WhiteboxTools 2.4.0</c>, or
    /// null when it does not answer within <paramref name="limit"/> (five seconds when not given).
    /// Runs <c>whitebox_tools --version</c>, which reads no input.
    /// </summary>
    public static string? ProbeVersion(string programPath, TimeSpan? limit = null)
    {
        var allowed = limit ?? TimeSpan.FromSeconds(5);
        try
        {
            var start = new ProcessStartInfo(programPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--version");
            SandboxedTherionRunner.ScrubEnvironment(start);

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var clock = Stopwatch.StartNew();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            process.StandardInput.Close();
            var firstLine = process.StandardOutput.ReadLineAsync();
            if (!firstLine.Wait(allowed))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var first = firstLine.Result;
            _ = process.StandardOutput.ReadToEndAsync();
            var left = allowed - clock.Elapsed;
            if (!process.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return VersionOf(first);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            return null;
        }
    }

    /// <summary>
    /// "WhiteboxTools v2.4.0 (c) Dr. John Lindsay 2017-2023": the name and the version, without the
    /// rest; null for a line that does not begin that way.
    /// </summary>
    public static string? VersionOf(string? firstLine)
    {
        var words = (firstLine ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !words[0].StartsWith("WhiteboxTools", StringComparison.Ordinal))
        {
            return null;
        }

        var version = words[1].TrimStart('v', 'V');
        return version.Length > 0 && version.Length <= 32 && version.All(c => char.IsAsciiDigit(c) || c == '.')
            ? $"WhiteboxTools {version}"
            : null;
    }

    /// <summary>The whole command a run starts: the limits, the sandbox, then the program and its arguments.</summary>
    internal ProcessStartInfo Command(string runDirectory, string tool, IReadOnlyList<WhiteboxArgument> arguments, TimeSpan timeout)
    {
        var run = Path.GetFullPath(runDirectory);
        if (run.IndexOfAny(['=', '"', '\'']) >= 0)
        {
            throw new InvalidOperationException(
                "WhiteboxTools cannot be given a run directory whose path holds '=' or a quote; choose a work directory without them.");
        }

        var scratch = Path.Combine(run, ScratchDirectoryName);
        var command = new List<string>();

        if (prlimitPath is not null)
        {
            command.AddRange(
            [
                prlimitPath,
                "--as=" + options.MemoryCap.ToString(CultureInfo.InvariantCulture),
                "--cpu=" + Seconds(TimeSpan.FromTicks(timeout.Ticks * Math.Max(1, Environment.ProcessorCount)) + TimeSpan.FromSeconds(30)),
                "--fsize=" + options.OutputFileCap.ToString(CultureInfo.InvariantCulture),
                "--",
            ]);
        }

        if (timeoutPath is not null)
        {
            command.AddRange([timeoutPath, "--kill-after=10", Seconds(timeout + TimeSpan.FromSeconds(15))]);
        }

        if (bwrapPath is not null)
        {
            command.AddRange(
            [
                bwrapPath,
                "--unshare-all", "--die-with-parent", "--new-session", "--clearenv",
                "--ro-bind", "/usr", "/usr",
                "--symlink", "usr/bin", "/bin",
                "--symlink", "usr/lib", "/lib",
                "--symlink", "usr/lib64", "/lib64",
                "--symlink", "usr/sbin", "/sbin",
            ]);
            foreach (var path in ReadOnlyConfiguration)
            {
                command.AddRange(["--ro-bind-try", path, path]);
            }

            // The program reads its settings.json from its own directory, so that directory is
            // visible, read-only, at its own path wherever it was installed.
            var programDirectory = Path.GetDirectoryName(programPath)!;
            if (!programDirectory.StartsWith("/usr/", StringComparison.Ordinal))
            {
                command.AddRange(["--ro-bind", programDirectory, programDirectory]);
            }

            command.AddRange(
            [
                "--proc", "/proc",
                "--dev", "/dev",
                "--bind", run, run,
                "--bind", scratch, "/tmp",
                "--setenv", "PATH", SystemPath,
                "--setenv", "HOME", "/tmp",
                "--setenv", "TMPDIR", "/tmp",
                "--setenv", "LANG", "C.UTF-8",
                "--chdir", run,
            ]);
        }

        command.Add(programPath);
        command.Add("--run=" + tool);
        foreach (var argument in arguments)
        {
            command.Add(argument.Kind switch
            {
                WhiteboxArgumentKind.Flag => "--" + argument.Name,
                WhiteboxArgumentKind.Input or WhiteboxArgumentKind.Output => $"--{argument.Name}={Path.Combine(run, argument.Value!)}",
                _ => $"--{argument.Name}={argument.Value}",
            });
        }

        var wrapped = new ProcessStartInfo(command[0])
        {
            WorkingDirectory = run,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in command.Skip(1))
        {
            wrapped.ArgumentList.Add(argument);
        }

        // The server's environment holds its secrets; none of it reaches the program or the
        // wrappers around it. Unsandboxed, the run's temporary files still go to the run directory.
        SandboxedTherionRunner.ScrubEnvironment(wrapped);
        wrapped.Environment["LANG"] = "C.UTF-8";
        if (bwrapPath is null)
        {
            wrapped.Environment["HOME"] = scratch;
            wrapped.Environment["TMPDIR"] = scratch;
            if (OperatingSystem.IsWindows())
            {
                wrapped.Environment["TMP"] = scratch;
                wrapped.Environment["TEMP"] = scratch;
            }
        }

        return wrapped;
    }

    private static string Seconds(TimeSpan span) =>
        ((long)Math.Ceiling(span.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    /// <summary>The last characters of what a run printed, kept as lines arrive from two pipes at once.</summary>
    private sealed class OutputTail(int length)
    {
        private readonly Lock gate = new();
        private readonly StringBuilder text = new();

        public void Add(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (gate)
            {
                text.Append(line).Append('\n');
                if (text.Length > length * 2)
                {
                    text.Remove(0, text.Length - length);
                }
            }
        }

        public string Text
        {
            get
            {
                lock (gate)
                {
                    return text.Length > length ? text.ToString(text.Length - length, length) : text.ToString();
                }
            }
        }
    }
}
