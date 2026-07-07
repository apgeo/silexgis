// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Marker: writes to this entity are recorded in audit_log.</summary>
public interface IAuditable
{
    /// <summary>String form of the entity key, for the polymorphic audit reference.</summary>
    string AuditId { get; }
}
