// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A protected row's display is decided by its kind first: caves and entrances are snapped and
/// centrelines withheld whatever any feature type says, and only a generic row reads its type.
/// </summary>
public sealed class FeatureDisplayTests
{
    [Theory]
    [InlineData(FeatureKind.Cave)]
    [InlineData(FeatureKind.CaveEntrance)]
    public void A_cave_or_an_entrance_is_snapped_whatever_its_type_says(FeatureKind kind)
    {
        FeatureDisplay.PolicyOf(kind, ProtectedDisplay.Withhold).ShouldBe(ProtectedDisplay.SnapPoint);
        FeatureDisplay.PolicyOf(kind, ProtectedDisplay.SnapPoint).ShouldBe(ProtectedDisplay.SnapPoint);
        FeatureDisplay.PolicyOf(kind, null).ShouldBe(ProtectedDisplay.SnapPoint);
    }

    [Fact]
    public void A_centreline_is_withheld_whatever_its_type_says()
    {
        FeatureDisplay.PolicyOf(FeatureKind.Centerline, ProtectedDisplay.SnapPoint).ShouldBe(ProtectedDisplay.Withhold);
        FeatureDisplay.PolicyOf(FeatureKind.Centerline, null).ShouldBe(ProtectedDisplay.Withhold);
    }

    [Fact]
    public void A_generic_row_follows_its_type_and_is_withheld_when_the_type_is_unknown()
    {
        FeatureDisplay.PolicyOf(FeatureKind.Generic, ProtectedDisplay.SnapPoint).ShouldBe(ProtectedDisplay.SnapPoint);
        FeatureDisplay.PolicyOf(FeatureKind.Generic, ProtectedDisplay.Withhold).ShouldBe(ProtectedDisplay.Withhold);
        FeatureDisplay.PolicyOf(FeatureKind.Generic, null).ShouldBe(ProtectedDisplay.Withhold);
    }
}
