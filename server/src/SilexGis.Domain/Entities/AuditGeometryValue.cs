// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The full before and after shape of one geometry an edit changed, kept beside a history row
/// whose diff could only record that geometry as a summary.
/// </summary>
/// <remarks>
/// <para>
/// The history records a geometry longer than a few kilobytes of text as its type, point count
/// and fingerprint (<see cref="Geo.AuditGeometry"/>), which says that it changed but cannot put it
/// back. For an edit of a feature's own shape — a cave area or a surface area drawn with many
/// vertices — this row keeps both shapes as well-known binary, so a restore or the undo of an
/// upload can return the shape exactly. Centerlines are left out: they are rebuilt from their
/// survey files, and they are the large ones.
/// </para>
/// <para>
/// <b>These bytes are positions.</b> No history payload carries them; only the restore and the
/// undo read them, and both refuse a geometry to a caller who may not see the feature's exact
/// location.
/// </para>
/// </remarks>
public class AuditGeometryValue
{
    public long AuditEntryId { get; set; }

    public AuditEntry? AuditEntry { get; set; }

    /// <summary>The changed property, as the history row's diff names it ("Geom").</summary>
    public required string Property { get; set; }

    /// <summary>The shape before the edit, or null when there was none.</summary>
    public byte[]? OldWkb { get; set; }

    /// <summary>The shape after the edit, or null when it was cleared.</summary>
    public byte[]? NewWkb { get; set; }
}
