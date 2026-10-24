// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which bytes a reader asking for a file itself is handed. Each test pairs the outcome it is
/// about with the neighbouring one over the same file, so a rule that answered one value for
/// everything could not pass.
/// </summary>
public class OriginalReachTests
{
    [Fact]
    public void A_reader_who_may_place_a_photo_gets_the_stored_file_while_the_switch_is_on()
    {
        Reach(mayCarryLocation: true, disclosable: true, keep: true, removable: true)
            .ShouldBe(OriginalReach.Stored);

        // The same photo to a reader who may not place it: the whole picture, less its location.
        Reach(mayCarryLocation: true, disclosable: false, keep: true, removable: true)
            .ShouldBe(OriginalReach.LocationFree);
    }

    [Fact]
    public void With_the_switch_off_even_a_reader_who_may_place_the_photo_gets_the_location_free_copy()
    {
        Reach(mayCarryLocation: true, disclosable: true, keep: false, removable: true)
            .ShouldBe(OriginalReach.LocationFree);
        Reach(mayCarryLocation: true, disclosable: true, keep: true, removable: true)
            .ShouldBe(OriginalReach.Stored);
    }

    [Fact]
    public void A_file_whose_location_cannot_be_removed_keeps_renderings_only_for_a_reader_who_may_not_have_it()
    {
        // A video, or an image this application cannot write back.
        Reach(mayCarryLocation: true, disclosable: false, keep: true, removable: false)
            .ShouldBe(OriginalReach.None);
        Reach(mayCarryLocation: true, disclosable: true, keep: false, removable: false)
            .ShouldBe(OriginalReach.None);

        // The reader who may place it, with the switch on, still gets the stored bytes.
        Reach(mayCarryLocation: true, disclosable: true, keep: true, removable: false)
            .ShouldBe(OriginalReach.Stored);
    }

    [Fact]
    public void A_photo_whose_record_holds_no_position_is_still_treated_as_carrying_one()
    {
        // Clearing a photo's recorded position never rewrites it, and a fix that sits only in an
        // XMP packet is never read out: the record is not evidence about the bytes.
        var unplacedPhoto = PhotoPositionProtection.MayCarryLocation(FileKind.Image, recordsPosition: false);
        unplacedPhoto.ShouldBeTrue();
        Reach(unplacedPhoto, disclosable: false, keep: true, removable: true)
            .ShouldBe(OriginalReach.LocationFree);
        Reach(unplacedPhoto, disclosable: true, keep: true, removable: true)
            .ShouldBe(OriginalReach.Stored);
    }

    [Fact]
    public void A_video_with_no_recorded_position_has_no_original_for_a_reader_who_may_not_place_it()
    {
        // Nothing reads the position a phone writes into a video, so none is ever recorded.
        var video = PhotoPositionProtection.MayCarryLocation(FileKind.Video, recordsPosition: false);
        video.ShouldBeTrue();
        Reach(video, disclosable: false, keep: true, removable: false)
            .ShouldBe(OriginalReach.None);
        Reach(video, disclosable: true, keep: true, removable: false)
            .ShouldBe(OriginalReach.Stored);
        PhotoPositionProtection.MayCarryLocation(FileKind.Audio, recordsPosition: false).ShouldBeTrue();
    }

    [Fact]
    public void A_document_that_cannot_carry_a_capture_location_is_handed_over_as_stored()
    {
        var report = PhotoPositionProtection.MayCarryLocation(FileKind.Document, recordsPosition: false);
        report.ShouldBeFalse();
        Reach(report, disclosable: false, keep: true, removable: false)
            .ShouldBe(OriginalReach.Stored);
        Reach(report, disclosable: false, keep: false, removable: false)
            .ShouldBe(OriginalReach.Stored);

        // A position set on one by hand brings it under the rule like any photograph.
        var placedReport = PhotoPositionProtection.MayCarryLocation(FileKind.Document, recordsPosition: true);
        placedReport.ShouldBeTrue();
        Reach(placedReport, disclosable: false, keep: true, removable: false)
            .ShouldBe(OriginalReach.None);
    }

    private static OriginalReach Reach(bool mayCarryLocation, bool disclosable, bool keep, bool removable) =>
        PhotoPositionProtection.OriginalReachOf(mayCarryLocation, disclosable, keep, removable);
}
