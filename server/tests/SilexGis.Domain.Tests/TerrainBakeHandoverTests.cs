// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The agreement between the application and the program that makes tiles: what is asked for, what
/// comes back, and the one line in a tool's log that means it quietly did less than it was told to.
/// </summary>
/// <remarks>
/// Every one of these is a place where being wrong is invisible. A depth left out is a bake several
/// levels deeper than anyone wanted; a line ending the other side cannot read is a depth that is not
/// a number; and a memory warning gone unread is a complete, valid pyramid of ground at the wrong
/// resolution, published under a green tick.
/// </remarks>
public class TerrainBakeHandoverTests
{
    [Fact]
    public void A_request_states_the_depth_and_says_where_from_and_where_to()
    {
        var text = new TerrainBakeRequest(
            "/data/terrain/builds/abc/prepared",
            "/data/terrain/builds/abc/tiles",
            13,
            TerrainHeightDatum.Orthometric).ToText();

        text.ShouldContain("input=/data/terrain/builds/abc/prepared");
        text.ShouldContain("output=/data/terrain/builds/abc/tiles");

        // Never absent, whatever else changes. Left to work it out for itself the tile maker takes
        // its depth from the finest raster it was given, which multiplies the tile count several
        // times over for a small patch of fine survey inside a coarse fill.
        text.ShouldContain("maxDepth=13");
        text.ShouldContain("datum=orthometric");
    }

    [Fact]
    public void A_request_ends_its_lines_the_way_the_other_side_reads_them()
    {
        var text = new TerrainBakeRequest("/in", "/out", 9, TerrainHeightDatum.Ellipsoidal).ToText();

        // A carriage return would survive into the value, and a depth of "9\r" is not a number to
        // the shell that reads this — which fails as a refusal naming a field nobody touched.
        text.ShouldNotContain("\r");
        text.ShouldEndWith("\n");
        text.ShouldContain("datum=ellipsoidal");
    }

    [Fact]
    public void An_answer_says_which_of_the_things_that_can_happen_happened()
    {
        TerrainBakeResult.Parse("status=succeeded\nexit=0\nfinished=2026-08-16T09:00:00Z\n")
            .ShouldBe(new TerrainBakeResult(TerrainBakeOutcome.Succeeded, 0, null));

        TerrainBakeResult.Parse("status=failed\nexit=1\nreason=it exited 1\n")
            .ShouldBe(new TerrainBakeResult(TerrainBakeOutcome.Failed, 1, "it exited 1"));

        TerrainBakeResult.Parse("status=refused\nexit=\nreason=maxDepth is not a number\n")
            .ShouldBe(new TerrainBakeResult(
                TerrainBakeOutcome.Refused, null, "maxDepth is not a number"));

        TerrainBakeResult.Parse("status=interrupted\nexit=\nreason=the worker stopped\n").Outcome
            .ShouldBe(TerrainBakeOutcome.Interrupted);
    }

    /// <summary>
    /// An answer nobody can read is an answer, not an exception: it is written by another program
    /// on the far side of a directory, and "it said something I do not understand" has to be
    /// reportable in the same shape as everything else it can say.
    /// </summary>
    [Fact]
    public void An_answer_that_makes_no_sense_is_read_as_one_that_makes_no_sense()
    {
        TerrainBakeResult.Parse("").Outcome.ShouldBe(TerrainBakeOutcome.Unreadable);
        TerrainBakeResult.Parse("status=elsewhere\n").Outcome.ShouldBe(TerrainBakeOutcome.Unreadable);
        TerrainBakeResult.Parse("half a line").Outcome.ShouldBe(TerrainBakeOutcome.Unreadable);

        // Written by a host that ends its lines the other way, which is an ordinary thing for a
        // directory two operating systems can both write into.
        TerrainBakeResult.Parse("status=succeeded\r\nexit=0\r\n").Outcome
            .ShouldBe(TerrainBakeOutcome.Succeeded);
    }

    /// <summary>
    /// The line this whole step exists to catch. Short of memory the tile maker does not fail and
    /// does not stop: it stops refining and writes coarser tiles, and says so once, in its own log,
    /// in the middle of a run that then ends cleanly.
    /// </summary>
    [Fact]
    public void A_tool_that_gave_up_on_detail_is_recognised_however_it_phrases_it()
    {
        TerrainBakeHandover.MeansDegraded("2026-08-16 09:12:44 CRITICAL memory pressure detected")
            .ShouldBeTrue();
        TerrainBakeHandover.MeansDegraded("STOPPING mesh refinement due to CRITICAL memory")
            .ShouldBeTrue();

        // Ordinary work, and an ordinary notice about memory that is not the tool deciding to ship
        // less than it was asked for. Treating either as degradation would fail every large bake.
        TerrainBakeHandover.MeansDegraded("Generating tiles for level 13").ShouldBeFalse();
        TerrainBakeHandover.MeansDegraded("INFO memory usage 41%").ShouldBeFalse();
        TerrainBakeHandover.MeansDegraded("").ShouldBeFalse();
    }
}
