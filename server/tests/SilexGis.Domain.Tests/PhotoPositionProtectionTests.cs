// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The rule deciding whether a caller may be handed a photo's own recorded position. Every
/// test here pairs the withheld case with the disclosed one over the same chain, because a
/// rule that said no to everything would pass any test that only checked for a refusal.
/// </summary>
public class PhotoPositionProtectionTests
{
    private static readonly Guid OpenCave = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GuardedCave = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Neighbour = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_photo_hanging_on_nothing_is_disclosable_to_anyone()
    {
        // Nothing in the archive says the photo is of a guarded place, so there is no
        // protection for it to inherit — and no caller it could be inherited against.
        PhotoPositionProtection.IsDisclosable([], new HashSet<Guid>()).ShouldBeTrue();
        PhotoPositionProtection.IsDisclosable([], new HashSet<Guid> { OpenCave }).ShouldBeTrue();
    }

    [Fact]
    public void One_guarded_link_in_the_chain_withholds_the_position_while_an_open_one_does_not()
    {
        List<Guid> chain = [OpenCave];

        // The unreadable state, built explicitly: a caller who may place nothing.
        PhotoPositionProtection.IsDisclosable(chain, new HashSet<Guid>()).ShouldBeFalse();

        // ...and the same chain for a caller who may place it.
        PhotoPositionProtection.IsDisclosable(chain, new HashSet<Guid> { OpenCave }).ShouldBeTrue();
    }

    [Fact]
    public void Inheriting_from_two_places_means_obeying_both()
    {
        List<Guid> chain = [OpenCave, GuardedCave];

        // Every reason to allow, one reason to refuse: a photo attached to an open cave and
        // a guarded one is still a photo of the guarded one.
        PhotoPositionProtection
            .IsDisclosable(chain, new HashSet<Guid> { OpenCave })
            .ShouldBeFalse();

        PhotoPositionProtection
            .IsDisclosable(chain, new HashSet<Guid> { OpenCave, GuardedCave })
            .ShouldBeTrue();
    }

    [Fact]
    public void A_feature_reached_only_through_a_placing_link_still_counts()
    {
        // The chain arrives already widened — a neighbour reachable through a link whose
        // kind places its endpoints is in it exactly like a directly attached feature, and
        // the rule must not treat it as somehow weaker.
        List<Guid> chain = [OpenCave, Neighbour];

        PhotoPositionProtection
            .IsDisclosable(chain, new HashSet<Guid> { OpenCave })
            .ShouldBeFalse();

        PhotoPositionProtection
            .IsDisclosable(chain, new HashSet<Guid> { OpenCave, Neighbour })
            .ShouldBeTrue();
    }

    [Fact]
    public void Repeats_in_the_chain_change_nothing()
    {
        // Two attachments to the same cave arrive as two entries; the answer is about the
        // set of places, not how many ways the photo reaches each one.
        List<Guid> chain = [GuardedCave, GuardedCave, GuardedCave];

        PhotoPositionProtection.IsDisclosable(chain, new HashSet<Guid>()).ShouldBeFalse();
        PhotoPositionProtection
            .IsDisclosable(chain, new HashSet<Guid> { GuardedCave })
            .ShouldBeTrue();
    }
}
