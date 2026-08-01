// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Baseline read audience of a protected object (stored in the RLS-ready visibility column).
/// Stored as smallint; values are part of the schema contract — do not renumber.
/// </summary>
public enum Visibility : short
{
    Private = 0,
    CavingGroup = 1,
    Authenticated = 2,
    Public = 3,
}
