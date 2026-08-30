// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Who reads a trip nobody said anything about. The rule is asked twice — once by the write that
/// applies it and once by the form that names the audience before the trip exists — so it lives in
/// one place and both callers get the same sentence.
/// </summary>
public class TripAudienceRulesTests
{
    private static readonly Guid OneGroup = Guid.NewGuid();
    private static readonly Guid AnotherGroup = Guid.NewGuid();

    [Fact]
    public void A_trip_being_planned_starts_with_its_author_s_caving_group()
    {
        var (visibility, groupId) = TripAudienceRules.DefaultAudience(
            TripCreationIntent.Plan, [OneGroup]);

        visibility.ShouldBe(Visibility.CavingGroup);

        // The group travels with the audience rather than being left for the caller to fill in:
        // a group-visible row naming no group admits nobody, so the two are one answer.
        groupId.ShouldBe(OneGroup);
    }

    [Fact]
    public void A_trip_written_up_afterwards_starts_private_whatever_its_author_belongs_to()
    {
        // Both halves in one test: the same author, the same memberships, and only the reason the
        // trip is being created changed. Nothing but that decides the difference.
        TripAudienceRules.DefaultAudience(TripCreationIntent.Report, [OneGroup])
            .ShouldBe((Visibility.Private, null));
        TripAudienceRules.DefaultAudience(TripCreationIntent.Plan, [OneGroup])
            .ShouldBe((Visibility.CavingGroup, OneGroup));
    }

    [Fact]
    public void An_author_with_no_group_plans_privately_rather_than_to_everybody()
    {
        // The fallback is the narrow answer on purpose. A plan names the cave it is for, so
        // handing it to every account because its author happens to belong to no group would be
        // a widening nobody asked for; a plan only its author can read is merely useless, and is
        // fixed by naming an audience.
        TripAudienceRules.DefaultAudience(TripCreationIntent.Plan, [])
            .ShouldBe((Visibility.Private, null));

        // The positive half, with the one thing that differs changed and nothing else.
        TripAudienceRules.DefaultAudience(TripCreationIntent.Plan, [OneGroup])
            .ShouldBe((Visibility.CavingGroup, OneGroup));
    }

    [Fact]
    public void Belonging_to_several_groups_is_answered_the_same_way_as_belonging_to_none()
    {
        // Nothing here can say which group the trip is for — the memberships carry no order and
        // none of them outranks another — and guessing would show the plan to a club that has
        // nothing to do with it.
        TripAudienceRules.DefaultAudience(TripCreationIntent.Plan, [OneGroup, AnotherGroup])
            .ShouldBe((Visibility.Private, null));
    }
}
