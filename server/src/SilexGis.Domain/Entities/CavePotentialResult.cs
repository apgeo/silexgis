// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Entities;

/// <summary>Where one run of a cave-potential model has got to.</summary>
/// <remarks>Stored as a number: appended to, never renumbered.</remarks>
public enum CavePotentialResultStatus : short
{
    Queued = 0,
    Computing = 1,
    Ready = 2,
    Failed = 3,
}

/// <summary>
/// One run of a cave-potential model over one area at one cell size: what was asked, who for,
/// what it read, and the map it produced.
/// </summary>
/// <remarks>
/// <para>
/// <b>It records the model as run, not a pointer to it.</b> A model is edited and deleted after it
/// has been run, and a map whose recipe has since changed would be a picture nobody can account
/// for. So the method and the definition are copied here when the run is asked for, and the model
/// it came from is kept only as a name and a link that may go.
/// </para>
/// <para>
/// <b>It carries no owner, group or visibility, on purpose.</b> Who may read a result is not
/// something its author chooses. A map weighed from recorded positions shows where those positions
/// are, so it is readable by exactly the accounts that may read and place every feature it was
/// computed from — asked afresh at each reading, of the account it was run for as of anyone else —
/// and <see cref="EvidenceFeatureIds"/> is what that question is asked about. A map weighed from
/// the ground alone names no feature here and is read by whoever may read the terrain. Either way
/// the reader must also be able to place the area it was run over, whose outline the map draws.
/// </para>
/// <para>
/// <b>The rights it ran under</b> are the account's own at the moment the run was computed, read
/// from its rows then and not from anything stored when it was asked for. What is kept of them is
/// what they let the run read: the account, whether it was a full administrator, every feature
/// that went in, and how many of those were location-protected.
/// </para>
/// </remarks>
public class CavePotentialResult : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The model this was run from, while it still exists.</summary>
    public Guid? ModelId { get; set; }

    /// <summary>The model's name when it was run.</summary>
    public required string ModelName { get; set; }

    /// <summary>How the model run arrived at its weights. Says how <see cref="Definition"/> is read.</summary>
    public CavePotentialMethod Method { get; set; }

    /// <summary>The model's definition as it was when the run was asked for (jsonb).</summary>
    public string Definition { get; set; } = "{}";

    /// <summary>The area the run was over. The map covers this area's outline and nothing outside it.</summary>
    public Guid AreaFeatureId { get; set; }

    /// <summary>The cell size asked for, in metres on the ground.</summary>
    public double CellMeters { get; set; }

    /// <summary>The account the run was asked by and computed for.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>Whether that account was a full administrator when the run was computed.</summary>
    public bool RanAsFullAdministrator { get; set; }

    /// <summary>Whether the model weighs any layer made from recorded positions.</summary>
    public bool UsesPositions { get; set; }

    /// <summary>
    /// Every recorded feature that went into the run's evidence, in id order. Empty until the run
    /// is computed, and empty for good when the model weighs the ground alone.
    /// </summary>
    public Guid[] EvidenceFeatureIds { get; set; } = [];

    /// <summary>
    /// The elevation build that was active when the run read the ground, or null when the model
    /// weighs no terrain layer. Which computed pictures were read, with their settings, is in
    /// <see cref="Summary"/>.
    /// </summary>
    public Guid? TerrainBuildId { get; set; }

    /// <summary>How many of those features were location-protected when the run read them.</summary>
    public int ProtectedFeatureCount { get; set; }

    /// <summary>
    /// What the run found, as JSON (jsonb): each layer's weight and what it was made from, how
    /// many cells were scored, and the least, greatest and mean favourability. Written by the
    /// server only.
    /// </summary>
    public string Summary { get; set; } = "{}";

    public CavePotentialResultStatus Status { get; set; }

    /// <summary>Why the run failed, as a stable code.</summary>
    public string? ErrorCode { get; set; }

    public string? Message { get; set; }

    public long? ProcessingJobId { get; set; }

    /// <summary>The absolute path of the favourability raster, once written.</summary>
    public string? Path { get; set; }

    public double? West { get; set; }

    public double? South { get; set; }

    public double? East { get; set; }

    public double? North { get; set; }

    public int? Columns { get; set; }

    public int? Rows { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>When the map was computed: the date a result is known by.</summary>
    public DateTimeOffset? ComputedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
