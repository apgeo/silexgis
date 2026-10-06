// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.Trips;
using Xunit;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The mark a generated write-up carries at the front of its name — the only thing that tells it
/// from a report a club wrote and uploaded into the same slot.
/// </summary>
/// <remarks>
/// Two things read the mark and both delete on what it recognises: regenerating a write-up takes
/// the earlier generated one out of the slot, and deleting the record takes the generated one
/// with it. So what is pinned here is what the mark must never recognise — a hand-written report,
/// and the other kind's write-up — beside what it must.
///
/// Nothing here says two records get different marks, and nothing should: the mark carries the
/// leading characters of an identifier that is ordered by time, so records made within the same
/// minute share them. That is harmless, because the mark is only ever read among the files pinned
/// to one record, and it is the reason no test below asserts otherwise.
/// </remarks>
public class GeneratedReportNamingTests
{
    private static readonly Guid Record = Guid.Parse("01a10f36-84da-7b22-be13-52c4ba6c4301");

    [Fact]
    public void A_generated_write_up_is_recognised_by_its_own_kinds_mark()
    {
        var tripWriteUp = $"{TripReportNaming.GeneratedPrefix(Record)}20261006.docx";
        var campWriteUp = $"{ExpeditionReportNaming.GeneratedPrefix(Record)}20261006.docx";

        tripWriteUp.ShouldBe("trip-report-01a10f36-20261006.docx");
        campWriteUp.ShouldBe("expedition-report-01a10f36-20261006.docx");

        tripWriteUp.ShouldStartWith(TripReportNaming.GeneratedPrefix(Record));
        campWriteUp.ShouldStartWith(ExpeditionReportNaming.GeneratedPrefix(Record));
    }

    /// <summary>
    /// A camp gathers trips, and a trip belongs to a camp — the two kinds of write-up sit a pin
    /// apart. Neither mark may recognise the other's file, even for one and the same identifier.
    /// </summary>
    [Fact]
    public void Neither_kinds_mark_recognises_the_others_write_up()
    {
        var tripWriteUp = $"{TripReportNaming.GeneratedPrefix(Record)}20261006.docx";
        var campWriteUp = $"{ExpeditionReportNaming.GeneratedPrefix(Record)}20261006.docx";

        tripWriteUp.StartsWith(ExpeditionReportNaming.GeneratedPrefix(Record), StringComparison.Ordinal)
            .ShouldBeFalse();
        campWriteUp.StartsWith(TripReportNaming.GeneratedPrefix(Record), StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("club-report.txt")]
    [InlineData("report.docx")]
    [InlineData("Trip report 2026.docx")]
    [InlineData("expedition report.pdf")]
    [InlineData("my-trip-report-01a10f36-20261006.docx")]
    [InlineData("")]
    public void A_report_somebody_named_themselves_is_not_recognised(string uploaded)
    {
        uploaded.StartsWith(TripReportNaming.GeneratedPrefix(Record), StringComparison.Ordinal).ShouldBeFalse();
        uploaded.StartsWith(ExpeditionReportNaming.GeneratedPrefix(Record), StringComparison.Ordinal).ShouldBeFalse();
    }

    /// <summary>
    /// An empty mark would recognise every file there is, and what the mark recognises is
    /// deleted. Asked of an identifier with nothing in it too, which is the value a mark would be
    /// built from if a caller ever passed a record it had not loaded.
    /// </summary>
    [Fact]
    public void A_mark_is_never_empty()
    {
        foreach (var id in new[] { Record, Guid.Empty })
        {
            TripReportNaming.GeneratedPrefix(id).ShouldNotBeNullOrWhiteSpace();
            ExpeditionReportNaming.GeneratedPrefix(id).ShouldNotBeNullOrWhiteSpace();
            TripReportNaming.GeneratedPrefix(id).ShouldEndWith("-");
            ExpeditionReportNaming.GeneratedPrefix(id).ShouldEndWith("-");
        }
    }
}
