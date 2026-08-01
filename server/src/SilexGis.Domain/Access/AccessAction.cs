// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Action bit flags stored in access_entries.actions. The first six values are the
/// pre-existing per-object permission flags and are part of the schema contract —
/// do not renumber.
/// </summary>
[Flags]
public enum AccessAction
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4,
    Share = 8,

    /// <summary>May edit the direct access entries on an object (bounded by the
    /// no-amplification rule: only rights the caller effectively holds can be granted).</summary>
    ManagePermissions = 16,

    /// <summary>Consumed by location protection: exact coordinates of a protected
    /// feature require this on every protected root above it.</summary>
    ViewExactLocation = 32,

    /// <summary>Create objects in the domain; evaluated against the target context
    /// (prospective parent feature and/or requested caving-group binding).</summary>
    Create = 64,

    /// <summary>Run an operation: import, export, job, reprocess. Evaluated against the
    /// target row when one exists, otherwise domain-wide.</summary>
    Execute = 128,
}
