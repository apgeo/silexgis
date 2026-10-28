// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The transform from a picture as stored to its upright rendering, stated as a flip left to
/// right first and a clockwise turn after it — checked for every orientation code against every
/// recorded turn.
/// </summary>
/// <remarks>
/// The expected answer is worked out here from what each orientation code means in the EXIF and
/// TIFF definitions — which side of the upright picture the stored first row and first column end
/// up on — and is never derived from the code under test. Both sides are compared as the whole
/// permutation of a small non-square grid, so a transform that agrees on one pixel and not on the
/// others still fails.
/// </remarks>
public class PhotoOrientationUprightTests
{
    private const int Width = 5;
    private const int Height = 3;

    public static TheoryData<int?, int> EveryCodeAndTurn()
    {
        var data = new TheoryData<int?, int>();
        foreach (var code in new int?[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            for (var turns = 0; turns < 4; turns++)
            {
                data.Add(code, turns);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCodeAndTurn))]
    public void Flipping_first_and_then_turning_draws_every_pixel_where_the_code_and_the_turn_put_it(int? code, int turns)
    {
        var (mirrored, degrees) = PhotoOrientation.Upright(code, turns);

        degrees.ShouldBeOneOf(0, 90, 180, 270);
        Permutation((x, y) => FlipThenTurn(x, y, mirrored, degrees))
            .ShouldBe(Permutation((x, y) => AsDefined(x, y, code!.Value, turns)));
    }

    [Fact]
    public void The_four_codes_that_mirror_say_so_and_the_others_do_not()
    {
        foreach (var code in new[] { 2, 4, 5, 7 })
        {
            PhotoOrientation.Upright(code, 0).Mirrored.ShouldBeTrue($"code {code}");
        }

        foreach (var code in new[] { 1, 3, 6, 8 })
        {
            PhotoOrientation.Upright(code, 0).Mirrored.ShouldBeFalse($"code {code}");
        }
    }

    [Fact]
    public void A_picture_with_no_code_or_one_the_renderer_cannot_read_is_turned_by_the_recorded_turn_alone()
    {
        foreach (var code in new int?[] { null, 0, 9, -1 })
        {
            PhotoOrientation.Upright(code, 0).ShouldBe((false, 0));
            PhotoOrientation.Upright(code, 1).ShouldBe((false, 90));
            PhotoOrientation.Upright(code, 3).ShouldBe((false, 270));
            PhotoOrientation.Upright(code, -1).ShouldBe((false, 270), "a turn back is normalised like any other");
        }
    }

    [Fact]
    public void The_upright_size_swaps_the_sides_exactly_when_the_combined_turn_is_a_quarter()
    {
        // A phone's portrait photograph, stored on its side with code 6.
        PhotoOrientation.UprightSize(4000, 3000, 6, 0).ShouldBe((3000, 4000));
        // Turned back a quarter by hand: the code's quarter and the recorded three add to a whole.
        PhotoOrientation.UprightSize(4000, 3000, 6, 3).ShouldBe((4000, 3000));
        PhotoOrientation.UprightSize(4000, 3000, 3, 0).ShouldBe((4000, 3000));
        PhotoOrientation.UprightSize(4000, 3000, 5, 0).ShouldBe((3000, 4000));
        PhotoOrientation.UprightSize(4000, 3000, null, 1).ShouldBe((3000, 4000));
        PhotoOrientation.UprightSize(4000, 3000, null, 0).ShouldBe((4000, 3000));
    }

    /// <summary>
    /// Where each stored pixel lands, keyed by its stored position, with the upright frame's size.
    /// </summary>
    private static string Permutation(Func<int, int, (int X, int Y, int W, int H)> map)
    {
        var landed = new List<string>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var to = map(x, y);
                landed.Add($"({x},{y})->({to.X},{to.Y}) in {to.W}x{to.H}");
            }
        }

        return string.Join("; ", landed);
    }

    /// <summary>The documented formulas: flip left to right if mirrored, then the clockwise turn.</summary>
    private static (int X, int Y, int W, int H) FlipThenTurn(int x, int y, bool mirrored, int degrees)
    {
        var fx = mirrored ? Width - 1 - x : x;
        var fy = y;
        return degrees switch
        {
            0 => (fx, fy, Width, Height),
            90 => (Height - 1 - fy, fx, Height, Width),
            180 => (Width - 1 - fx, Height - 1 - fy, Width, Height),
            270 => (fy, Width - 1 - fx, Height, Width),
            _ => throw new ArgumentOutOfRangeException(nameof(degrees), degrees, null),
        };
    }

    /// <summary>
    /// The code read as its definition states it — the side of the upright picture the stored first
    /// row is on, then the side the stored first column is on — followed by the recorded quarter
    /// turns, one clockwise quarter at a time.
    /// </summary>
    private static (int X, int Y, int W, int H) AsDefined(int x, int y, int code, int turns)
    {
        // 1 top/left, 2 top/right, 3 bottom/right, 4 bottom/left,
        // 5 left/top, 6 right/top, 7 right/bottom, 8 left/bottom.
        int ux, uy, w, h;
        switch (code)
        {
            case 1: (ux, uy) = (x, y); break;
            case 2: (ux, uy) = (Width - 1 - x, y); break;
            case 3: (ux, uy) = (Width - 1 - x, Height - 1 - y); break;
            case 4: (ux, uy) = (x, Height - 1 - y); break;
            case 5: (ux, uy) = (y, x); break;
            case 6: (ux, uy) = (Height - 1 - y, x); break;
            case 7: (ux, uy) = (Height - 1 - y, Width - 1 - x); break;
            case 8: (ux, uy) = (y, Width - 1 - x); break;
            default: throw new ArgumentOutOfRangeException(nameof(code), code, null);
        }

        (w, h) = code <= 4 ? (Width, Height) : (Height, Width);
        for (var i = 0; i < turns; i++)
        {
            (ux, uy, w, h) = (h - 1 - uy, ux, h, w);
        }

        return (ux, uy, w, h);
    }
}
