// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

public class TripDeletionRulesTests
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_default_window_is_the_one_a_deleted_document_gets()
    {
        TripDeletionRules.DefaultRetentionDays.ShouldBe(30);
        TimeSpan.FromDays(TripDeletionRules.DefaultRetentionDays).ShouldBe(SoftDeleteRules.DefaultRetention);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Zero_or_less_is_no_window_at_all(int days)
    {
        // Not a window of nothing: a deleted trip is then kept until somebody says otherwise, so
        // it stays restorable however long ago it went and no pass has a cut-off to measure by.
        var window = TripDeletionRules.Window(days);

        window.ShouldBeNull();
        TripDeletionRules.RemovedAt(DeletedAt, window).ShouldBeNull();
        TripDeletionRules.PurgeCutoff(DeletedAt.AddYears(10), window).ShouldBeNull();
        TripDeletionRules.IsRestorable(DeletedAt, DeletedAt.AddYears(10), window).ShouldBeTrue();
    }

    [Fact]
    public void A_window_says_when_the_trip_goes()
    {
        var window = TripDeletionRules.Window(30);

        window.ShouldBe(TimeSpan.FromDays(30));
        TripDeletionRules.RemovedAt(DeletedAt, window).ShouldBe(DeletedAt.AddDays(30));
    }

    [Fact]
    public void Restorable_and_removable_are_two_readings_of_one_line()
    {
        var window = TripDeletionRules.Window(30);
        var justInside = DeletedAt.AddDays(30).AddTicks(-1);
        var theMoment = DeletedAt.AddDays(30);

        // One tick before the moment a list said it would go: still restorable, and the pass's
        // cut-off has not reached it.
        TripDeletionRules.IsRestorable(DeletedAt, justInside, window).ShouldBeTrue();
        (DeletedAt <= TripDeletionRules.PurgeCutoff(justInside, window)).ShouldBeFalse();

        // At the moment itself both flip together. A trip the pass may remove is never one a
        // restore would still accept, and the other way round.
        TripDeletionRules.IsRestorable(DeletedAt, theMoment, window).ShouldBeFalse();
        (DeletedAt <= TripDeletionRules.PurgeCutoff(theMoment, window)).ShouldBeTrue();
    }

    [Fact]
    public void A_trip_deleted_before_the_window_was_shortened_goes_by_the_new_window()
    {
        // The stamp records when, not for how long: the window is read when the question is
        // asked, so shortening it shortens it for everything already deleted.
        var asked = DeletedAt.AddDays(10);

        TripDeletionRules.IsRestorable(DeletedAt, asked, TripDeletionRules.Window(30)).ShouldBeTrue();
        TripDeletionRules.IsRestorable(DeletedAt, asked, TripDeletionRules.Window(7)).ShouldBeFalse();
    }
}
