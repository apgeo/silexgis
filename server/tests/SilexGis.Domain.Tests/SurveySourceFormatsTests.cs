// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which raw survey sources may be archived, and the rule that a file has to contain what its
/// name says it does.
/// </summary>
public sealed class SurveySourceFormatsTests
{
    /// <summary>A Therion source file, as text.</summary>
    private static byte[] TherionText() => Encoding.UTF8.GetBytes(
        "survey main\n  centreline\n    data normal from to length compass clino\n"
        + "    1 2 12.5 145 -3\n  endcentreline\nendsurvey\n");

    /// <summary>The first bytes of a Linux executable — a real binary header, not random noise.</summary>
    private static byte[] Executable() =>
        [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0x3E, 0];

    /// <summary>The first bytes of a ZIP container.</summary>
    private static byte[] ZipArchive() =>
        [(byte)'P', (byte)'K', 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00];

    [Theory]
    [InlineData("cave.th", SurveySourceKind.TherionSource)]
    [InlineData("plan.th2", SurveySourceKind.TherionSource)]
    [InlineData("project.thconfig", SurveySourceKind.TherionConfig)]
    [InlineData("cave.svx", SurveySourceKind.SurvexSource)]
    [InlineData("therion.log", SurveySourceKind.TherionLog)]
    [InlineData("topodroid-export.zip", SurveySourceKind.TopoDroidArchive)]
    public void Each_accepted_name_claims_one_kind(string fileName, SurveySourceKind expected) =>
        SurveySourceFormats.ClaimedKind(fileName).ShouldBe(expected);

    [Fact]
    public void The_claim_is_read_from_the_name_regardless_of_case_or_path()
    {
        SurveySourceFormats.ClaimedKind("CAVE.SVX").ShouldBe(SurveySourceKind.SurvexSource);
        SurveySourceFormats.ClaimedKind("surveys/2024/cave.Th").ShouldBe(SurveySourceKind.TherionSource);
    }

    [Theory]
    [InlineData("model.lox")]
    [InlineData("model.3d")]
    [InlineData("walls.stl")]
    [InlineData("notes")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_this_archive_does_not_accept_claims_nothing(string? fileName) =>
        SurveySourceFormats.ClaimedKind(fileName).ShouldBeNull();

    [Fact]
    public void A_source_file_that_is_text_is_what_its_name_says()
    {
        // The positive half of the check, in the same test as the negative one below: a rule that
        // refused everything would pass a refusal test on its own.
        SurveySourceFormats.ContentMatches(SurveySourceKind.SurvexSource, TherionText()).ShouldBeTrue();
        SurveySourceFormats.ContentMatches(SurveySourceKind.TherionSource, TherionText()).ShouldBeTrue();
        SurveySourceFormats.ContentMatches(SurveySourceKind.TherionLog, TherionText()).ShouldBeTrue();
        SurveySourceFormats.ContentMatches(SurveySourceKind.TopoDroidArchive, ZipArchive()).ShouldBeTrue();
    }

    [Fact]
    public void An_executable_renamed_to_a_source_extension_is_not_one()
    {
        SurveySourceFormats.ContentMatches(SurveySourceKind.SurvexSource, Executable()).ShouldBeFalse();
        SurveySourceFormats.ContentMatches(SurveySourceKind.TherionSource, Executable()).ShouldBeFalse();
        SurveySourceFormats.ContentMatches(SurveySourceKind.TherionLog, Executable()).ShouldBeFalse();
    }

    [Fact]
    public void A_recognised_binary_format_is_never_accepted_as_a_text_source()
    {
        // Not only executables: anything the shared signature table already claims is that format,
        // whatever the upload chose to call it.
        SurveySourceFormats.ContentMatches(SurveySourceKind.TherionSource, ZipArchive()).ShouldBeFalse();
        SurveySourceFormats.ContentMatches(SurveySourceKind.SurvexSource, [0x25, 0x50, 0x44, 0x46, 0x2D])
            .ShouldBeFalse();
    }

    [Fact]
    public void Text_is_not_an_archive()
    {
        SurveySourceFormats.ContentMatches(SurveySourceKind.TopoDroidArchive, TherionText()).ShouldBeFalse();
        SurveySourceFormats.ContentMatches(SurveySourceKind.TopoDroidArchive, Executable()).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_file_claims_nothing_and_matches_nothing()
    {
        foreach (var kind in Enum.GetValues<SurveySourceKind>())
        {
            SurveySourceFormats.ContentMatches(kind, []).ShouldBeFalse();
        }
    }

    [Fact]
    public void No_archived_source_is_recorded_as_a_format_that_gets_read_for_words()
    {
        // The archive stores and does not parse — the compiler's log above all. Recording a source
        // under a text media type would enrol it in text extraction, which opens the file and reads
        // it, so the media types chosen here are what keeps that promise.
        foreach (var kind in Enum.GetValues<SurveySourceKind>())
        {
            TextExtractionFormats.CarriesText(SurveySourceFormats.MediaTypeFor(kind))
                .ShouldBeFalse($"{kind} would be queued for text extraction");
        }
    }

    [Fact]
    public void Every_accepted_extension_maps_to_a_kind()
    {
        SurveySourceFormats.AcceptedExtensions.ShouldNotBeEmpty();
        foreach (var extension in SurveySourceFormats.AcceptedExtensions)
        {
            extension.ShouldStartWith(".");
            extension.ShouldBe(extension.ToLowerInvariant());
            SurveySourceFormats.ClaimedKind($"file{extension}").ShouldNotBeNull();
        }
    }
}
