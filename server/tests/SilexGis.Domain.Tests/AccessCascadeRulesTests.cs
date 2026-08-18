// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The two rules a grant reaching from one thing to many must obey, driven without a database
/// or a route because both are rules rather than behaviours of any one endpoint.
/// </summary>
public sealed class AccessCascadeRulesTests
{
    [Fact]
    public void Exact_location_may_never_ride_along_and_every_other_action_may()
    {
        AccessCascadeRules.CarriesNeverCascaded(AccessAction.ViewExactLocation).ShouldBeTrue();

        // Asked for alongside things that are perfectly fine, which is how it would actually
        // arrive: a caller ticking every box on a form rather than asking for it on its own.
        AccessCascadeRules.CarriesNeverCascaded(
            AccessAction.Read | AccessAction.Write | AccessAction.ViewExactLocation).ShouldBeTrue();

        // The positive half, so this proves a rule rather than a refusal of everything.
        foreach (var action in AccessActions.All)
        {
            if (action != AccessAction.ViewExactLocation)
            {
                AccessCascadeRules.CarriesNeverCascaded(action).ShouldBeFalse($"{action} may cascade");
            }
        }

        AccessCascadeRules.CarriesNeverCascaded(AccessAction.None).ShouldBeFalse();
    }

    [Fact]
    public void A_refusal_can_say_how_many_refused_and_has_nothing_with_which_to_say_which()
    {
        var detail = AccessCascadeRules.IncompleteDetail(7);

        detail.ShouldContain("7");

        // The count is the only thing the sentence is built from, so there is no argument
        // through which an identifier could reach it even by accident. Two refusals of the
        // same size are the same sentence whatever was refused.
        detail.ShouldBe(AccessCascadeRules.IncompleteDetail(7));
        detail.ShouldNotBe(AccessCascadeRules.IncompleteDetail(6));
    }

    [Fact]
    public void A_refusal_of_nothing_is_not_a_refusal()
    {
        // Guards the caller, not the reader: a cascade that refused nothing must be applied,
        // and a "0 trips were refused" message would mean the writer had lost track of that.
        Should.Throw<ArgumentOutOfRangeException>(() => AccessCascadeRules.IncompleteDetail(0));
    }
}
