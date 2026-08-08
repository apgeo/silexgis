// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a photograph proposes to call the object it becomes. The bar is deliberately high: an
/// empty name a reviewer fills in costs one field, and a registry full of caves called
/// "IMG_2043" costs an afternoon.
/// </summary>
public class PhotoNameProposalTests
{
    [Theory]
    [InlineData("IMG_2043.JPG")]
    [InlineData("DSC01234.jpg")]
    [InlineData("DSCF0001.RAF")]
    [InlineData("20260808_114233.jpg")]
    [InlineData("20260808.jpg")]
    [InlineData("P1010023.JPG")]
    [InlineData("photo_12.png")]
    [InlineData("poza 3.jpg")]
    [InlineData("1234567.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_the_camera_chose_proposes_nothing(string? fileName)
    {
        PhotoNameProposal.From(fileName).ShouldBeNull();
    }

    [Theory]
    [InlineData("Pestera Ursilor.jpg", "Pestera Ursilor")]
    [InlineData("Peștera Ursilor.JPG", "Peștera Ursilor")]
    [InlineData("Avenul din Sesuri.png", "Avenul din Sesuri")]
    [InlineData("intrare P4.jpg", "intrare P4")]
    public void A_name_a_person_chose_is_proposed(string fileName, string expected)
    {
        PhotoNameProposal.From(fileName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Ursilor (2).jpg", "Ursilor")]
    [InlineData("Ursilor-3.jpg", "Ursilor")]
    [InlineData("Ursilor 2.jpg", "Ursilor")]
    public void A_counter_a_phone_added_to_distinguish_three_shots_is_not_part_of_the_name(
        string fileName, string expected)
    {
        PhotoNameProposal.From(fileName).ShouldBe(expected);
    }

    [Fact]
    public void The_first_picture_that_offers_a_usable_name_wins()
    {
        // The pictures arrive in capture order, so this is the earliest one somebody renamed.
        PhotoNameProposal.From(["IMG_2041.jpg", "IMG_2042.jpg", "Izbucul Galbenei.jpg", "Ursilor.jpg"])
            .ShouldBe("Izbucul Galbenei");
    }

    [Fact]
    public void A_drop_of_nothing_but_camera_names_proposes_nothing()
    {
        PhotoNameProposal.From(["IMG_2041.jpg", "IMG_2042.jpg"]).ShouldBeNull();
    }

    [Fact]
    public void An_absurdly_long_file_name_is_not_a_name()
    {
        PhotoNameProposal.From(new string('a', 150) + ".jpg").ShouldBeNull();
    }
}
