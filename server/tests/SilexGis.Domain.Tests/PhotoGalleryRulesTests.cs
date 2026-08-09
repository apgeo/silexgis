// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The pure rules the gallery rests on: turning a picture without rewriting it, what may be
/// done with one, where it sits in an album, and how long a deleted one can be got back.
/// </summary>
public class PhotoGalleryRulesTests
{
    [Fact]
    public void A_turn_is_normalised_into_a_quarter_of_a_circle()
    {
        PhotoOrientation.Normalize(0).ShouldBe(0);
        PhotoOrientation.Normalize(3).ShouldBe(3);
        PhotoOrientation.Normalize(4).ShouldBe(0);
        PhotoOrientation.Normalize(7).ShouldBe(3);

        // "Turn it the other way" is the natural thing to send, and −1 is the same picture as 3.
        // C#'s remainder keeps the sign of its left operand, so this is the case that breaks if
        // the second modulus is dropped.
        PhotoOrientation.Normalize(-1).ShouldBe(3);
        PhotoOrientation.Normalize(-4).ShouldBe(0);
        PhotoOrientation.Normalize(-5).ShouldBe(3);
    }

    [Fact]
    public void Turning_a_picture_all_the_way_round_leaves_it_where_it_started()
    {
        // Which is the point of storing a turn rather than rewriting the bytes: four rotations
        // cost nothing and leave the upload byte-identical to what arrived.
        var back = Enumerable.Range(0, 4).Aggregate(0, (current, _) => PhotoOrientation.Rotate(current, 1));

        back.ShouldBe(0);
        PhotoOrientation.Rotate(1, -1).ShouldBe(0);
        PhotoOrientation.Rotate(3, 1).ShouldBe(0);
    }

    [Fact]
    public void A_quarter_turn_swaps_the_sides_and_a_half_turn_does_not()
    {
        // What a grid needs before it has any bytes: a portrait picture turned once is
        // landscape, and a tile sized from the stored dimensions alone is the wrong shape.
        PhotoOrientation.Apply(4000, 3000, 0).ShouldBe((4000, 3000));
        PhotoOrientation.Apply(4000, 3000, 1).ShouldBe((3000, 4000));
        PhotoOrientation.Apply(4000, 3000, 2).ShouldBe((4000, 3000));
        PhotoOrientation.Apply(4000, 3000, 3).ShouldBe((3000, 4000));

        PhotoOrientation.SwapsAxes(1).ShouldBeTrue();
        PhotoOrientation.SwapsAxes(2).ShouldBeFalse();
        PhotoOrientation.Degrees(3).ShouldBe(270);
    }

    [Fact]
    public void A_licence_is_one_of_the_known_codes_or_nothing_at_all()
    {
        PhotoLicences.IsKnown(PhotoLicences.CcBySa).ShouldBeTrue();
        PhotoLicences.IsKnown(null).ShouldBeTrue();
        PhotoLicences.IsKnown("  ").ShouldBeTrue();

        // Free text is what the column exists to prevent: "ask Ana" answers nothing.
        PhotoLicences.IsKnown("ask Ana").ShouldBeFalse();
        PhotoLicences.IsKnown("CC-BY").ShouldBeFalse();
    }

    [Fact]
    public void Nobody_having_said_is_not_the_same_as_all_rights_reserved()
    {
        // Both refuse reuse, and they must still read differently: one is a decision somebody
        // made, the other is a decision nobody has made yet.
        PhotoLicences.AllowsReuse(null).ShouldBeFalse();
        PhotoLicences.AllowsReuse(PhotoLicences.AllRightsReserved).ShouldBeFalse();
        PhotoLicences.All.ShouldContain(PhotoLicences.AllRightsReserved);
        PhotoLicences.All.ShouldNotContain((string?)null!);
    }

    [Fact]
    public void The_open_licences_allow_reuse_and_all_but_the_dedication_want_a_credit()
    {
        foreach (var code in new[]
                 {
                     PhotoLicences.Cc0, PhotoLicences.CcBy, PhotoLicences.CcBySa,
                     PhotoLicences.CcByNc, PhotoLicences.CcByNcSa, PhotoLicences.CcByNd,
                     PhotoLicences.CcByNcNd,
                 })
        {
            PhotoLicences.AllowsReuse(code).ShouldBeTrue(code);
        }

        PhotoLicences.RequiresAttribution(PhotoLicences.Cc0).ShouldBeFalse();
        PhotoLicences.RequiresAttribution(PhotoLicences.CcBy).ShouldBeTrue();
        PhotoLicences.RequiresAttribution(null).ShouldBeFalse();
    }

