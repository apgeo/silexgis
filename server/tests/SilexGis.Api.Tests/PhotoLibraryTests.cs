// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading a neighbouring photo library. These first cases need no database and no host: they pin
/// the two values that are put into an outbound request and would otherwise fail silently — the
/// order the rectangle is stated in, and what this application is willing to interpolate into a
/// request path.
///
/// <para>
/// Every rectangle, identifier and hash below is invented. Nothing here comes from any real
/// library, and no coordinate names a real place.
/// </para>
/// </summary>
public sealed class PhotoLibraryTests
{
    /// <summary>
    /// The library states a rectangle as north, east, south, west. This application states one as
    /// west, south, east, north, and GeoJSON states a position as longitude then latitude — so
    /// three orders are in play and two of them are wrong here.
    /// </summary>
    /// <remarks>
    /// The rectangle is chosen so that every one of the four numbers differs and the latitudes
    /// cannot be mistaken for the longitudes: a square, or one straddling the equator, would let a
    /// transposed pair produce the right string by accident, and this test exists precisely because
    /// a transposition produces an empty map rather than an error — which is indistinguishable from
    /// the feature being switched off.
    /// </remarks>
    [Fact]
    public void The_rectangle_is_stated_north_east_south_west()
    {
        var bounds = new Envelope(x1: 21.5, x2: 24.25, y1: 45.125, y2: 46.75);

        PhotoPrismClient.LatLng(bounds).ShouldBe("46.75,24.25,45.125,21.5");
    }

    /// <summary>
    /// Written with the invariant culture, because a host whose culture writes a decimal comma
    /// would otherwise send four numbers separated by seven commas and be told nothing about it.
    /// </summary>
    [Fact]
    public void The_rectangle_is_written_with_a_decimal_point()
    {
        var bounds = new Envelope(x1: -1.5, x2: -0.25, y1: 0.125, y2: 2.5);

        PhotoPrismClient.LatLng(bounds).ShouldBe("2.5,-0.25,0.125,-1.5");
        PhotoPrismClient.LatLng(bounds).Split(',').Length.ShouldBe(4);
    }

    /// <summary>
    /// The order this application uses for a rectangle everywhere else, written out here so the
    /// difference is visible rather than remembered: if the two ever agree, one of them has been
    /// changed by mistake.
    /// </summary>
    [Fact]
    public void The_rectangle_is_not_this_applications_own_order()
    {
        var bounds = new Envelope(x1: 21.5, x2: 24.25, y1: 45.125, y2: 46.75);
        var westSouthEastNorth = $"{bounds.MinX},{bounds.MinY},{bounds.MaxX},{bounds.MaxY}";

        PhotoPrismClient.LatLng(bounds).ShouldNotBe(westSouthEastNorth);
    }

    /// <summary>
    /// A reference is a foreign string that ends up inside a URL this application sends. Anything
    /// that could make it a different request is refused here rather than escaped later, because
    /// the escaping would have to be right in every one of the places a reference is used.
    /// </summary>
    [Theory]
    [InlineData("abcdef0123456789")]
    [InlineData("psxyz-1")]
    [InlineData("a_b-C9")]
    public void A_reference_of_letters_digits_hyphens_and_underscores_is_sent(string reference) =>
        PhotoLibraryHttp.IsSafeReference(reference).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    [InlineData("abc/def")]
    [InlineData("abc?size=huge")]
    [InlineData("abc def")]
    [InlineData("abc.def")]
    [InlineData("abc%2Fdef")]
    public void A_reference_that_could_ask_a_different_question_is_refused(string? reference) =>
        PhotoLibraryHttp.IsSafeReference(reference).ShouldBeFalse();

    [Fact]
    public void A_reference_longer_than_any_library_mints_is_refused() =>
        PhotoLibraryHttp.IsSafeReference(new string('a', 129)).ShouldBeFalse();

    /// <summary>
    /// The one that decides whether a library keeps its photographs. This product draws a
    /// placeholder instead of failing when it went looking for a file on disk and did not find it,
    /// and that same act marks the file missing and deletes the photograph from its own index — so
    /// a drawing carrying a successful status is the dangerous answer, not a harmless one, and
    /// reading it as harmless is how a map viewport becomes a purge.
    /// </summary>
    [Fact]
    public void A_drawing_where_a_photograph_was_expected_stops_the_picture_path()
    {
        using var answer = Answered(HttpStatusCode.OK, "image/svg+xml");

        PhotoLibraryHttp.ClassifyPicture(answer).ShouldBe(PictureVerdict.NotAPicture);
    }

    /// <summary>
    /// The same drawing carrying a refusal is a different sentence: the library declined the
    /// request before looking for anything on disk, so nothing of its is at risk and the picture
    /// path stays open.
    /// </summary>
    [Fact]
    public void A_drawing_carrying_a_refusal_is_a_defect_on_this_side_and_not_a_danger()
    {
        using var answer = Answered(HttpStatusCode.BadRequest, "image/svg+xml");

        PhotoLibraryHttp.ClassifyPicture(answer).ShouldBe(PictureVerdict.WrongRendering);
    }

    [Fact]
    public void A_picture_is_recognised_as_one()
    {
        using var answer = Answered(HttpStatusCode.OK, "image/jpeg");

        PhotoLibraryHttp.ClassifyPicture(answer).ShouldBe(PictureVerdict.Picture);
    }

    /// <summary>
    /// A rejected credential is told apart from a lost disk, because the two send whoever has to
    /// act on them to completely different places.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void A_refused_credential_is_not_read_as_a_lost_disk(HttpStatusCode status)
    {
        using var answer = Answered(status, "image/svg+xml");

        PhotoLibraryHttp.ClassifyPicture(answer).ShouldBe(PictureVerdict.Unauthorized);
    }

    /// <summary>
    /// A page of markup where a picture was expected is a library in trouble, and is treated as the
    /// dangerous case: nothing in the answer says which kind of trouble, and only one of the kinds
    /// is recoverable.
    /// </summary>
    [Fact]
    public void A_page_of_markup_where_a_picture_was_expected_stops_the_picture_path()
    {
        using var answer = Answered(HttpStatusCode.OK, "text/html");

        PhotoLibraryHttp.ClassifyPicture(answer).ShouldBe(PictureVerdict.NotAPicture);
    }

    private static HttpResponseMessage Answered(HttpStatusCode status, string mediaType) =>
        new(status)
        {
            Content = new ByteArrayContent([0x00])
            {
                Headers = { ContentType = new MediaTypeHeaderValue(mediaType) },
            },
        };
}
