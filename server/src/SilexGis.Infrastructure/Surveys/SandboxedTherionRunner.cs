// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>How one Therion run ended.</summary>
/// <param name="ExitCode">The exit code of what was started (Therion's own, through the wrappers), or -1 when it never ran.</param>
/// <param name="LogPath">The log this run wrote, or null when it wrote none.</param>
public sealed record TherionRun(int ExitCode, string? LogPath);

/// <summary>
/// Starts Therion over a project built around an uploaded survey, inside a sandbox. The one class
/// in the application that may start the compiler; an architecture test holds every other class
/// to that.
/// </summary>
/// <remarks>
/// <para>
/// Therion opens any path a survey names and writes what it read into its log, and drawings make it
/// run TeX and MetaPost over text derived from the upload. So it is started through bubblewrap with
/// a filesystem of its own: the system's programs and libraries read-only, the few configuration
/// files Therion and TeX read, and the project directory as the one place it can write — bound at
/// its own path, so the full paths the compiler is given mean the same inside. The sandbox has no
/// network and none of the server's environment (which holds the database password), and its
/// <c>/tmp</c> is a directory inside the project rather than the host's, which on many machines is
/// memory and which Therion fills with a working directory per run. Around it, <c>prlimit</c>
/// bounds the address space, processor time and the size of any one written file, and
/// <c>timeout</c> stops the whole run a little after the caller's own deadline in case the caller
/// itself is stuck.
/// </para>
/// <para>
/// Without bubblewrap the runner runs Therion directly with the same limits and the same emptied
/// environment — only when the installation said so, because the filesystem boundary is the part
/// that is then missing.
/// </para>
/// </remarks>
public sealed class SandboxedTherionRunner
{
    /// <summary>The directory, inside the project, the sandbox's <c>/tmp</c> is.</summary>
    public const string ScratchDirectoryName = ".tmp";

    /// <summary>The log file each run writes, in the project directory.</summary>
    public const string LogFileName = TherionCompiler.DefaultLogFileName;

    private const string SystemPath = "/usr/bin:/bin";

    // Read-only, when they exist: what Therion and the TeX programs it starts read at run time. The
    // alternatives directory is where several TeX programs' links in /usr/bin point.
    private static readonly string[] ReadOnlyConfiguration =
    [
        "/etc/therion.ini", "/etc/texmf", "/etc/ld.so.cache", "/etc/alternatives", "/etc/fonts", "/var/lib/texmf",
    ];

    private readonly string therionPath;
    private readonly string? bwrapPath;
    private readonly string? prlimitPath;
    private readonly string? timeoutPath;
    private readonly SurveyCompilerOptions options;

