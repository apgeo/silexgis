// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What an upload actually is, decided from its first bytes. The signatures are the
/// load-bearing part: text extraction dispatches on the recorded format, so a file filed
/// under the wrong one is handed to a reader that cannot read it.
/// </summary>
public class FileFormatsTests
{
    [Theory]
    [InlineData("image/png", FileKind.Image)]
    [InlineData("image/jpeg", FileKind.Image)]
    [InlineData("image/gif", FileKind.Image)]
    [InlineData("image/tiff", FileKind.Image)]
    [InlineData("image/bmp", FileKind.Image)]
    [InlineData("image/webp", FileKind.Image)]
    [InlineData("image/svg+xml", FileKind.Image)]
    [InlineData("image/heic", FileKind.Image)]
    [InlineData("image/heif", FileKind.Image)]
    [InlineData("image/avif", FileKind.Image)]
    [InlineData("application/pdf", FileKind.Document)]
    [InlineData("application/rtf", FileKind.Document)]
    [InlineData("application/vnd.oasis.opendocument.text", FileKind.Document)]
    [InlineData("audio/mpeg", FileKind.Audio)]
    [InlineData("audio/flac", FileKind.Audio)]
    [InlineData("audio/ogg", FileKind.Audio)]
    [InlineData("audio/wav", FileKind.Audio)]
    [InlineData("audio/mp4", FileKind.Audio)]
    [InlineData("video/mp4", FileKind.Video)]
    [InlineData("video/quicktime", FileKind.Video)]
    [InlineData("video/webm", FileKind.Video)]
    [InlineData("video/x-matroska", FileKind.Video)]
    [InlineData("video/x-msvideo", FileKind.Video)]
    [InlineData("video/mpeg", FileKind.Video)]
    [InlineData("application/gzip", FileKind.Other)]
    [InlineData("application/zip", FileKind.Other)]
    public void Header_signature_decides_format_and_kind(string expectedMimeType, FileKind expectedKind)
    {
        var format = FileFormats.FromHeader(Sample(expectedMimeType));

        format.ShouldNotBeNull();
        format.MimeType.ShouldBe(expectedMimeType);
        format.Kind.ShouldBe(expectedKind);
    }

    [Fact]
    public void What_a_label_would_have_said_never_reaches_bytes_that_speak_for_themselves()
    {
        // The signature table takes no label at all, which is the design: bytes that carry a
        // signature are decided before any hint is consulted. What this proves is that the
        // two answers genuinely differ, so believing the label would have filed a photograph
        // as a text document and sent it to a reader that cannot read it.
        var png = Sample("image/png");

        var fromBytes = FileFormats.FromHeader(png);
        fromBytes.ShouldNotBeNull();
        fromBytes.Kind.ShouldBe(FileKind.Image);
        fromBytes.MimeType.ShouldBe("image/png");

        FileFormats.KindOf("text/plain").ShouldBe(FileKind.Document);

        // And the honest twin — the same bytes labelled correctly — lands on the same answer,
        // so the rule is "the bytes decide" and not "distrust whoever labelled it".
        FileFormats.KindOf("image/png").ShouldBe(fromBytes.Kind);
    }

    [Fact]
    public void A_photograph_in_the_mp4_container_family_is_an_image_and_not_a_recording()
    {
        // HEIC and AVIF are ISO base media files in exactly the way an MP4 is, and the brand
        // after "ftyp" is the only thing telling them apart. Getting this wrong is not
        // cosmetic: reading a photo's capture location out of its EXIF, counting it as one
        // page and offering it a thumbnail all happen for images and for nothing else, so a
        // photograph filed as a recording silently loses all three.
        foreach (var (bytes, expected) in ((byte[], string)[])
                 [(Sample("image/heic"), "image/heic"), (Sample("image/avif"), "image/avif")])
        {
            var photograph = FileFormats.FromHeader(bytes);
            photograph.ShouldNotBeNull();
            photograph.Kind.ShouldBe(FileKind.Image);
            photograph.MimeType.ShouldBe(expected);
        }

        // The twin that shares the container with them byte for byte and differs only in its
        // brand: it is still video, so the rule is the brand and not "ftyp now means image".
        var recording = FileFormats.FromHeader(Sample("video/mp4"));
        recording.ShouldNotBeNull();
        recording.Kind.ShouldBe(FileKind.Video);
        recording.MimeType.ShouldBe("video/mp4");
    }

