// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>
/// Where a run stopped for a step done on another computer: the step, and the artefacts it was to
/// read, in the order the run would have given them to the worker.
/// </summary>
/// <param name="StepId">The step's registry id.</param>
/// <param name="InputArtifactIds">
/// The artefacts the step reads. An output uploaded for it is recorded as made from exactly
/// these, so the run that carries on matches it as it would match a worker's product of the same
/// step, inputs and parameters, and reuses it rather than asking again.
/// </param>
public sealed record ScanExternalWait(string StepId, IReadOnlyList<Guid> InputArtifactIds);

/// <summary>
/// The file names a step done elsewhere takes its products back under. Each kind of product has
/// a fixed name, so the download's instructions can say exactly what to send, and a file sent
/// under a name no output of the waiting step has is refused before anything of it is kept.
/// </summary>
public static class ScanExternalOutputs
{
    /// <summary>The name an HDMapping session is sent back under: the session folder zipped, <c>session.mjs</c> at its root.</summary>
    public const string SessionName = "session.zip";

    /// <summary>The names a product of this kind may be sent back under; none for a kind no step is done elsewhere to make.</summary>
    public static IReadOnlyList<string> NamesFor(ScanArtifactKind kind) => kind switch
    {
        ScanArtifactKind.HdmSession => [SessionName],
        ScanArtifactKind.PointCloudLaz => ["cloud.laz", "cloud.las"],
        ScanArtifactKind.Trajectory => ["trajectory.csv"],
        ScanArtifactKind.Report => ["report.json"],
        _ => [],
    };

    /// <summary>
    /// The kind of the step's output a file sent under this name is, or null when the step has no
    /// output by that name. Names compare without regard to case: a file renamed on a desktop
    /// whose file system ignores case is still the file asked for.
    /// </summary>
    public static ScanArtifactKind? KindOf(IScanStep step, string name)
    {
        ArgumentNullException.ThrowIfNull(step);
        foreach (var kind in step.Outputs)
        {
            if (NamesFor(kind).Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>The format a file sent under an accepted name is stored as: its extension, lower case, without the dot.</summary>
    public static string FormatOf(string name) =>
        Path.GetExtension(name).TrimStart('.').ToLowerInvariant();

    /// <summary>
    /// Whether a member name inside a sent archive stays inside it: not rooted, no drive, no parent
    /// step, no NUL. The worker that later unpacks the archive refuses the same names; checking at
    /// the door means a bad archive is refused while the person who sent it is still there.
    /// </summary>
    public static bool IsContainedMember(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] is '/' or '\\' || name.Contains(':') || name.Contains('\0'))
        {
            return false;
        }

        return !name.Replace('\\', '/').Split('/').Contains("..");
    }
}
