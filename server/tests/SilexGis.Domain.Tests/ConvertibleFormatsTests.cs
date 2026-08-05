// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which formats a converted copy would give pages to. The answer is a property of the format
/// and nothing else — in particular it does not change with what an installation happens to
/// have deployed, so that "this could be shown" and "nothing here can show it" stay two
/// separate statements rather than one muddled one.
/// </summary>
public class ConvertibleFormatsTests
{
    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("application/msword")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/vnd.ms-powerpoint")]
    [InlineData("application/rtf")]
    [InlineData("application/vnd.oasis.opendocument.text")]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet")]
    // A legacy compound file whose sub-type nothing settled is one of the office formats, and
    // an office suite opens it whichever one it turns out to be.
    [InlineData("application/x-ole-storage")]
    public void An_office_document_has_no_pages_until_something_lays_it_out(string mediaType) =>
        ConvertibleFormats.CanConvert(mediaType).ShouldBeTrue();

    [Theory]
    // Already paginates itself. Converting it would replace a document with a worse copy.
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("audio/mpeg")]
    [InlineData("video/mp4")]
    // Shown as itself, and a converted copy would be a rendering of something already readable.
    [InlineData("text/plain")]
    [InlineData("text/csv")]
    [InlineData("application/geo+json")]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_gains_nothing_from_being_converted(string? mediaType) =>
        ConvertibleFormats.CanConvert(mediaType).ShouldBeFalse();

    [Fact]
    public void A_converted_copy_is_a_portable_document_and_therefore_never_converted_again() =>
        ConvertibleFormats.CanConvert(ConvertibleFormats.TargetMediaType).ShouldBeFalse();
}
