// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// An auditable entity whose changes should also surface in a <em>parent</em> entity's
/// history timeline. The audit trail records these root coordinates so a parent's history
/// query picks up changes to its children (entrances, attachments, taggings, trip links,
/// participants) without each child needing its own timeline. Implemented alongside
/// <see cref="IAuditable"/>.
/// </summary>
public interface IAuditChild
{
    /// <summary>
    /// CLR type name of the root entity (e.g. "Cave", "TripLog", "SurfaceFeature"), or null
    /// when this particular row belongs to no parent.
    /// </summary>
    /// <remarks>
    /// Nullable because a kind of row can be a child of something only sometimes: an access
    /// rule written by a camp's sharing belongs on that camp's trail, while the same kind of
    /// rule authored by hand belongs to nobody but itself. A row answering null is recorded
    /// with no root, exactly as a row whose type never implements this.
    /// </remarks>
    string? RootEntityType { get; }

    /// <summary>String form of the root entity key, or null alongside a null type.</summary>
    string? RootEntityId { get; }
}
