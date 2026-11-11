// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Storage;

namespace SilexGis.Domain.Tests;

public class StorageReportRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 8);

    [Fact]
    public void Growth_is_unknown_without_an_earlier_report_and_signed_with_one()
    {
        StorageGrowthRules.Since(1_000, null).ShouldBeNull();
        StorageGrowthRules.Since(1_000, 400).ShouldBe(600);
        StorageGrowthRules.Since(400, 1_000).ShouldBe(-600);
    }

    [Fact]
    public void The_pace_is_growth_over_the_span_and_unknown_for_no_span()
    {
        StorageGrowthRules.PerDay(700, 7).ShouldBe(100);
        StorageGrowthRules.PerDay(null, 7).ShouldBeNull();
        StorageGrowthRules.PerDay(700, 0).ShouldBeNull();
    }

    [Fact]
    public void The_full_day_is_the_room_over_the_pace_rounded_up()
    {
        StorageGrowthRules.FullOn(1_000, 100, Today).ShouldBe(Today.AddDays(10));
        StorageGrowthRules.FullOn(1_001, 100, Today).ShouldBe(Today.AddDays(11));
    }

    [Fact]
    public void Nothing_is_projected_without_a_bound_or_growth_and_a_store_past_its_cap_is_full_today()
    {
        StorageGrowthRules.FullOn(null, 100, Today).ShouldBeNull();
        StorageGrowthRules.FullOn(1_000, null, Today).ShouldBeNull();
        StorageGrowthRules.FullOn(1_000, 0, Today).ShouldBeNull();
        StorageGrowthRules.FullOn(1_000, -50, Today).ShouldBeNull();
        StorageGrowthRules.FullOn(0, 100, Today).ShouldBe(Today);
        StorageGrowthRules.FullOn(-10, 100, Today).ShouldBe(Today);

        // A century away is not a date anybody plans by.
        StorageGrowthRules.FullOn(long.MaxValue, 1, Today).ShouldBeNull();
    }

    [Fact]
    public void Only_the_named_groups_may_be_deleted_and_originals_are_not_one_of_them()
    {
        foreach (var group in StorageReclaimGroups.All)
        {
            StorageReclaimGroups.IsKnown(group).ShouldBeTrue();
        }

        StorageReclaimGroups.IsKnown("originals").ShouldBeFalse();
        StorageReclaimGroups.IsKnown("SUPERSEDED-SCAN-PRODUCTS").ShouldBeFalse();
        StorageReclaimGroups.IsKnown("").ShouldBeFalse();
        StorageReclaimGroups.IsKnown(null).ShouldBeFalse();
    }
}