    [Fact]
    public void Legacy_compound_office_files_are_documents_refined_by_extension()
    {
        var format = FileFormats.FromHeader(Sample("application/x-ole-storage"));

        format.ShouldNotBeNull();
        format.Kind.ShouldBe(FileKind.Document);
        format.MimeType.ShouldBe(FileFormats.CompoundFileMimeType);

        FileFormats.CompoundFileMimeTypeFor("notes.doc").ShouldBe("application/msword");
        FileFormats.CompoundFileMimeTypeFor("budget.XLS").ShouldBe("application/vnd.ms-excel");
        FileFormats.CompoundFileMimeTypeFor("mystery.bin").ShouldBe(FileFormats.CompoundFileMimeType);
    }

    [Fact]
    public void Plain_text_carries_no_signature_and_is_recognised_as_text()
    {
        var text = Encoding.UTF8.GetBytes("Raport de tură, Peștera Urșilor\nAdâncime: 42 m\n");

        FileFormats.FromHeader(text).ShouldBeNull();
        FileFormats.LooksLikeText(text).ShouldBeTrue();
    }

    [Fact]
    public void Binary_bytes_that_match_nothing_are_not_mistaken_for_text()
    {
        byte[] binary = [0x03, 0x00, 0x7F, 0x00, 0xFE, 0x01, 0x00, 0x00, 0x11, 0x22];

        FileFormats.FromHeader(binary).ShouldBeNull();
        FileFormats.LooksLikeText(binary).ShouldBeFalse();

        // The positive twin: the same length of genuinely textual bytes does pass.
        FileFormats.LooksLikeText(Encoding.UTF8.GetBytes("hello, câmp\n")).ShouldBeTrue();
    }

