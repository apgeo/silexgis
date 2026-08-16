// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// How a pyramid is judged from its own bytes.
/// </summary>
/// <remarks>
/// Every rule here protects against a failure nothing reports. A tile mistaken for a compressed one
/// makes a whole good pyramid unservable; a level advertised and empty draws the level above at the
/// wrong resolution; tiles held and advertised nowhere are never asked for at all; and a version
/// that does not move when the tiles do is a changed pyramid served out of a week-old cache.
/// </remarks>
public class TerrainPyramidCheckTests
{
    /// <summary>
    /// The reason a mesh is identified from its numbers rather than from the two bytes at the
    /// front: it has no marker of its own there, so about one tile in sixty-five thousand begins
    /// with the compressed marker by coincidence — and because those bytes are a function of where
    /// the tile is, making the pyramid again brings the same coincidence back for ever.
    /// </summary>
    [Fact]
    public void A_mesh_that_happens_to_begin_with_the_compressed_marker_is_still_a_mesh()
    {
        var head = Mesh();
        head[0] = 0x1f;
        head[1] = 0x8b;

        TerrainPyramidCheck.Classify(head, 92 + (1200 * 6)).ShouldBe(TerrainTileKind.Mesh);
    }

    [Fact]
    public void A_compressed_tile_is_recognised_from_the_whole_of_its_header()
    {
        var compressed = new byte[128];
        compressed[0] = 0x1f;
        compressed[1] = 0x8b;
        compressed[2] = 0x08;
        compressed[3] = 0x00;

        TerrainPyramidCheck.Classify(compressed, compressed.Length).ShouldBe(TerrainTileKind.Compressed);

        // Those two bytes alone are not enough: this one declares a compression method the format
        // has never defined and sets reserved flag bits, so it is neither a mesh nor a stream.
        var pretender = new byte[128];
        pretender[0] = 0x1f;
        pretender[1] = 0x8b;
        pretender[2] = 0x99;
        pretender[3] = 0xff;

        TerrainPyramidCheck.Classify(pretender, pretender.Length).ShouldBe(TerrainTileKind.Damaged);
    }

    /// <summary>
    /// The shapes a disk that filled, or a run that was killed while writing, leaves behind. Each
    /// of them is a file of the right name in the right place that draws nothing.
    /// </summary>
    [Fact]
    public void A_tile_that_is_empty_short_or_cut_off_is_damaged()
    {
        TerrainPyramidCheck.Classify([], 0).ShouldBe(TerrainTileKind.Damaged);
        TerrainPyramidCheck.Classify(new byte[91], 91).ShouldBe(TerrainTileKind.Damaged);

        // A run of zeros satisfies every plausible range there is and declares no vertices at all.
        TerrainPyramidCheck.Classify(new byte[92], 92).ShouldBe(TerrainTileKind.Damaged);

        // Whole header, and the file stops well before the vertices it says it holds.
        TerrainPyramidCheck.Classify(Mesh(), 92).ShouldBe(TerrainTileKind.Damaged);
    }

    [Fact]
    public void A_header_whose_numbers_cannot_describe_ground_is_damaged()
    {
        var size = 92 + (1200 * 6);

        TerrainPyramidCheck.Classify(Mesh(centre: 9e9), size).ShouldBe(TerrainTileKind.Damaged);
        TerrainPyramidCheck.Classify(Mesh(lowest: 900f, highest: 200f), size).ShouldBe(TerrainTileKind.Damaged);
        TerrainPyramidCheck.Classify(Mesh(highest: 99_000f), size).ShouldBe(TerrainTileKind.Damaged);
        TerrainPyramidCheck.Classify(Mesh(vertices: 0), size).ShouldBe(TerrainTileKind.Damaged);
    }

