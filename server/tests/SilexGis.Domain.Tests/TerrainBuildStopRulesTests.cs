// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which builds may be called off and which may be run again, decided from the same three facts the
/// screen offers the two actions from.
/// </summary>
public class TerrainBuildStopRulesTests
{
    private static readonly DateTimeOffset Asked = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_queued_build_stops_at_once_and_a_running_one_is_asked_to()
    {
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Queued, null, null)
            .ShouldBe(TerrainCancelOutcome.StopNow);
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Running, null, null)
            .ShouldBe(TerrainCancelOutcome.AskToStop);
    }

    [Fact]
    public void Asking_twice_is_answered_as_done_rather_than_refused()
    {
        // Pressing the button again while the first press is still being acted on, or after it
        // has been, is not a mistake worth a refusal: what was asked for is true or on its way.
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Running, null, Asked)
            .ShouldBe(TerrainCancelOutcome.AlreadyAsked);
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Failed, TerrainBuildFailures.Cancelled, Asked)
            .ShouldBe(TerrainCancelOutcome.AlreadyAsked);
    }

    [Fact]
    public void A_build_that_stopped_some_other_way_cannot_be_called_off()
    {
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Succeeded, null, null)
            .ShouldBe(TerrainCancelOutcome.NotCancellable);
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Failed, TerrainBuildFailures.BakeFailed, null)
            .ShouldBe(TerrainCancelOutcome.NotCancellable);

        // A build that finished although somebody asked too late: its pyramid is published, and
        // calling it off now would be a claim about work that has already been done.
        TerrainBuildStopRules.Cancel(TerrainBuildStatus.Succeeded, null, Asked)
            .ShouldBe(TerrainCancelOutcome.NotCancellable);
    }

    [Fact]
    public void Called_off_is_a_reason_on_a_failed_build_and_nothing_else()
    {
        TerrainBuildStopRules.IsCancelled(TerrainBuildStatus.Failed, TerrainBuildFailures.Cancelled)
            .ShouldBeTrue();
        TerrainBuildStopRules.IsCancelled(TerrainBuildStatus.Failed, TerrainBuildFailures.BakeUnavailable)
            .ShouldBeFalse();

        // A build being cancelled is still running: the code alone on any other status means
        // nothing, so a stale one cannot make a running build read as stopped.
        TerrainBuildStopRules.IsCancelled(TerrainBuildStatus.Running, TerrainBuildFailures.Cancelled)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(TerrainBuildStatus.Failed, true)]
    [InlineData(TerrainBuildStatus.Succeeded, false)]
    [InlineData(TerrainBuildStatus.Queued, false)]
    [InlineData(TerrainBuildStatus.Running, false)]
    public void Only_a_stopped_build_may_be_run_again(TerrainBuildStatus status, bool allowed) =>
        TerrainBuildStopRules.MayRetry(status).ShouldBe(allowed);
}
