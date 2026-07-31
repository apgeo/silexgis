// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// How a feature kind is displayed to callers without exact-location permission when the
/// feature is under a protection root. Security-bearing kind metadata: admin-only edits,
/// audited; seeded values are contract-tested. Stored as smallint.
/// </summary>
public enum ProtectedDisplay : short
{
    /// <summary>
    /// Point geometry is snapped to the obfuscation grid; any other geometry class is
    /// withheld regardless (the class floor — extended geometry cannot be safely snapped).
    /// </summary>
    SnapPoint = 0,

    /// <summary>Withheld entirely, whatever the geometry class (existence is not disclosed).</summary>
    Withhold = 1,
}
