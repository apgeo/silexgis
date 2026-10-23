// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Features;

/// <summary>
/// How a protected row is shown to somebody who may read it but not place it exactly.
/// </summary>
/// <remarks>
/// <para>
/// Decided by the row's kind first, and only for generic rows by their feature type. A cave and an
/// entrance are snapped to the protection grid, a centreline is withheld, and nothing a feature type
/// says can change that: caves carry a feature type of their own, and if that type's stored display
/// were consulted for them, one edited value in a taxonomy table would move every protected cave
/// from "approximately here" to "not there at all", or the other way. Every path that hands a
/// position out — the feature routes, share links, file exports — asks this, so they cannot come to
/// disagree about a cave.
/// </para>
/// <para>
/// A generic row whose type is unknown is withheld: failing closed is the only safe answer when the
/// policy cannot be read.
/// </para>
/// </remarks>
public static class FeatureDisplay
{
    /// <param name="kind">The row's kind.</param>
    /// <param name="typeDisplay">The protected display of the row's feature type, if it has one.</param>
    public static ProtectedDisplay PolicyOf(FeatureKind kind, ProtectedDisplay? typeDisplay) => kind switch
    {
        FeatureKind.Cave or FeatureKind.CaveEntrance => ProtectedDisplay.SnapPoint,
        FeatureKind.Generic => typeDisplay ?? ProtectedDisplay.Withhold,
        _ => ProtectedDisplay.Withhold,
    };
}
