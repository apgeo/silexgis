// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using Xunit;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Who is on a trip and who is waiting for a place on it.
/// </summary>
/// <remarks>
/// Every test here asserts who is in as well as who is out, in the same body. A ranking that
/// stopped admitting anybody at all would satisfy every "this person is waiting" assertion on its
/// own, and would read as a stricter limit rather than as the broken thing it is.
/// </remarks>
public class TripAttendanceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_trip_with_no_stated_limit_takes_everybody_who_said_yes()
    {
        var rows = new[]
        {
            Yes(1, Noon),
            Yes(2, Noon.AddMinutes(5)),
            Yes(3, Noon.AddMinutes(10)),
        };

        var places = TripAttendance.Rank(rows, null);

        places.Count.ShouldBe(3);
        places.Values.ShouldAllBe(x => x.Attending);
        places[1].Place.ShouldBe(1);
        places[3].Place.ShouldBe(3);
    }

    [Fact]
    public void The_answer_past_the_limit_is_kept_and_waits_while_the_ones_before_it_are_in()
    {
        var rows = Enumerable.Range(1, 9)
            .Select(i => Yes(i, Noon.AddMinutes(i)))
            .ToArray();

        var places = TripAttendance.Rank(rows, 8);

        // The ninth was written down, which is the whole point of a limit that refuses nothing:
        // it holds a place in the order and is waiting for a place on the trip.
        places.Count.ShouldBe(9);
        places[9].Place.ShouldBe(9);
        places[9].Attending.ShouldBeFalse();

        // And the eight before it are on the trip — without which "the ninth is waiting" would
        // also be true of a ranking that admitted nobody.
        places.Values.Count(x => x.Attending).ShouldBe(8);
        places[8].Attending.ShouldBeTrue();
    }

    [Fact]
    public void Somebody_who_said_maybe_and_later_yes_joins_the_queue_when_they_said_yes()
    {
        // The row was created first and answered last: the stamp that counts is when the answer
        // standing now was given, not when the row appeared.
        var lateConvert = Yes(1, Noon.AddHours(6));
        lateConvert.CreatedAt = Noon.AddDays(-30);

        var rows = new[]
        {
            lateConvert,
            Yes(2, Noon.AddMinutes(1)),
            Yes(3, Noon.AddMinutes(2)),
        };

        var places = TripAttendance.Rank(rows, 2);

        // Behind both of the people who said yes while they were still saying maybe...
        places[1].Place.ShouldBe(3);
        places[1].Attending.ShouldBeFalse();

        // ...and those two are on the trip, in the order they answered in.
        places[2].Place.ShouldBe(1);
        places[2].Attending.ShouldBeTrue();
        places[3].Place.ShouldBe(2);
        places[3].Attending.ShouldBeTrue();
    }

    [Fact]
    public void Being_picked_puts_somebody_on_the_trip_without_moving_anybody_in_the_order()
    {
        var rows = new[]
        {
            Yes(1, Noon.AddMinutes(1)),
            Yes(2, Noon.AddMinutes(2)),
            Yes(3, Noon.AddMinutes(3)),
        };
        rows[2].SelectedAt = Noon.AddDays(1);

        var places = TripAttendance.Rank(rows, 2);

        // The picked person is on the trip from the back of the queue...
        places[3].Attending.ShouldBeTrue();

        // ...and displaces the last person who would otherwise have got in, not the first.
        places[1].Attending.ShouldBeTrue();
        places[2].Attending.ShouldBeFalse();

        // The order underneath is untouched: everybody still holds the place they signed up for,
        // so a surface drawing it can show a hand-chosen team without hiding who was first.
        places[1].Place.ShouldBe(1);
        places[2].Place.ShouldBe(2);
        places[3].Place.ShouldBe(3);
    }

    [Fact]
    public void Only_a_yes_holds_a_place_and_the_others_are_neither_in_nor_waiting()
    {
        var rows = new[]
        {
            Row(1, TripInvitationResponse.No, Noon.AddMinutes(1)),
            Row(2, TripInvitationResponse.Maybe, Noon.AddMinutes(2)),
            Row(3, TripInvitationResponse.Pending, null),
            Yes(4, Noon.AddMinutes(4)),
        };

        var places = TripAttendance.Rank(rows, 4);

        // The one who said yes holds the first place, and holds it whoever answered before them:
        // an order counting declines would seat four people on a trip one person is coming to.
        places.Count.ShouldBe(1);
        places[4].Place.ShouldBe(1);
        places[4].Attending.ShouldBeTrue();
    }

    [Fact]
    public void Two_answers_in_the_same_instant_are_taken_in_a_fixed_order()
    {
        var same = Noon.AddMinutes(7);
        var rows = new[] { Yes(9, same), Yes(4, same) };

        // Read twice from two orderings of the same rows: without the key behind the stamp, the
        // same answers would seat two different people on two readings.
        var first = TripAttendance.Rank(rows, 1);
        var second = TripAttendance.Rank(rows.Reverse(), 1);

        first[4].Attending.ShouldBeTrue();
        first[9].Attending.ShouldBeFalse();
        second[4].Attending.ShouldBeTrue();
        second[9].Attending.ShouldBeFalse();
    }

    [Fact]
    public void A_yes_with_no_stamp_on_it_waits_behind_every_stamped_one()
    {
        var rows = new[] { Yes(1, null), Yes(2, Noon.AddYears(5)) };

        var places = TripAttendance.Rank(rows, 1);

        // A missing stamp is not "answered at the beginning of time": handing the last place to
        // whoever's record is least complete is the opposite of taking people in order.
        places[2].Attending.ShouldBeTrue();
        places[1].Attending.ShouldBeFalse();
    }

    /// <summary>
    /// The order a list is rendered in and the places that list reports come from one expression.
    /// Written twice they would compile and pass, and disagree the day either was changed alone —
    /// at which point a reader sees place 3 drawn above place 2 and concludes they were passed
    /// over out of turn.
    /// </summary>
    [Fact]
    public void The_order_the_answers_are_read_in_is_the_order_the_places_are_numbered_in()
    {
        var rows = new[]
        {
            Yes(3, Noon.AddMinutes(30)),
            Row(2, TripInvitationResponse.No, Noon.AddMinutes(10)),
            Yes(1, null),
            Yes(4, Noon.AddMinutes(5)),
        };

        var ordered = TripAttendance.InSignUpOrder(rows);
        var places = TripAttendance.Rank(rows, 1);

        // Everything is in the order, answers and refusals alike, because a list shows them all;
        // the numbering covers the yeses, and runs up the order without ever going back on it.
        ordered.Select(x => x.Id).ShouldBe([4L, 2L, 3L, 1L]);

        var numbered = ordered.Where(x => places.ContainsKey(x.Id)).Select(x => places[x.Id].Place);
        numbered.ShouldBe([1, 2, 3]);

        // And the first in that order is the one on the trip, so the numbering is not merely
        // self-consistent while everybody waits.
        places[4].Attending.ShouldBeTrue();
        places[3].Attending.ShouldBeFalse();
    }

    private static TripInvitation Yes(long id, DateTimeOffset? respondedAt) =>
        Row(id, TripInvitationResponse.Yes, respondedAt);

    private static TripInvitation Row(long id, TripInvitationResponse response, DateTimeOffset? respondedAt) =>
        new()
        {
            Id = id,
            TripLogId = Guid.Parse("2f7b0f2e-0000-4000-8000-000000000001"),
            CaverId = Guid.NewGuid(),
            Response = response,
            RespondedAt = respondedAt,
        };
}
