// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Whether a build that has just finished takes the scene.
/// </summary>
/// <remarks>
/// The interesting cases are the two failure shapes this rule exists to avoid, and each is asserted
/// beside the case it has to stay distinguishable from. A rule that only ever draws over bare
/// ground and a rule that always draws the newest build both pass a test that checks the happy
/// path alone: the first never fires again after an installation's first build, and the second
/// silently overrules the operator who picked a coarser build on purpose.
/// </remarks>
public class TerrainActivationRulesTests
{
    [Fact]
    public void A_finished_build_draws_itself_when_nothing_is_drawn()
    {
        TerrainActivationRules.MayDrawAutomatically(
            hasDrawablePyramid: true, alreadyDrawn: false, drawnWasAutomatic: null)
            .ShouldBeTrue();
    }

    [Fact]
    public void It_takes_over_from_a_build_that_drew_itself_but_not_from_one_somebody_chose()
    {
        // The pair is the whole rule. Losing the first line leaves an installation whose second
        // build never draws itself; losing the second overrules a deliberate choice.
        TerrainActivationRules.MayDrawAutomatically(
            hasDrawablePyramid: true, alreadyDrawn: false, drawnWasAutomatic: true)
            .ShouldBeTrue();

        TerrainActivationRules.MayDrawAutomatically(
            hasDrawablePyramid: true, alreadyDrawn: false, drawnWasAutomatic: false)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_run_that_succeeded_without_publishing_a_pyramid_is_not_drawn()
    {
        // Terrain that is not there draws as smooth bare ground with nothing anywhere saying so,
        // which is worse than leaving the scene where it was.
        TerrainActivationRules.MayDrawAutomatically(
            hasDrawablePyramid: false, alreadyDrawn: false, drawnWasAutomatic: null)
            .ShouldBeFalse();

        TerrainActivationRules.MayDrawAutomatically(
            hasDrawablePyramid: false, alreadyDrawn: false, drawnWasAutomatic: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_build_already_being_drawn_is_left_alone()
    {
        // The re-delivered-job case: nothing to do, and moving the mark off a row and back on to
        // it would write an audit entry saying the scene changed when it did not.
        TerrainActivationRules.MayDrawAutomatically(
            hasDrawablePyramid: true, alreadyDrawn: true, drawnWasAutomatic: false)
            .ShouldBeFalse();
    }
}