    [Fact]
    public void A_picture_appended_to_an_album_goes_after_the_last_one()
    {
        AlbumOrdering.Append(null).ShouldBe(AlbumOrdering.Step);
        AlbumOrdering.Append(AlbumOrdering.Step).ShouldBe(2 * AlbumOrdering.Step);
    }

    [Fact]
    public void Dropping_between_two_pictures_rewrites_one_row_rather_than_the_album()
    {
        // The whole reason positions are sparse: a drag is a great many small moves, and
        // renumbering the album on each is both slow and a large audit entry for a nudge.
        AlbumOrdering.Between(1000, 2000).ShouldBe(1500);
        AlbumOrdering.Between(null, null).ShouldBe(AlbumOrdering.Step);
        AlbumOrdering.Between(2000, null).ShouldBe(2000 + AlbumOrdering.Step);
        AlbumOrdering.Between(null, 2000)!.Value.ShouldBeLessThan(2000);
    }

    [Fact]
    public void When_the_gap_runs_out_the_album_says_so_rather_than_guessing()
    {
        // Repeatedly dropping into the same place halves the space each time; eventually there
        // is nowhere left, and the answer is to spread the sequence out rather than to invent a
        // position that collides.
        AlbumOrdering.Between(1000, 1001).ShouldBeNull();
        AlbumOrdering.Between(1000, 1002).ShouldBe(1001);
    }

    [Fact]
    public void Spreading_an_album_out_again_gives_it_evenly_spaced_room()
    {
        AlbumOrdering.Respread(0).ShouldBeEmpty();
        AlbumOrdering.Respread(3).ShouldBe(new[]
        {
            AlbumOrdering.Step, 2 * AlbumOrdering.Step, 3 * AlbumOrdering.Step,
        });
    }

    [Fact]
    public void A_deleted_document_is_restorable_inside_its_window_and_purgeable_after_it()
    {
        var deleted = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var retention = TimeSpan.FromDays(30);

        SoftDeleteRules.IsRestorable(deleted, deleted.AddDays(29), retention).ShouldBeTrue();
        SoftDeleteRules.IsPurgeable(deleted, deleted.AddDays(29), retention).ShouldBeFalse();

        SoftDeleteRules.IsRestorable(deleted, deleted.AddDays(31), retention).ShouldBeFalse();
        SoftDeleteRules.IsPurgeable(deleted, deleted.AddDays(31), retention).ShouldBeTrue();
    }

    [Fact]
    public void The_two_readings_never_overlap_so_nothing_is_both_restorable_and_purged()
    {
        var deleted = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var retention = TimeSpan.FromDays(30);

        foreach (var days in new[] { 0, 1, 29, 30, 31, 400 })
        {
            var now = deleted.AddDays(days);
            SoftDeleteRules.IsRestorable(deleted, now, retention)
                .ShouldNotBe(SoftDeleteRules.IsPurgeable(deleted, now, retention), $"day {days}");
        }
    }

    [Fact]
    public void The_sweep_is_given_a_cut_off_rather_than_a_predicate_per_row()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var retention = TimeSpan.FromDays(30);

        var cutoff = SoftDeleteRules.PurgeCutoff(now, retention);

        cutoff.ShouldBe(now - retention);
        // Anything deleted at or before the cut-off is past its window, which is exactly what an
        // indexed comparison on the column answers.
        SoftDeleteRules.IsPurgeable(cutoff, now, retention).ShouldBeTrue();
        SoftDeleteRules.IsPurgeable(cutoff.AddSeconds(1), now, retention).ShouldBeFalse();
    }

    [Fact]
    public void What_is_left_of_the_window_is_never_negative()
    {
        var deleted = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var retention = TimeSpan.FromDays(30);

        SoftDeleteRules.RemainingWindow(deleted, deleted.AddDays(10), retention)
            .ShouldBe(TimeSpan.FromDays(20));

        // A "deleted, −4 days left" line is worse than no line at all.
        SoftDeleteRules.RemainingWindow(deleted, deleted.AddDays(34), retention)
            .ShouldBe(TimeSpan.Zero);
    }
}
