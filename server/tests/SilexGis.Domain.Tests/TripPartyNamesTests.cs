// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which of a person's two names a surface with little room shows.
/// </summary>
public class TripPartyNamesTests
{
    [Fact]
    public void What_the_party_calls_somebody_is_shown_where_it_has_been_said()
    {
        TripPartyNames.Shown("Ion Popescu", "Nelu").ShouldBe("Nelu");
    }

    [Fact]
    public void Somebody_with_no_short_name_is_shown_under_the_roster_name()
    {
        // An improvement to a drawing and never a precondition for one: a roster nobody has
        // annotated publishes exactly as it did before the field existed.
        TripPartyNames.Shown("Ion Popescu", null).ShouldBe("Ion Popescu");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_short_name_of_nothing_is_no_short_name(string blank)
    {
        // It would draw a marker with nothing on it, which reads as somebody the page was told not
        // to name — a different statement from "nobody gave them a short name".
        TripPartyNames.Shown("Ion Popescu", blank).ShouldBe("Ion Popescu");
    }

    [Fact]
    public void A_short_name_is_shown_without_the_spaces_typed_round_it()
    {
        TripPartyNames.Shown("Ion Popescu", "  Nelu ").ShouldBe("Nelu");
    }
}
