// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How a party is numbered and ordered: by the number each person was given, with somebody no
/// writer numbered listed after the rest rather than dropped.
/// </summary>
public class TripPartyNumberingTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bogdan = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Carmen = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Dan = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    [Fact]
    public void The_next_number_is_one_past_the_highest_ever_given_and_never_a_gap()
    {
        TripPartyNumbering.Next([]).ShouldBe(1);
        TripPartyNumbering.Next([1, 2, 3]).ShouldBe(4);
        // Somebody numbered 2 left: the gap stays theirs.
        TripPartyNumbering.Next([1, 3]).ShouldBe(4);
        TripPartyNumbering.Next([5]).ShouldBe(6);
    }

    [Fact]
    public void The_party_is_listed_by_stored_number_whatever_order_the_roster_rows_are_in()
    {
        // Ana's row is the newest on the trip — her job changed — and she is still number 1.
        var order = TripPartyNumbering.Order(
            [(Ana, 90), (Bogdan, 11), (Carmen, 12)],
            new Dictionary<Guid, int> { [Ana] = 1, [Bogdan] = 2, [Carmen] = 3 },
            highestGiven: 3);

        order.ShouldBe([new(Ana, 1), new(Bogdan, 2), new(Carmen, 3)]);
    }

    [Fact]
    public void Somebody_who_left_leaves_a_gap_and_nobody_moves_up()
    {
        var order = TripPartyNumbering.Order(
            [(Ana, 10), (Carmen, 12)],
            new Dictionary<Guid, int> { [Ana] = 1, [Bogdan] = 2, [Carmen] = 3 },
            highestGiven: 3);

        order.ShouldBe([new(Ana, 1), new(Carmen, 3)]);
    }

    [Fact]
    public void Somebody_no_writer_numbered_comes_last_in_roster_order_numbered_past_the_highest_given()
    {
        // Number 4 was given once and is held by nobody now; the unnumbered start after it, so a
        // number a page has already shown is not handed to somebody else even provisionally.
        var order = TripPartyNumbering.Order(
            [(Dan, 40), (Carmen, 30), (Ana, 10), (Bogdan, 20)],
            new Dictionary<Guid, int> { [Ana] = 2 },
            highestGiven: 4);

        order.ShouldBe([new(Ana, 2), new(Bogdan, 5), new(Carmen, 6), new(Dan, 7)]);
    }

    [Fact]
    public void A_trip_nobody_numbered_is_listed_as_it_always_was_by_the_order_its_rows_were_written()
    {
        var order = TripPartyNumbering.Order(
            [(Carmen, 3), (Ana, 1), (Bogdan, 2)], new Dictionary<Guid, int>(), highestGiven: 0);

        order.ShouldBe([new(Ana, 1), new(Bogdan, 2), new(Carmen, 3)]);
    }

    [Fact]
    public void A_trip_with_nobody_on_it_has_an_empty_party()
    {
        TripPartyNumbering.Order([], new Dictionary<Guid, int> { [Ana] = 1 }, highestGiven: 1)
            .ShouldBeEmpty();
    }
}
