// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Features;

/// <summary>
/// Codes of shipped cave types that something other than the seeder depends on by name.
/// </summary>
/// <remarks>
/// The same rule the feature-type codes are held under, and for the same reason: a query that
/// resolves a kind by code and finds nothing answers with an empty result that looks exactly like a
/// correct answer, so a rename must break the build rather than quietly empty a view. Only codes a
/// behaviour is built on belong here; the palette itself lives with the seeder that writes it.
/// </remarks>
public static class CaveTypeSeeds
{
    /// <summary>
    /// A cave that a stream comes out of. Its entrance altitude is the height water leaves the
    /// massif at, which is why an elevation view draws these as reference lines rather than as
    /// ordinary entrances: a level cut at the height of a resurgence is a different statement about
    /// a cave from a level cut anywhere else.
    /// </summary>
    public const string SpringCave = "spring_cave";
}