    [Fact]
    public void A_byte_order_mark_does_not_stop_text_from_reading_as_text()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("titlu;valoare\n")];

        FileFormats.LooksLikeText(withBom).ShouldBeTrue();
    }

    [Theory]
    [InlineData("text/csv", "table.csv", "text/csv")]
    [InlineData("application/json", "data.json", "application/json")]
    [InlineData("application/octet-stream", "notes.md", "text/markdown")]
    [InlineData("application/octet-stream", "track.gpx", "application/gpx+xml")]
    [InlineData("application/x-invented", "mystery", "text/plain")]
    [InlineData(null, null, "text/plain")]
    public void Text_subtype_takes_the_hint_only_within_the_class_the_bytes_decided(
        string? declared, string? fileName, string expected) =>
        FileFormats.TextMimeTypeFor(declared, fileName).ShouldBe(expected);

    [Theory]
    [InlineData("image/png", FileKind.Image)]
    [InlineData("audio/ogg", FileKind.Audio)]
    [InlineData("video/webm", FileKind.Video)]
    [InlineData("text/markdown", FileKind.Document)]
    [InlineData("application/pdf", FileKind.Document)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileKind.Document)]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", FileKind.Document)]
    [InlineData("application/octet-stream", FileKind.Other)]
    [InlineData(null, FileKind.Other)]
    public void Media_type_maps_to_a_kind_when_the_bytes_said_nothing(string? mimeType, FileKind expected) =>
        FileFormats.KindOf(mimeType).ShouldBe(expected);

    [Fact]
    public void Media_type_parameters_are_not_part_of_the_type()
    {
        FileFormats.BaseType("Text/Plain; charset=UTF-8").ShouldBe("text/plain");
        FileFormats.BaseType("   ").ShouldBeNull();
    }

    [Fact]
    public void A_header_shorter_than_any_signature_matches_nothing()
    {
        FileFormats.FromHeader([0x89, 0x50]).ShouldBeNull();
        FileFormats.LooksLikeText([]).ShouldBeFalse();
    }

    /// <summary>
    /// A header that really starts the named format. Only the leading bytes matter here —
    /// that is exactly the claim being tested.
    /// </summary>
    private static byte[] Sample(string mimeType) => mimeType switch
    {
        "image/png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D],
        "image/jpeg" => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. Ascii("JFIF")],
        "image/gif" => [.. Ascii("GIF89a"), 0x01, 0x00, 0x01, 0x00],
        "image/tiff" => [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00],
        "image/bmp" => [.. Ascii("BM"), 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00],
        "image/webp" => [.. Ascii("RIFF"), 0x24, 0x00, 0x00, 0x00, .. Ascii("WEBPVP8 ")],
        "image/svg+xml" => Ascii("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"),
        "application/pdf" => Ascii("%PDF-1.7\n%âãÏÓ\n"),
        "application/rtf" => Ascii("{\\rtf1\\ansi\\deff0 Raport}"),
        "application/vnd.oasis.opendocument.text" => OpenDocument("application/vnd.oasis.opendocument.text"),
        "audio/mpeg" => [.. Ascii("ID3"), 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        "audio/flac" => [.. Ascii("fLaC"), 0x00, 0x00, 0x00, 0x22],
        "audio/ogg" => [.. Ascii("OggS"), 0x00, 0x02, 0x00, 0x00],
        "audio/wav" => [.. Ascii("RIFF"), 0x24, 0x08, 0x00, 0x00, .. Ascii("WAVEfmt ")],
        "audio/mp4" => [0x00, 0x00, 0x00, 0x20, .. Ascii("ftypM4A "), 0x00, 0x00, 0x00, 0x00],
        // Still images in the same container family as MP4 — what a phone camera writes by
        // default, and the case where "anything that is not audio is video" files a photograph
        // as a recording.
        "image/heic" => [0x00, 0x00, 0x00, 0x18, .. Ascii("ftypheic"), 0x00, 0x00, 0x00, 0x00, .. Ascii("mif1heic")],
        "image/heif" => [0x00, 0x00, 0x00, 0x18, .. Ascii("ftypmif1"), 0x00, 0x00, 0x00, 0x00, .. Ascii("mif1heic")],
        "image/avif" => [0x00, 0x00, 0x00, 0x1C, .. Ascii("ftypavif"), 0x00, 0x00, 0x00, 0x00, .. Ascii("avifmif1miaf")],
        "video/mp4" => [0x00, 0x00, 0x00, 0x20, .. Ascii("ftypisom"), 0x00, 0x00, 0x02, 0x00],
        "video/quicktime" => [0x00, 0x00, 0x00, 0x14, .. Ascii("ftypqt  "), 0x00, 0x00, 0x02, 0x00],
        "video/webm" => [0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00, .. Ascii("Bwebm")],
        "video/x-matroska" => [0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00, .. Ascii("Bmatroska")],
        "video/x-msvideo" => [.. Ascii("RIFF"), 0x24, 0x08, 0x00, 0x00, .. Ascii("AVI LIST")],
        "video/mpeg" => [0x00, 0x00, 0x01, 0xBA, 0x44, 0x00, 0x04, 0x00],
        "application/gzip" => [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00],
        "application/zip" => [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00],
        "application/x-ole-storage" => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00],
        _ => throw new ArgumentOutOfRangeException(nameof(mimeType), mimeType, "No sample for this format."),
    };

    /// <summary>
    /// An OpenDocument package's opening bytes: a ZIP local header whose first entry is the
    /// uncompressed <c>mimetype</c>, which is what the format specifies so the type is
    /// readable without unpacking.
    /// </summary>
    private static byte[] OpenDocument(string mediaType)
    {
        var buffer = new byte[128];
        byte[] localHeader = [0x50, 0x4B, 0x03, 0x04];
        localHeader.CopyTo(buffer, 0);
        buffer[8] = 0;                      // compression method: stored
        buffer[22] = (byte)mediaType.Length; // uncompressed size
        buffer[26] = 8;                      // file-name length: "mimetype"
        Ascii("mimetype").CopyTo(buffer, 30);
        Ascii(mediaType).CopyTo(buffer, 38);

        // What tripped the naive reader: the next local header starts immediately after the
        // value and its first bytes are letters, so the value's own declared length is the
        // only thing that ends it.
        localHeader.CopyTo(buffer, 38 + mediaType.Length);
        return buffer;
    }

    private static byte[] Ascii(string value) => Encoding.Latin1.GetBytes(value);
}
