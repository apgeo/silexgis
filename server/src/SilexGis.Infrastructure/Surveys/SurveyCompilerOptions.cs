// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Whether and how this installation compiles the surveys field devices upload.
/// </summary>
public sealed class SurveyCompilerOptions
{
    public const string SectionName = "SurveyCompiler";

    /// <summary>The installation compiles nothing; uploads are archived and their archive read.</summary>
    public const string ModeNone = "none";

    /// <summary>Therion runs on this machine, inside a sandbox, started by the application.</summary>
    public const string ModeLocal = "local";

    /// <summary>Therion runs in a compiler service of its own, which the application hands each project to.</summary>
    public const string ModeService = "service";

    /// <summary>
    /// Where uploads are compiled: <see cref="ModeNone"/> (the default), <see cref="ModeLocal"/> or
    /// <see cref="ModeService"/>. Anything else is read as none, so a typing mistake never starts a
    /// compiler nobody chose.
    /// </summary>
    public string Mode { get; set; } = ModeNone;

    /// <summary>The compiler service's address in service mode, such as <c>http://therion:3100</c>.</summary>
    public string Url { get; set; } = "";

    /// <summary>The Therion program to run in local mode. Empty means the first <c>therion</c> on the search path.</summary>
    public string TherionPath { get; set; } = "";

    /// <summary>The sandbox program (bubblewrap) local mode starts Therion through. Empty means the first <c>bwrap</c> on the search path.</summary>
    public string BwrapPath { get; set; } = "";

    /// <summary>
    /// Whether local mode may run Therion without the sandbox when bubblewrap cannot be found.
    /// Off by default, and then local mode refuses to compile: Therion run over an uploaded survey
    /// can read any file the server can, and quotes what it read into a log the uploader sees.
    /// </summary>
    public bool AllowUnsandboxed { get; set; }

    /// <summary>How long one compile may run, in seconds, before it is stopped.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// The address space one compile may use, in bytes (4 GB by default). Therion itself needs tens
    /// of megabytes; the TeX programs it starts for drawings need more.
    /// </summary>
    public long MaxMemoryBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>The largest single file one compile may write, in bytes (1 GB by default).</summary>
    public long MaxOutputFileBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>
    /// The directory compile runs are made in, one subdirectory per run, deleted when the run ends.
    /// Empty means <c>survey-work</c> under the file store's root, which is on the volume the
    /// installation already sized for files — never the system's temporary directory, which on
    /// many machines is memory.
    /// </summary>
    public string WorkDir { get; set; } = "";

    /// <summary>
    /// The most a compiler service may hand back for one compile, in bytes (512 MB by default):
    /// the log and every output together. A larger answer is refused rather than stored.
    /// </summary>
    public long MaxOutputBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Whether <see cref="Mode"/> names local mode.</summary>
    public bool IsLocal => string.Equals(Mode?.Trim(), ModeLocal, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <see cref="Mode"/> names service mode and an address is given.</summary>
    public bool IsService =>
        string.Equals(Mode?.Trim(), ModeService, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Url);

    /// <summary>The compile time limit, held to between ten seconds and two hours.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 10, 7200));

    /// <summary>The memory limit, never below 256 MB.</summary>
    public long MemoryCap => Math.Max(256L * 1024 * 1024, MaxMemoryBytes);

    /// <summary>The written-file limit, never below 1 MB.</summary>
    public long OutputFileCap => Math.Max(1024L * 1024, MaxOutputFileBytes);

    /// <summary>The limit on what a compiler service hands back, never below 1 MB.</summary>
    public long OutputCap => Math.Max(1024L * 1024, MaxOutputBytes);
}
