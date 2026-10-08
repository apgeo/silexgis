// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TrackingCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The findings a sheet importer can report, recorded as a set.
/// </summary>
/// <remarks>
/// <para>
/// A finding reaches a reviewer as its bare name in a response, and the screen that shows it looks
/// the name up to word it. So adding a value to <see cref="TrackingCsvProblem"/> and stopping there
/// does not break anything visible — it shows somebody <c>PlaceLabelAmbiguous</c> on the one screen
/// whose entire job is telling them what to go and fix, and nothing else ever says so.
/// </para>
/// <para>
/// This is the set-equality test that makes that impossible: a new finding fails here, and the
/// failure says what else has to happen. The mirror of this list is
/// <c>client/src/components/trips/trackingCsvProblems.ts</c>, whose own test asserts both locales
/// word exactly the names on it. Two tests rather than one because neither project can read the
/// other's source, and a list nobody checks is worse than no list.
/// </para>
/// </remarks>
public class TrackingCsvProblemSurfaceTests
{
    /// <summary>
    /// Every finding this importer can report, as of the last time somebody worded them.
    /// </summary>
    /// <remarks>
    /// Kept in the order the enum declares them so a diff against it reads straightforwardly.
    /// Adding a name here is a statement that the client's list and both locale files have it too.
    /// </remarks>
    private static readonly string[] Worded =
    [
        nameof(TrackingCsvProblem.UnterminatedQuote),
        nameof(TrackingCsvProblem.NoHeader),
        nameof(TrackingCsvProblem.TooManyColumns),
        nameof(TrackingCsvProblem.NamedColumnMissing),
        nameof(TrackingCsvProblem.UnmappedColumn),
        nameof(TrackingCsvProblem.MomentColumnMissing),
        nameof(TrackingCsvProblem.CaverColumnMissing),
        nameof(TrackingCsvProblem.TimeColumnNeedsADay),
        nameof(TrackingCsvProblem.MomentUnreadable),
        nameof(TrackingCsvProblem.MomentWithoutTime),
        nameof(TrackingCsvProblem.MomentWithoutDate),
        nameof(TrackingCsvProblem.MomentSkippedByClockChange),
        nameof(TrackingCsvProblem.MomentRepeatedByClockChange),
        nameof(TrackingCsvProblem.NoCavers),
        nameof(TrackingCsvProblem.NoPlaceAndNoState),
        nameof(TrackingCsvProblem.DepthUnreadable),
        nameof(TrackingCsvProblem.StateWordUnknown),
        nameof(TrackingCsvProblem.NoteTooLong),
        nameof(TrackingCsvProblem.MomentMissing),
        nameof(TrackingCsvProblem.RaggedRow),
        nameof(TrackingCsvProblem.DepthOutOfRange),
        nameof(TrackingCsvProblem.DateOrderConflict),
        nameof(TrackingCsvProblem.StateOverridesPlace),
        nameof(TrackingCsvProblem.CaverNotOnRoster),
        nameof(TrackingCsvProblem.CaverAmbiguous),
        nameof(TrackingCsvProblem.TeamNotOnTrip),
        nameof(TrackingCsvProblem.TeamAmbiguous),
        nameof(TrackingCsvProblem.PlaceLabelUnknown),
        nameof(TrackingCsvProblem.PlaceLabelAmbiguous),
        nameof(TrackingCsvProblem.StationNotInModel),
        nameof(TrackingCsvProblem.ToStationNotInModel),
        nameof(TrackingCsvProblem.StretchSameStation),
        nameof(TrackingCsvProblem.ToStationWithoutStation),
        nameof(TrackingCsvProblem.DepthReferenceUnknown),
        nameof(TrackingCsvProblem.NoStationAtDepth),
        nameof(TrackingCsvProblem.ModelMissing),
        nameof(TrackingCsvProblem.DuplicateInFile),
        nameof(TrackingCsvProblem.MomentInFuture),
        nameof(TrackingCsvProblem.ClockRunsBackwards),
        nameof(TrackingCsvProblem.AlreadyRecorded),
        nameof(TrackingCsvProblem.AlreadyRecordedSeveralTimes),
    ];

    [Fact]
    public void Every_finding_a_sheet_can_report_has_been_worded_for_the_reviewer()
    {
        var declared = Enum.GetNames<TrackingCsvProblem>().OrderBy(n => n, StringComparer.Ordinal);
        var worded = Worded.OrderBy(n => n, StringComparer.Ordinal);

        // Set equality, both directions. A finding missing from the list is one a reviewer would be
        // shown untranslated; a name on the list that no longer exists is wording kept for a
        // finding nothing can report, which sits in two locale files unread.
        declared.ShouldBe(
            worded,
            customMessage:
                "A tracking-sheet finding was added or removed. Word it in "
                + "client/src/i18n/locales/{en,ro}.json under trips.tracking.csvImport.problemNames "
                + "and list it in client/src/components/trips/trackingCsvProblems.ts, then add it "
                + "here.");
    }

    [Fact]
    public void Every_column_role_a_sheet_can_carry_is_one_the_mapping_screen_offers()
    {
        // The same reasoning for the other list the server publishes by name: a role with no
        // wording is a chooser labelled with an identifier.
        Enum.GetNames<TrackingCsvField>().OrderBy(n => n, StringComparer.Ordinal).ShouldBe(
            new[]
            {
                nameof(TrackingCsvField.Cavers),
                nameof(TrackingCsvField.Date),
                nameof(TrackingCsvField.Depth),
                nameof(TrackingCsvField.Details),
                nameof(TrackingCsvField.Note),
                nameof(TrackingCsvField.Place),
                nameof(TrackingCsvField.RecordedAt),
                nameof(TrackingCsvField.State),
                nameof(TrackingCsvField.Station),
                nameof(TrackingCsvField.Team),
                nameof(TrackingCsvField.Time),
                nameof(TrackingCsvField.ToStation),
            }.OrderBy(n => n, StringComparer.Ordinal),
            customMessage:
                "A column role was added or removed. Name it in "
                + "client/src/i18n/locales/{en,ro}.json under trips.tracking.csvImport.fields, "
                + "give it header spellings in TrackingCsvColumnMapping.CandidatesFor, then add it "
                + "here.");
    }

    [Fact]
    public void Every_column_role_has_at_least_one_header_spelling_it_is_detected_under()
    {
        // A role with no candidates can only ever be mapped by hand, which on a screen that says
        // "detected" beside every other column reads as a bug in the detection.
        foreach (var field in TrackingCsvColumnMapping.AllFields)
        {
            TrackingCsvColumnMapping.CandidatesFor(field).ShouldNotBeEmpty($"{field} has no spellings");
        }
    }
}
