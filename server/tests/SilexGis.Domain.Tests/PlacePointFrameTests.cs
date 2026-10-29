// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Domain.Tests;

public class PlacePointFrameTests
{
    public static TheoryData<int, int> CodesAndTurns()
    {
        var data = new TheoryData<int, int>();
        foreach (var code in Enumerable.Range(1, 8))
        {
            foreach (var turns in Enumerable.Range(0, 4))
            {
                data.Add(code, turns);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CodesAndTurns))]
    public void Every_pixel_of_a_non_square_picture_comes_back_from_its_fraction(int code, int turns)
    {
        var frame = PictureFrame.Of(7, 4, code, turns);
        var seen = new HashSet<(int, int)>();
        for (var x = 0; x < frame.Width; x++)
        {
            for (var y = 0; y < frame.Height; y++)
            {
                var (fx, fy) = PlacePointFrame.ToFraction(frame, x, y)!.Value;
                fx.ShouldBeInRange(0, 1);
                fy.ShouldBeInRange(0, 1);
                PlacePointFrame.FromFraction(frame, fx, fy).ShouldBe((x, y), $"code {code}, turns {turns}");

                // Every stored pixel lands on its own upright pixel, inside the upright frame.
                var (u, v) = PlacePointFrame.ToUpright(frame, x, y);
                u.ShouldBeInRange(0, frame.UprightWidth - 1);
                v.ShouldBeInRange(0, frame.UprightHeight - 1);
                seen.Add((u, v)).ShouldBeTrue();
            }
        }

        seen.Count.ShouldBe(28);
    }

    [Theory]
    [InlineData(1, 0, 0)] // upright: the top-left stays top-left
    [InlineData(6, 3, 0)] // a quarter turn clockwise: top-left goes top-right
    [InlineData(3, 6, 3)] // half a turn: bottom-right
    [InlineData(8, 0, 6)] // three quarters: bottom-left
    [InlineData(2, 6, 0)] // mirrored: top-right
    public void The_top_left_stored_pixel_lands_where_the_renderer_draws_it(int code, int u, int v)
    {
        var frame = PictureFrame.Of(7, 4, code, 0);
        PlacePointFrame.ToUpright(frame, 0, 0).ShouldBe((u, v));
    }

    [Fact]
    public void A_recorded_turn_adds_to_the_orientation_code()
    {
        // Code 6 is already a quarter turn; three more make it upright again.
        var frame = PictureFrame.Of(7, 4, 6, 3);
        frame.TurnDegrees.ShouldBe(0);
        frame.Mirrored.ShouldBeFalse();
        PlacePointFrame.ToUpright(frame, 2, 1).ShouldBe((2, 1));
    }

    [Fact]
    public void A_mirror_is_exactly_one_minus_the_fraction()
    {
        var plain = PictureFrame.Of(10, 5, 1, 0);
        var mirrored = PictureFrame.Of(10, 5, 2, 0);
        var (fx, fy) = PlacePointFrame.ToFraction(plain, 3, 2)!.Value;
        var (mx, my) = PlacePointFrame.ToFraction(mirrored, 3, 2)!.Value;
        mx.ShouldBe(1 - fx, 1e-12);
        my.ShouldBe(fy);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(999_999, 0)]
    [InlineData(0, 499_999)]
    [InlineData(999_999, 499_999)]
    [InlineData(123_457, 98_765)]
    public void Whole_pixels_of_the_largest_picture_come_back_exactly(int x, int y)
    {
        foreach (var code in Enumerable.Range(1, 8))
        {
            foreach (var turns in Enumerable.Range(0, 4))
            {
                var frame = PictureFrame.Of(1_000_000, 500_000, code, turns);
                var (fx, fy) = PlacePointFrame.ToFraction(frame, x, y)!.Value;
                PlacePointFrame.FromFraction(frame, fx, fy).ShouldBe((x, y));
            }
        }
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(0, 4)]
    [InlineData(-1, 0)]
    [InlineData(100, 100)]
    public void A_pixel_off_the_picture_has_no_fraction(int x, int y)
    {
        PlacePointFrame.ToFraction(PictureFrame.Of(7, 4, null, 0), x, y).ShouldBeNull();
    }

    [Fact]
    public void A_fraction_on_the_far_edge_is_the_last_pixel()
    {
        PlacePointFrame.FromFraction(PictureFrame.Of(7, 4, null, 0), 1, 1).ShouldBe((6, 3));
    }

    [Fact]
    public void The_anchor_carries_the_pixel_and_the_fraction_and_reads_back()
    {
        var frame = PictureFrame.Of(7, 4, 6, 0);
        var anchor = PlacePointAnchor.Write(2, 1, frame);
        var parsed = PlacePointAnchor.Read(anchor)!.Value;
        parsed.Px.ShouldBe(2);
        parsed.Py.ShouldBe(1);
        PlacePointFrame.FromFraction(frame, parsed.X!.Value, parsed.Y!.Value).ShouldBe((2, 1));
        PlacePointAnchor.CarriesStoredPixel(anchor).ShouldBeTrue();
        ResLinkRules.AnchorPayloadProblem(AnchorKind.ImageRegion, anchor).ShouldBeNull();
    }

    [Fact]
    public void A_pixel_off_the_picture_or_on_an_unknown_frame_is_written_without_a_fraction()
    {
        foreach (var anchor in new[]
                 {
                     PlacePointAnchor.Write(70, 1, PictureFrame.Of(7, 4, null, 0)),
                     PlacePointAnchor.Write(2, 1, (PictureFrame?)null),
                 })
        {
            var parsed = PlacePointAnchor.Read(anchor)!.Value;
            parsed.X.ShouldBeNull();
            parsed.Y.ShouldBeNull();
            ResLinkRules.AnchorPayloadProblem(AnchorKind.ImageRegion, anchor).ShouldBeNull();
        }
    }

    [Fact]
    public void An_ordinary_point_is_no_place_pin()
    {
        PlacePointAnchor.Read("""{"shape":"point","x":0.5,"y":0.5}""").ShouldBeNull();
        PlacePointAnchor.CarriesStoredPixel("""{"shape":"point","x":0.5,"y":0.5}""").ShouldBeFalse();
        PlacePointAnchor.CarriesStoredPixel(null).ShouldBeFalse();
        PlacePointAnchor.CarriesStoredPixel("not json").ShouldBeFalse();
    }
}
