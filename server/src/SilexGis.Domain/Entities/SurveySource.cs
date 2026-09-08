// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Which survey source a stored file is.
/// </summary>
/// <remarks>
/// Stored as a smallint and never renumbered: the value is part of the schema contract, so a new
/// member takes the next free number and nothing is reused. The kind says what the file is, not
/// which tool wrote it — two extensions of the same language are one kind, and the file's own name
/// is kept alongside for whoever needs the finer distinction.
/// </remarks>
public enum SurveySourceKind : short
{
    /// <summary>Therion survey or drawing source (<c>.th</c>, <c>.th2</c>).</summary>
    TherionSource = 0,

    /// <summary>A Therion project configuration (<c>.thconfig</c>).</summary>
    TherionConfig = 1,

    /// <summary>
    /// The log a Therion compilation wrote (<c>.log</c>). Archived like the rest, and the only kind
    /// read for what it says: it carries the loop-error table, which is how well the survey closes.
    /// </summary>
    TherionLog = 2,

    /// <summary>Survex survey source (<c>.svx</c>).</summary>
    SurvexSource = 3,

    /// <summary>An archive exported by a survey app, such as TopoDroid (<c>.zip</c>).</summary>
    TopoDroidArchive = 4,
}

/// <summary>
/// A raw survey source archived against a cave: the material a compiled survey was produced from.
///
/// <para>
/// A compiled export is derived data. It states what one version of one toolchain made of the
/// measurements on one day, and it cannot be re-made from itself — so an installation that keeps
/// only compiled models has lost the survey the day the toolchain moves on. This row is how the
/// source keeps existing.
/// </para>
///
/// <para>
/// The bytes are not stored here. They go through the document machinery like every other upload,
/// which is what gives them revisions: <see cref="DocumentId"/> names the document, and its current
/// revision names the file that is the source *now*. Uploading a corrected source is a new revision
/// of that document rather than a second row, so the history of a source is one thread and the row
/// pointing at it never has to be moved.
/// </para>
///
/// <para>
/// The row carries no rights of its own. It belongs to a cave and the cave answers for it, on the
/// same terms as that cave's compiled models: a survey source can name fixed coordinates, so it is
/// location data and is withheld entirely from a caller without exact-location access to the cave.
/// </para>
/// </summary>
public class SurveySource : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning cave's feature id (FK to the cave subtype row).</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>
    /// The archived document. Its current revision holds the file that is this source now; earlier
    /// revisions are what it was before.
    /// </summary>
    public Guid DocumentId { get; set; }

    public SurveySourceKind Kind { get; set; }

    /// <summary>What to call this source in a list — the uploaded file's name, unless renamed.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The name the file arrived under, kept because the kind is coarser than the format: a
    /// <c>.th</c> and a <c>.th2</c> are one kind and only the name says which is which.
    /// </summary>
    public required string OriginalFileName { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Archived sources surface in their cave's timeline (features audit as "Feature").
    public string RootEntityType => nameof(Feature);

    public string RootEntityId => CaveFeatureId.ToString();
}
