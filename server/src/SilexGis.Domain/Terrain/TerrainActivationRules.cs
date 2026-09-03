// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// Whether a build that has just finished may become the terrain the 3D scene draws without
/// anybody pressing anything.
/// </summary>
/// <remarks>
/// <para>
/// Somebody who has just built an area wants to look at it, and the step between finishing and
/// seeing it was a button they had to know to press. So a finished build draws itself.
/// </para>
/// <para>
/// The one thing it must never do is take the scene away from a build somebody chose on purpose.
/// That is why the choice records how it was made: an automatic mark may be moved on by the next
/// finished build, a hand-made one may not. Without that distinction the rule has only two
/// possible shapes, and both are wrong — "draw it whenever nothing is drawn" means the second
/// build an installation ever makes never draws itself, and "always draw the newest" quietly
/// overrules the operator who picked a coarser build for a reason.
/// </para>
/// </remarks>
public static class TerrainActivationRules
{
    /// <summary>
    /// Whether the just-finished build should take the scene, given what is drawn now.
    /// </summary>
    /// <param name="hasDrawablePyramid">
    /// Whether this build actually left a pyramid that can be drawn. A run can succeed without
    /// reaching the publishing step — the chain stops at the last step this installation
    /// implements — and terrain that is not there is drawn as smooth bare ground with nothing
    /// anywhere saying so, which is worse than not switching at all.
    /// </param>
    /// <param name="alreadyDrawn">Whether this build is already the one being drawn.</param>
    /// <param name="drawnWasAutomatic">
    /// How the currently drawn build got there, or null when nothing is drawn. Bare ground counts
    /// as nothing rather than as a choice: it is where an installation starts, and a build
    /// finishing is the first moment there is anything to draw at all.
    /// </param>
    public static bool MayDrawAutomatically(
        bool hasDrawablePyramid, bool alreadyDrawn, bool? drawnWasAutomatic)
    {
        if (!hasDrawablePyramid || alreadyDrawn)
        {
            return false;
        }

        return drawnWasAutomatic is null or true;
    }
}
