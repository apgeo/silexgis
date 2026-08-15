// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The numbers behind a build's status and phase, pinned.
///
/// <para>
/// They are stored as smallints and they are read by things that are not this code: the partial
/// index that finds builds still owed work names the unfinished statuses as literal numbers in SQL,
/// and a stored row means whatever the enum meant on the day it was written. Renumbering either
/// vocabulary would leave every existing row describing a different state and that index quietly
/// selecting the wrong ones — a resume sweep that finds nothing, with no error anywhere.
/// </para>
/// </summary>
public class TerrainBuildVocabularyTests
{
    [Fact]
    public void Build_statuses_keep_the_numbers_rows_were_written_with()
    {
        ((short)TerrainBuildStatus.Queued).ShouldBe((short)0);
        ((short)TerrainBuildStatus.Running).ShouldBe((short)1);
        ((short)TerrainBuildStatus.Succeeded).ShouldBe((short)2);
        ((short)TerrainBuildStatus.Failed).ShouldBe((short)3);
    }

    [Fact]
    public void The_unfinished_statuses_are_the_two_the_resume_index_names()
    {
        // The index that finds builds a restart still owes work to is filtered on "status in
        // (0, 1)". These are those two, and this test is what keeps that literal honest.
        Enum.GetValues<TerrainBuildStatus>()
            .Where(s => s is TerrainBuildStatus.Queued or TerrainBuildStatus.Running)
            .Select(s => (short)s)
            .ShouldBe([0, 1]);
    }

    [Fact]
    public void Phases_are_numbered_in_the_order_the_work_happens()
    {
        // Each step consumes what the one before it produced, so the order is part of the meaning
        // and not merely how the members were typed out: a build resuming after a restart re-enters
        // at the earliest step whose output is missing.
        ((short)TerrainBuildPhase.Pending).ShouldBe((short)0);
        ((short)TerrainBuildPhase.Fetch).ShouldBe((short)1);
        ((short)TerrainBuildPhase.Prepare).ShouldBe((short)2);
        ((short)TerrainBuildPhase.Bake).ShouldBe((short)3);
        ((short)TerrainBuildPhase.Validate).ShouldBe((short)4);
        ((short)TerrainBuildPhase.Publish).ShouldBe((short)5);

        Enum.GetValues<TerrainBuildPhase>().Select(p => (short)p).ShouldBe([0, 1, 2, 3, 4, 5]);
    }

    [Fact]
    public void Source_kinds_keep_the_numbers_rows_were_written_with()
    {
        ((short)TerrainBuildSourceKind.Fetched).ShouldBe((short)0);
        ((short)TerrainBuildSourceKind.Uploaded).ShouldBe((short)1);
        ((short)TerrainBuildSourceKind.ServerDirectory).ShouldBe((short)2);
    }
}
