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
    /// <summary>CLR type name of the root entity (e.g. "Cave", "TripLog", "SurfaceFeature").</summary>
    string RootEntityType { get; }

    /// <summary>String form of the root entity key.</summary>
    string RootEntityId { get; }
}
