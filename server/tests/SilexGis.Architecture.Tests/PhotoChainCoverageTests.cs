// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// Whether a photograph's recorded position may be told depends on what it is filed under, and
/// the photo-position rule keeps one table of the attachment vocabulary saying what places a
/// photograph filed under each kind. A kind the table forgets is withheld (it falls back to every
/// guarded feature), but forgetting is still a decision nobody made: this sweep makes every kind,
/// including any appended later, be named there before the build passes.
/// </summary>
public class PhotoChainCoverageTests
{
    [Fact]
    public void Every_attachment_kind_says_what_places_a_photograph_filed_under_it()
    {
        var unnamed = Enum.GetValues<AttachedEntityType>()
            .Where(t => !PhotoPositionDisclosure.TryChainSourceOf(t, out _))
            .ToList();

        unnamed.ShouldBeEmpty(
            "these attachment kinds have no arm in the photo-position chain. Say in "
            + "PhotoPositionDisclosure.TryChainSourceOf what places a photograph filed under each: "
            + "the places it names, or every guarded feature when it names none");
    }

    [Fact]
    public void Kinds_that_name_no_place_are_placed_by_every_guarded_feature()
    {
        // The fixture half: the kinds a person can file a photograph under through the ordinary
        // attachment surface that carry no position of their own. Each was once disclosable.
        foreach (var kind in new[]
        {
            AttachedEntityType.CavingGroup, AttachedEntityType.Event, AttachedEntityType.Geofile,
            AttachedEntityType.GeoreferencedMap, AttachedEntityType.MapView,
        })
        {
            PhotoPositionDisclosure.ChainSourceOf(kind).ShouldBe(PhotoChainSource.EveryGuardedFeature, kind.ToString());
        }

        PhotoPositionDisclosure.ChainSourceOf(AttachedEntityType.StoredFile).ShouldBe(PhotoChainSource.ParentFile);
        PhotoPositionDisclosure.ChainSourceOf(AttachedEntityType.TripLog).ShouldBe(PhotoChainSource.Trip);
        PhotoPositionDisclosure.ChainSourceOf(AttachedEntityType.Expedition).ShouldBe(PhotoChainSource.Camp);
    }

    [Fact]
    public void A_kind_nobody_has_placed_fails_closed()
    {
        const AttachedEntityType appendedLater = (AttachedEntityType)short.MaxValue;
        PhotoPositionDisclosure.TryChainSourceOf(appendedLater, out _).ShouldBeFalse();
        PhotoPositionDisclosure.ChainSourceOf(appendedLater).ShouldBe(PhotoChainSource.EveryGuardedFeature);
    }
}
