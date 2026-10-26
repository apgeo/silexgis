// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

public sealed class UploadReviewDecisionsTests
{
    private const ImportReviewDecision Pending = ImportReviewDecision.Pending;

    [Fact]
    public void Accepting_a_line_writes_what_it_depends_on_first_however_far_up()
    {
        // A scan (3) on a recording (2) at a place (1); and an unrelated line (4).
        HeldLine[] lines = [new(1, [], Pending), new(2, [1], Pending), new(3, [2], Pending), new(4, [], Pending)];

        UploadReviewDecisions.AcceptOrder(lines, [3]).ShouldBe([1, 2, 3]);
        UploadReviewDecisions.AcceptOrder(lines, [3, 1, 4]).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void A_dependency_kept_later_than_its_dependent_is_still_written_first()
    {
        // The phone re-sent the cave after the place, so the place (1) now waits on line 5.
        HeldLine[] lines = [new(1, [5], Pending), new(5, [], Pending)];

        UploadReviewDecisions.AcceptOrder(lines, [1]).ShouldBe([5, 1]);
    }

    [Fact]
    public void Only_waiting_lines_are_pulled_in()
    {
        HeldLine[] lines =
        [
            new(1, [], ImportReviewDecision.Accepted),
            new(2, [1], Pending),
            new(3, [], ImportReviewDecision.Rejected),
            new(4, [3], Pending),
        ];

        UploadReviewDecisions.AcceptOrder(lines, [2, 4]).ShouldBe([2, 4]);
        UploadReviewDecisions.AcceptOrder(lines, [1, 3]).ShouldBeEmpty();
    }

    [Fact]
    public void Rejecting_a_line_rejects_every_waiting_line_below_it_and_nothing_above()
    {
        HeldLine[] lines =
        [
            new(1, [], Pending), new(2, [1], Pending), new(3, [2], Pending),
            new(4, [1], ImportReviewDecision.Accepted), new(5, [], Pending),
        ];

        UploadReviewDecisions.RejectReach(lines, [2]).ShouldBe([2, 3]);
        UploadReviewDecisions.RejectReach(lines, [1]).ShouldBe([1, 2, 3]);
        UploadReviewDecisions.RejectReach(lines, [4]).ShouldBeEmpty();
    }

    [Fact]
    public void A_cycle_in_the_dependencies_ends_rather_than_loops()
    {
        HeldLine[] lines = [new(1, [2], Pending), new(2, [1], Pending)];

        UploadReviewDecisions.AcceptOrder(lines, [1]).Order().ShouldBe([1, 2]);
        UploadReviewDecisions.RejectReach(lines, [1]).ShouldBe([1, 2]);
    }

    [Fact]
    public void A_line_on_two_held_rows_is_pulled_in_with_both_and_rejected_with_either()
    {
        // A link (3) of a held photo (2) to a held cave (1).
        HeldLine[] lines = [new(1, [], Pending), new(2, [], Pending), new(3, [2, 1], Pending)];

        UploadReviewDecisions.AcceptOrder(lines, [3]).ShouldBe([2, 1, 3]);
        UploadReviewDecisions.RejectReach(lines, [1]).ShouldBe([1, 3]);
        UploadReviewDecisions.RejectReach(lines, [2]).ShouldBe([2, 3]);
    }

    [Fact]
    public void A_rejected_line_is_accepted_again_only_when_it_may_be_reopened_and_brings_its_rejected_dependency()
    {
        // A cave (1) rejected with the place (2) under it, and an older rejection (3) that can no
        // longer be reopened.
        HeldLine[] lines =
        [
            new(1, [], ImportReviewDecision.Rejected),
            new(2, [1], ImportReviewDecision.Rejected),
            new(3, [], ImportReviewDecision.Rejected),
            new(4, [], ImportReviewDecision.Failed),
        ];

        UploadReviewDecisions.AcceptOrder(lines, [2]).ShouldBeEmpty();
        UploadReviewDecisions.AcceptOrder(lines, [2], new HashSet<long> { 1, 2 }).ShouldBe([1, 2]);
        UploadReviewDecisions.AcceptOrder(lines, [3, 4], new HashSet<long> { 1, 2, 4 }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(new[] { ImportReviewDecision.Pending, ImportReviewDecision.Accepted }, ImportReviewState.Pending)]
    [InlineData(new[] { ImportReviewDecision.Accepted, ImportReviewDecision.Superseded }, ImportReviewState.Accepted)]
    [InlineData(new[] { ImportReviewDecision.Accepted, ImportReviewDecision.Failed }, ImportReviewState.PartlyAccepted)]
    [InlineData(new[] { ImportReviewDecision.Accepted, ImportReviewDecision.Rejected }, ImportReviewState.PartlyAccepted)]
    [InlineData(new[] { ImportReviewDecision.Rejected, ImportReviewDecision.Failed }, ImportReviewState.Rejected)]
    [InlineData(new[] { ImportReviewDecision.Superseded, ImportReviewDecision.Superseded }, ImportReviewState.Rejected)]
    public void A_batch_stands_where_its_lines_put_it(ImportReviewDecision[] decisions, ImportReviewState expected) =>
        UploadReviewDecisions.StateOf(decisions.Select(d => (ImportReviewDecision?)d)).ShouldBe(expected);
}
