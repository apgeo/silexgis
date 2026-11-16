// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>
/// How the installation runs WhiteboxTools, the terrain-analysis program behind the analyses the
/// application's own raster library has no equivalent of: not at all (the default), by starting the
/// program itself inside a sandbox, or by handing the work to the analysis service, a container of
/// its own beside the application.
/// </summary>
public sealed class WhiteboxOptions
{
    public const string SectionName = "Analysis:Whitebox";

    /// <summary>The program is not run; the analyses that need it say they are not available.</summary>
    public const string ModeNone = "none";

    /// <summary>The application starts the program itself, inside a sandbox.</summary>
    public const string ModeLocal = "local";

    /// <summary>The application hands each run to the analysis service named by <see cref="Url"/>.</summary>
    public const string ModeService = "service";

    /// <summary><c>none</c> (the default), <c>local</c> or <c>service</c>; anything else stops the application at start.</summary>
    public string Mode { get; set; } = ModeNone;

    /// <summary>The analysis service's address, such as <c>http://whitebox:3200</c>; required in service mode.</summary>
    public string Url { get; set; } = "";

    /// <summary>The <c>whitebox_tools</c> program; empty means the first one on the search path.</summary>
    public string WhiteboxPath { get; set; } = "";

    /// <summary>The bubblewrap program the local mode sandboxes runs with; empty means the first one on the search path.</summary>
    public string BwrapPath { get; set; } = "";

    /// <summary>
    /// Whether local mode may run the program without bubblewrap — with the same limits and emptied
    /// environment but without a filesystem of its own. Off by default: on Linux a missing
    /// bubblewrap then makes the program unavailable. Windows has no bubblewrap and needs it on.
    /// </summary>
    public bool AllowUnsandboxed { get; set; }

    /// <summary>The longest one run may take, in seconds (30 minutes by default; 10 seconds to 6 hours).</summary>
    public int TimeoutSeconds { get; set; } = 1800;

    /// <summary>The address space one run may use, in bytes (4 GB by default, never below 256 MB).</summary>
    public long MaxMemoryBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>The largest single file one run may write, in bytes (2 GB by default, never below 1 MB).</summary>
    public long MaxOutputFileBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>The most the outputs of one run may hold together, in bytes (4 GB by default, never below 1 MB).</summary>
    public long MaxOutputBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    public bool IsLocal => Is(ModeLocal);

    public bool IsService => Is(ModeService);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 10, 6 * 3600));

    public long MemoryCap => Math.Max(256L * 1024 * 1024, MaxMemoryBytes);

    public long OutputFileCap => Math.Max(1024L * 1024, MaxOutputFileBytes);

    public long OutputCap => Math.Max(1024L * 1024, MaxOutputBytes);

    private bool Is(string mode) => string.Equals((Mode ?? "").Trim(), mode, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses, at start, a mode the application does not know and a service mode without a usable
/// address. The survey compiler's switch reads a mistyped mode as "none"; here that would turn an
/// operator's typo into analyses that silently say "not available", so the application does not
/// start instead and says which setting is wrong.
/// </summary>
public sealed class WhiteboxOptionsValidator : IValidateOptions<WhiteboxOptions>
{
    public ValidateOptionsResult Validate(string? name, WhiteboxOptions options)
    {
        var mode = (options.Mode ?? "").Trim();
        if (mode.Length > 0
            && !string.Equals(mode, WhiteboxOptions.ModeNone, StringComparison.OrdinalIgnoreCase)
            && !options.IsLocal
            && !options.IsService)
        {
            return ValidateOptionsResult.Fail(
                $"SILEXGIS__Analysis__Whitebox__Mode is '{mode}'; it must be 'none', 'local' or 'service'.");
        }

        if (options.IsService
            && (!Uri.TryCreate(options.Url?.Trim(), UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
        {
            return ValidateOptionsResult.Fail(
                "SILEXGIS__Analysis__Whitebox__Mode is 'service', so SILEXGIS__Analysis__Whitebox__Url must be "
                + "the analysis service's http(s) address, such as http://whitebox:3200.");
        }

        if (options.TimeoutSeconds is < 10 or > 6 * 3600)
        {
            return ValidateOptionsResult.Fail(
                "SILEXGIS__Analysis__Whitebox__TimeoutSeconds must be between 10 and 21600.");
        }

        return ValidateOptionsResult.Success;
    }
}