    public SandboxedTherionRunner(string therionPath, string? bwrapPath, SurveyCompilerOptions options)
    {
        this.therionPath = therionPath;
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
    /// Compiles <paramref name="configFileName"/> inside <paramref name="projectDirectory"/>, reporting
    /// each line Therion and its children print. Cancelling stops the whole run and throws.
    /// </summary>
    public async Task<TherionRun> RunAsync(
        string projectDirectory, string configFileName, TimeSpan timeout, Action<string> onLine, CancellationToken ct)
    {
        var project = Path.GetFullPath(projectDirectory);
        var scratch = Path.Combine(project, ScratchDirectoryName);
        Directory.CreateDirectory(scratch);

        var compiler = new TherionCompiler(new ConfiguredTherion(therionPath));
        var run = await compiler.CompileAsync(
            Path.Combine(project, configFileName),
            new TherionCompileOptions
            {
                LogFile = LogFileName,
                Quiet = true,
                ConfigureProcess = start => Wrap(start, project, scratch, timeout),
            },
            new LineSink(onLine),
            ct);

        return new TherionRun(run.ExitCode, run.LogPath);
    }

    /// <summary>
    /// The compiler's own words about which version it is, such as <c>therion 6.3.4</c>, or null
    /// when it does not answer. Runs <c>therion --version</c>, which reads no survey.
    /// </summary>
    public static string? ProbeVersion(string therionPath)
    {
        try
        {
            var start = new ProcessStartInfo(therionPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--version");
            ScrubEnvironment(start);

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            var first = process.StandardOutput.ReadLine();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            // "therion 6.3.4 (2025-04-08)": the name and the version, without the date.
            var words = (first ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 && words[0] == "therion" ? $"therion {words[1]}" : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private ProcessStartInfo Wrap(ProcessStartInfo therion, string project, string scratch, TimeSpan timeout)
    {
        var command = new List<string>();

        if (prlimitPath is not null)
        {
            command.AddRange(
            [
                prlimitPath,
                "--as=" + options.MemoryCap.ToString(CultureInfo.InvariantCulture),
                "--cpu=" + Seconds(timeout + TimeSpan.FromSeconds(30)),
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

            // A Therion installed somewhere else than under /usr is visible at its own path.
            var therionDirectory = Path.GetDirectoryName(Path.GetFullPath(therionPath))!;
            if (!therionDirectory.StartsWith("/usr/", StringComparison.Ordinal))
            {
                command.AddRange(["--ro-bind", therionDirectory, therionDirectory]);
            }

            command.AddRange(
            [
                "--proc", "/proc",
                "--dev", "/dev",
                "--bind", project, project,
                "--bind", scratch, "/tmp",
                "--setenv", "PATH", SystemPath,
                "--setenv", "HOME", "/tmp",
                "--setenv", "TMPDIR", "/tmp",
                "--setenv", "TEXMFVAR", "/tmp/texmf-var",
                "--setenv", "LANG", "C.UTF-8",
                "--chdir", project,
            ]);
        }

        command.Add(therion.FileName);
        command.AddRange(therion.ArgumentList);

        var wrapped = new ProcessStartInfo(command[0])
        {
            WorkingDirectory = project,
            RedirectStandardOutput = therion.RedirectStandardOutput,
            RedirectStandardError = therion.RedirectStandardError,
            RedirectStandardInput = therion.RedirectStandardInput,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in command.Skip(1))
        {
            wrapped.ArgumentList.Add(argument);
        }

        // The server's environment holds its secrets; none of it reaches the compiler or the
        // wrappers around it. Unsandboxed, the run's temporary files still go to the project.
        ScrubEnvironment(wrapped);
        wrapped.Environment["LANG"] = "C.UTF-8";
        if (bwrapPath is null)
        {
            wrapped.Environment["HOME"] = scratch;
            wrapped.Environment["TMPDIR"] = scratch;
            wrapped.Environment["TEXMFVAR"] = Path.Combine(scratch, "texmf-var");
            if (OperatingSystem.IsWindows())
            {
                wrapped.Environment["TMP"] = scratch;
                wrapped.Environment["TEMP"] = scratch;
            }
        }

        return wrapped;
    }

    /// <summary>
    /// Takes the server's own settings out of a child's environment. On Linux and macOS nothing of
    /// it is kept but a system search path, which is all Therion and the TeX programs need. Windows
    /// programs cannot start without the system's own variables (<c>SystemRoot</c>, the search path
    /// that finds MetaPost and TeX), so there the inherited environment is kept and only what
    /// belongs to the application is removed: its <c>SILEXGIS__</c> settings, connection strings
    /// and the host's <c>ASPNETCORE_</c>/<c>DOTNET_</c> settings.
    /// </summary>
    private static void ScrubEnvironment(ProcessStartInfo start)
    {
        if (!OperatingSystem.IsWindows())
        {
            start.Environment.Clear();
            start.Environment["PATH"] = SystemPath;
            return;
        }

        string[] applicationPrefixes = ["SILEXGIS", "ConnectionStrings", "ASPNETCORE_", "DOTNET_"];
        foreach (var key in start.Environment.Keys.ToList())
        {
            if (applicationPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                start.Environment.Remove(key);
            }
        }
    }

    private static string Seconds(TimeSpan span) =>
        ((long)Math.Ceiling(span.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Answers the compiler's question of where Therion is with the one path this installation resolved.</summary>
    private sealed class ConfiguredTherion(string path) : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken ct = default) =>
            ValueTask.FromResult<ToolInfo?>(new ToolInfo(toolId, path, null, "configured"));
    }

    /// <summary>Hands each printed line on at once, on the thread that read it.</summary>
    private sealed class LineSink(Action<string> onLine) : IProgress<CompilerOutputLine>
    {
        public void Report(CompilerOutputLine value) => onLine(value.Text);
    }
}

/// <summary>Finds a program the way a shell would, without starting anything.</summary>
public static class ExecutableSearch
{
    /// <summary>The configured path when one is given and exists, else the first <paramref name="name"/> on the search path.</summary>
    public static string? Resolve(string? configured, string name)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        return OnPath(name);
    }

    /// <summary>
    /// The first file called <paramref name="name"/> in a directory of the search path, or null. On
    /// Windows a name without an extension is also tried with each of the extensions <c>PATHEXT</c>
    /// lists (<c>therion</c> is <c>therion.exe</c> there).
    /// </summary>
    public static string? OnPath(string name)
    {
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var directories = OperatingSystem.IsWindows() ? path : path.Concat(["/usr/bin", "/bin", "/usr/local/bin"]);
        var names = CandidateNames(name);
        foreach (var directory in directories)
        {
            foreach (var candidateName in names)
            {
                var candidate = Path.Combine(directory, candidateName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static List<string> CandidateNames(string name)
    {
        var names = new List<string> { name };
        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(name)))
        {
            var extensions = Environment.GetEnvironmentVariable("PATHEXT") is { Length: > 0 } listed
                ? listed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [".EXE", ".CMD", ".BAT", ".COM"];
            names.AddRange(extensions.Select(e => name + e.ToLowerInvariant()));
        }

        return names;
    }
}