    [Fact]
    public void A_level_the_pyramid_advertises_and_has_nothing_at_is_named()
    {
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> available =
        [
            [new TerrainTileRange(0, 0, 1, 0)],
            [new TerrainTileRange(0, 0, 3, 1)],
            [new TerrainTileRange(0, 0, 7, 3)],
        ];

        TerrainPyramidCheck
            .LevelsAdvertisedAndEmpty(available, new Dictionary<int, int> { [0] = 2, [1] = 8 })
            .ShouldBe([2]);

        // A whole pyramid says nothing, and neither does a level advertising no ground at all.
        TerrainPyramidCheck
            .LevelsAdvertisedAndEmpty(available, new Dictionary<int, int> { [0] = 2, [1] = 8, [2] = 1 })
            .ShouldBeEmpty();

        TerrainPyramidCheck
            .LevelsAdvertisedAndEmpty([[], []], new Dictionary<int, int>())
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The other direction, and the one that passes every other check: tiles that are there and
    /// that nothing will ever ask for, because the manifest says there is no ground where they are.
    /// </summary>
    [Fact]
    public void A_tile_outside_every_advertised_area_is_not_advertised()
    {
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> available =
        [
            [new TerrainTileRange(0, 0, 1, 0)],
            [new TerrainTileRange(4, 2, 5, 3)],
        ];

        TerrainPyramidCheck.IsAdvertised(available, new TerrainTileAddress(1, 4, 2)).ShouldBeTrue();
        TerrainPyramidCheck.IsAdvertised(available, new TerrainTileAddress(1, 5, 3)).ShouldBeTrue();

        // Two columns past the edge, the rectangle of a raster that is no longer advertised, and a
        // level the manifest does not mention at all.
        TerrainPyramidCheck.IsAdvertised(available, new TerrainTileAddress(1, 7, 2)).ShouldBeFalse();
        TerrainPyramidCheck.IsAdvertised(available, new TerrainTileAddress(1, 4, 9)).ShouldBeFalse();
        TerrainPyramidCheck.IsAdvertised(available, new TerrainTileAddress(2, 0, 0)).ShouldBeFalse();
    }

    /// <summary>
    /// The tile-maker writes a ring one tile wide around every rectangle it advertises, so that
    /// ring is accounted for rather than condemned.
    /// </summary>
    /// <remarks>
    /// The numbers here are a real pyramid's, read off one this pipeline baked: at level ten the
    /// manifest advertises columns 1154 to 1157 and rows 767 to 770, and the tiles on disk run from
    /// 1153 to 1158 and 766 to 771. Written out rather than described because the rule they justify
    /// is the difference between a check that refuses bad pyramids and one that refuses every
    /// pyramid, and a synthetic rectangle that matches its own manifest exactly — which is what was
    /// tested before — is a shape the tile-maker has never once produced.
    /// </remarks>
    [Fact]
    public void The_ring_the_tile_maker_writes_around_what_it_advertises_is_accounted_for()
    {
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> available =
            [[new TerrainTileRange(1154, 767, 1157, 770)]];

        for (var x = 1153; x <= 1158; x++)
        {
            for (var y = 766; y <= 771; y++)
            {
                TerrainPyramidCheck
                    .IsAdvertised(available, new TerrainTileAddress(0, x, y))
                    .ShouldBeTrue(FormattableString.Invariant($"tile {x}/{y} is in the ring"));
            }
        }

        // And the ring is one tile and no more, so the shape this check exists for — a pyramid
        // added to in place, whose manifest comes back describing only the newest raster and stops
        // advertising the rest — is still refused, whether it is far away or just beyond the ring.
        TerrainPyramidCheck
            .IsAdvertised(available, new TerrainTileAddress(0, 1159, 768)).ShouldBeFalse();
        TerrainPyramidCheck
            .IsAdvertised(available, new TerrainTileAddress(0, 1155, 765)).ShouldBeFalse();
        TerrainPyramidCheck
            .IsAdvertised(available, new TerrainTileAddress(0, 2048, 1400)).ShouldBeFalse();
    }

    [Fact]
    public void A_tile_says_where_it_sits_from_where_it_is_kept()
    {
        TerrainPyramidCheck.TryReadAddress("13/9243/6152.terrain", out var address).ShouldBeTrue();
        address.ShouldBe(new TerrainTileAddress(13, 9243, 6152));

        TerrainPyramidCheck.TryReadAddress("13/9243/6152.json", out _).ShouldBeFalse();
        TerrainPyramidCheck.TryReadAddress("9243/6152.terrain", out _).ShouldBeFalse();
        TerrainPyramidCheck.TryReadAddress("a/b/c.terrain", out _).ShouldBeFalse();
        TerrainPyramidCheck.TryReadAddress("13/-1/6152.terrain", out _).ShouldBeFalse();
    }

    /// <summary>
    /// The version is a cache key and nothing else. Two pyramids must never answer at the same
    /// addresses, and the same pyramid must always answer at the ones a browser already holds.
    /// </summary>
    [Fact]
    public void The_version_carries_the_content_and_is_stable_for_the_same_content()
    {
        const string digest = "9a6c569a9957deadbeef1234";

        TerrainPyramidCheck.VersionFrom(digest).ShouldBe("1.1.0-9a6c569a9957");
        TerrainPyramidCheck.VersionFrom(digest).ShouldBe(TerrainPyramidCheck.VersionFrom(digest));
        TerrainPyramidCheck.VersionFrom("0000111122223333").ShouldNotBe(TerrainPyramidCheck.VersionFrom(digest));

        // The tile-maker's own constant carries no dash, which is how one is told from the other,
        // and it has to survive being put on the end of an address.
        TerrainPyramidCheck.VersionFrom(digest).ShouldContain("-");
        Uri.EscapeDataString(TerrainPyramidCheck.VersionFrom(digest))
            .ShouldBe(TerrainPyramidCheck.VersionFrom(digest));
    }

    [Fact]
    public void The_credit_is_what_the_build_was_made_from_with_repeats_folded_together()
    {
        TerrainPyramidCheck.CreditFrom(["Copernicus DEM", "Copernicus DEM", " Copernicus DEM "])
            .ShouldBe("Copernicus DEM");

        TerrainPyramidCheck.CreditFrom(["Copernicus DEM", "A county survey"])
            .ShouldBe("Copernicus DEM; A county survey");

        // Nothing to say is said as nothing, never as filler: filler is displayed.
        TerrainPyramidCheck.CreditFrom([]).ShouldBeNull();
        TerrainPyramidCheck.CreditFrom([null, "  "]).ShouldBeNull();
    }

    [Fact]
    public void The_tile_makers_own_filler_is_told_apart_from_a_real_line()
    {
        TerrainPyramidCheck.IsFiller("insert attribution here").ShouldBeTrue();
        TerrainPyramidCheck.IsFiller(null).ShouldBeTrue();
        TerrainPyramidCheck.IsFiller("Copernicus DEM").ShouldBeFalse();
    }

    /// <summary>A tile header with numbers that describe real ground.</summary>
    private static byte[] Mesh(
        double centre = 4_200_000.5, float lowest = 210.5f, float highest = 1840.25f, uint vertices = 1200)
    {
        var head = new byte[92];
        BinaryPrimitives.WriteDoubleLittleEndian(head.AsSpan(0), centre);
        BinaryPrimitives.WriteDoubleLittleEndian(head.AsSpan(8), 1_700_000.25);
        BinaryPrimitives.WriteDoubleLittleEndian(head.AsSpan(16), 4_600_000.75);
        BinaryPrimitives.WriteSingleLittleEndian(head.AsSpan(24), lowest);
        BinaryPrimitives.WriteSingleLittleEndian(head.AsSpan(28), highest);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(88), vertices);
        return head;
    }
}
