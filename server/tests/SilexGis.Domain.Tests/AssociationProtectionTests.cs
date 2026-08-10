// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The association rule in its pure form. Every case pairs the withholding it asserts with
/// the disclosure that must survive it: a rule that hid everything would pass any test that
/// only checked what is hidden.
/// </summary>
public class AssociationProtectionTests
{
    private static readonly Guid Cave = Guid.NewGuid();

    [Fact]
    public void A_caller_who_may_place_the_feature_exactly_is_told_what_points_at_it()
    {
        var association = new FeatureAssociation(Cave, DocumentCarriesItsOwnPosition: false);

        // Exact view is the whole question: someone who already has the coordinates learns
        // nothing from being told which report is about them.
        AssociationProtection.IsWithheld(association, exactViewOfTarget: true, revealProtectedAssociations: false)
            .ShouldBeFalse();

        // …and the same caller, without it, is not.
        AssociationProtection.IsWithheld(association, exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeTrue();
    }

    [Fact]
    public void An_association_that_names_no_feature_is_never_withheld()
    {
        // A document hung on a trip, a club or a map view names no feature, so this rule
        // guards no position for it and there is no setting to consult — whatever position
        // the named thing may carry of its own is that world's business, not a pairing here.
        var unpositioned = new FeatureAssociation(null, DocumentCarriesItsOwnPosition: true);
        AssociationProtection.IsWithheld(unpositioned, exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeFalse();

        // The same document hung on a feature instead is a different matter entirely.
        AssociationProtection.IsWithheld(
                unpositioned with { TargetFeatureId = Cave },
                exactViewOfTarget: false,
                revealProtectedAssociations: false)
            .ShouldBeTrue();
    }

    [Fact]
    public void The_setting_reveals_a_named_feature_to_everyone_who_can_read_the_document()
    {
        var association = new FeatureAssociation(Cave, DocumentCarriesItsOwnPosition: false);

        AssociationProtection.IsWithheld(association, exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeTrue();
        AssociationProtection.IsWithheld(association, exactViewOfTarget: false, revealProtectedAssociations: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_document_carrying_its_own_position_stays_unpaired_in_both_settings()
    {
        var geotagged = new FeatureAssociation(Cave, DocumentCarriesItsOwnPosition: true);
        var plain = geotagged with { DocumentCarriesItsOwnPosition = false };

        // Switching the setting on reveals the plain association and not this one: a photo
        // stamped with where it was taken, placed beside a cave's name, is the cave's
        // position to within a walk, and the setting reveals names rather than positions.
        AssociationProtection.IsWithheld(geotagged, exactViewOfTarget: false, revealProtectedAssociations: true)
            .ShouldBeTrue();
        AssociationProtection.IsWithheld(plain, exactViewOfTarget: false, revealProtectedAssociations: true)
            .ShouldBeFalse();

        // And the carve-out costs nothing to a caller who may place the cave anyway — they
        // could pair the two themselves.
        AssociationProtection.IsWithheld(geotagged, exactViewOfTarget: true, revealProtectedAssociations: false)
            .ShouldBeFalse();
    }
}
