// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Entities;

/// <summary>
/// One file a scan's processing produced or took in, with its lineage: which step made it, with
/// which tool at which version, from which other artefacts, under which parameters.
/// </summary>
/// <remarks>
/// <para>
/// The lineage is what makes a rerun cheap and an upgrade findable. A rerun from a step reuses
/// every artefact whose step, inputs and parameter hash are unchanged instead of computing it
/// again; a tool upgrade finds the files an older version wrote by their tool and version. Both
/// read this row and nothing else, so everything they need is here rather than in the file.
/// </para>
/// <para>
/// The row names its upload always and its model when there is one: the uploaded original is
/// archived before any model exists for it, and an artefact of a failed run has an upload and no
/// model at all. The bytes themselves are a stored file, as every other file is.
/// </para>
/// </remarks>
public class ScanArtifact
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The upload whose run produced or received this file.</summary>
    public Guid SurveyUploadId { get; set; }

    /// <summary>The model this file belongs to, once the run has made one; null before, and null for a run that made none.</summary>
    public Guid? SurveyModelId { get; set; }

    public ScanArtifactKind Kind { get; set; }

    /// <summary>
    /// The file format in one short lower-case word, such as <c>laz</c>, <c>las</c> or
    /// <c>3dtiles-zip</c>: a kind can be written more than one way, and a step that takes a
    /// kind may take only some of its formats.
    /// </summary>
    public required string Format { get; set; }

    /// <summary>The stored file holding the bytes.</summary>
    public Guid FileId { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>The SHA-256 of the bytes, lower-case hex, so an input can be matched by content and a copy told from a change.</summary>
    public required string Sha256 { get; set; }

    /// <summary>The step that produced the file, by its registry id, such as <c>pointcloud.inspect</c>.</summary>
    public required string StepId { get; set; }

    /// <summary>The tool the step ran, as the step names it, such as <c>py3dtiles</c>.</summary>
    public required string Tool { get; set; }

    /// <summary>The tool's version as it reported it, when known.</summary>
    public string? ToolVersion { get; set; }

    /// <summary>The hash of the step's resolved parameters, as the recipe computes it, so two runs with the same settings are known to be the same.</summary>
    public required string ParamsHash { get; set; }

    /// <summary>The artefacts the step read to make this one, in no particular order; empty for an uploaded original.</summary>
    public Guid[] InputArtifactIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
