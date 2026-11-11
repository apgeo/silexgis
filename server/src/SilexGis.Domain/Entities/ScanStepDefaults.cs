// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The last default parameters a processing worker announced for one step, kept so the
/// application still knows a step's parameter schema while no worker is running.
/// </summary>
/// <remarks>
/// A worker dumps the defaults of the program it runs every time it starts, into the directory it
/// shares with the application. That file is the truth while it is there; this row is the copy the
/// application keeps of it, refreshed whenever the file says something new, so editing and
/// importing a profile still work with the worker stopped, its volume recreated, or the stack
/// moved. The schema itself is derived from the values on every read and never stored, so it can
/// never disagree with them.
/// </remarks>
public class ScanStepDefaults
{
    /// <summary>The registered step these defaults are for.</summary>
    public required string StepId { get; set; }

    /// <summary>The version the worker announced, as it names the step's tool (program and build).</summary>
    public required string ToolVersion { get; set; }

    /// <summary>The program's own version word, as its dump names it.</summary>
    public string? SoftwareVersion { get; set; }

    /// <summary>The default values, as one JSON object in the program's own sections.</summary>
    public string Values { get; set; } = "{}";

    /// <summary>The program's own text of the dump, when it has one (HDMapping writes TOML).</summary>
    public string? NativeText { get; set; }

    /// <summary>When the worker wrote the file this copy was taken from, as the file says.</summary>
    public DateTimeOffset? WrittenAt { get; set; }

    /// <summary>When the application took this copy.</summary>
    public DateTimeOffset StoredAt { get; set; }
}
