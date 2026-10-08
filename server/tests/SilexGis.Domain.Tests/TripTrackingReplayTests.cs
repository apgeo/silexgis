// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a send is answered with when the key it carries may already be on the trip's log.
/// </summary>
/// <remarks>
/// The rule has two answers that must not be confused — "this act was never written, write it"
/// and "this act was written and nothing of it is left on the log" — and they differ only in
/// whether the answer is absent or empty. Each test below asserts one beside the other.
/// </remarks>
public class TripTrackingReplayTests
{
    private static readonly DateTimeOffset Removed = new(2026, 9, 12, 14, 0, 0, TimeSpan.Zero);

    private static TripPositionEvent Report(Guid caver, bool removed = false) =>
        new() { CaverId = caver, RemovedAt = removed ? Removed : null };

    [Fact]
    public void An_act_the_log_has_never_heard_of_is_to_be_written()
    {
        TripTrackingRules.ReplayAnswer([Guid.NewGuid()], []).ShouldBeNull();
    }

    [Fact]
    public void An_act_already_written_is_answered_with_its_reports_in_the_order_the_send_names_its_people()
    {
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // Stored in an order of their own, as a read of the table hands them over.
        var stored = new[] { Report(third), Report(first), Report(second) };

        var answer = TripTrackingRules.ReplayAnswer([first, second, third], stored);

        answer.ShouldNotBeNull();
        answer.Select(e => e.CaverId).ShouldBe([first, second, third]);
    }

    [Fact]
    public void A_report_taken_off_the_log_is_neither_answered_nor_a_reason_to_write_again()
    {
        var (kept, taken) = (Guid.NewGuid(), Guid.NewGuid());
        var stored = new[] { Report(kept), Report(taken, removed: true) };

        var answer = TripTrackingRules.ReplayAnswer([kept, taken], stored);

        // Not null: the act is on record, so nothing is written — the removed one included.
        answer.ShouldNotBeNull();
        answer.ShouldHaveSingleItem().CaverId.ShouldBe(kept);
    }

    [Fact]
    public void An_act_removed_entirely_is_answered_with_nothing_and_still_not_written_again()
    {
        var caver = Guid.NewGuid();

        var answer = TripTrackingRules.ReplayAnswer([caver], [Report(caver, removed: true)]);

        // Empty and not null — the difference between "received, since removed" and "never
        // received". Beside it, the same send with nothing stored is the one that is written.
        answer.ShouldNotBeNull();
        answer.ShouldBeEmpty();
        TripTrackingRules.ReplayAnswer([caver], []).ShouldBeNull();
    }

    [Fact]
    public void What_the_repeat_says_is_not_compared_with_what_was_written()
    {
        var (written, asked) = (Guid.NewGuid(), Guid.NewGuid());

        // The key names the act. A repeat naming somebody else is answered with what the act
        // wrote, and nothing is written for the person it names now.
        var answer = TripTrackingRules.ReplayAnswer([asked], [Report(written)]);

        answer.ShouldNotBeNull();
        answer.ShouldHaveSingleItem().CaverId.ShouldBe(written);
    }

    [Fact]
    public void Somebody_the_repeat_does_not_name_comes_after_those_it_does()
    {
        var (named, unnamedEarlier, unnamedLater) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // Report ids are ordered by the time they were made, which is what the tail is sorted
        // by; stated here rather than left to two ids minted within one millisecond.
        var earlier = Report(unnamedEarlier);
        earlier.Id = new Guid("01920000-0000-7000-8000-000000000001");
        var later = Report(unnamedLater);
        later.Id = new Guid("01920000-0000-7000-8000-000000000002");
        var stored = new[] { later, Report(named), earlier };

        var answer = TripTrackingRules.ReplayAnswer([named], stored);

        answer.ShouldNotBeNull();
        answer.Select(e => e.CaverId).ShouldBe([named, unnamedEarlier, unnamedLater]);
    }

    /// <summary>
    /// Every send of one act has to write its people in one order, or two of them can each write
    /// one person and wait for the other. The order a save writes in is the order of the ids, so
    /// the ids are what is asserted: sorted by id, the reports are sorted by person — every time,
    /// since ids left to chance agree with any given order about once in the number of ways the
    /// people can be arranged, and a rule that held most of the time is the fault itself.
    /// </summary>
    [Fact]
    public void The_reports_of_one_act_are_given_ids_that_rise_as_its_people_do_however_the_send_names_them()
    {
        var people = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var inOrder = people.Order().ToList();

        for (var attempt = 0; attempt < 200; attempt++)
        {
            // Named forwards by one send and backwards by the next, as two phones might.
            var named = attempt % 2 == 0 ? people : [.. Enumerable.Reverse(people)];

            var ids = TripTrackingRules.ReportIdsInPersonOrder(named);

            ids.Keys.ShouldBe(people, ignoreOrder: true);
            ids.Values.Distinct().Count().ShouldBe(people.Count);
            ids.OrderBy(pair => pair.Value).Select(pair => pair.Key).ShouldBe(inOrder);
        }
    }

    [Fact]
    public void Somebody_named_twice_by_one_send_is_given_one_id()
    {
        var (once, twice) = (Guid.NewGuid(), Guid.NewGuid());

        var ids = TripTrackingRules.ReportIdsInPersonOrder([twice, once, twice]);

        ids.Keys.ShouldBe([once, twice], ignoreOrder: true);
    }

    [Fact]
    public void A_note_about_the_cave_is_the_answer_to_a_repeat_of_its_own_send()
    {
        // Its act names nobody and wrote one row with no person. The repeat names nobody either,
        // and is answered with that row — or with nothing, once somebody has taken it off.
        var note = new TripPositionEvent { Kind = TripPositionEventKind.CaveNote, Note = "loose rock" };

        var answer = TripTrackingRules.ReplayAnswer([], [note]);

        answer.ShouldNotBeNull();
        answer.ShouldHaveSingleItem().ShouldBeSameAs(note);

        note.RemovedAt = DateTimeOffset.UnixEpoch;
        TripTrackingRules.ReplayAnswer([], [note]).ShouldBeEmpty();
    }
}
