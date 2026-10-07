// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// A short name for everything a plan would write, so that a commit can say which plan it means.
/// </summary>
/// <remarks>
/// <para>
/// A sheet is read twice: once to be looked at and once to be written. Between the two the trip can
/// change under it — a team renamed, somebody added to the roster, a report typed by a colleague, a
/// place declared in the cave — and the second reading then plans something the reviewer never saw.
/// The preview hands this name out, the commit is handed it back, and a commit whose own plan has
/// another name is refused instead of writing it. Nothing is kept between the two requests: the
/// name is recomputed from the sheet and the trip each time, which is the whole of the binding.
/// </para>
/// <para>
/// <b>The list of what goes into it is the guarantee.</b> Every value that reaches a written row is
/// in it — the line, the instant, the person, the team, the kind, the station, the depth, the note
/// and whether a report is already there to be replaced — together with the two facts about the
/// sheet that decide what a replacement leaves standing, and the survey and the cave a placed row
/// is anchored to. A value that changed a written row and was not in here would be a change no
/// commit is refused for; a value in here that is only shown (how a name was matched, the place as
/// the sheet spelled it, the remarks on a row) would refuse commits that would have written exactly
/// what was previewed. The anchor is left out of a plan that places nobody for the same reason:
/// such a plan writes no anchor, so another survey under it is still the plan that was shown.
/// </para>
/// <para>
/// <b>What a replaced report holds now goes in as this caller was shown it, and no further.</b> A
/// preview says what is in the log under each row that would replace something, and a reviewer
/// agrees to the pair. Somebody correcting that report in between leaves the plan's own values as
/// they were, so without this the commit would write over a report the reviewer never read. But a
/// stored report is a stored position, and this name is handed to anybody who may write the trip —
/// which somebody may without being allowed to learn where the cave is. A caller who knows every
/// other input (it is their own sheet) could try station names against the name until one fitted,
/// so a withheld value hashed in here would be a withheld value given away. What is handed in is
/// therefore the report after the withholding the log's own list applies for that caller: exactly
/// what the preview answered them, in which a withheld place is already absent.
/// </para>
/// </remarks>
public static class TrackingCsvPlanDigest
{
    /// <summary>
    /// Said first, so that a change to how the rest is written down gives every plan a new name
    /// rather than, by accident, an old plan's.
    /// </summary>
    private const string Shape = "tracking-sheet-plan/2";

    /// <summary>The name, as 64 lower-case hexadecimal digits.</summary>
    /// <param name="plan">What the sheet would write.</param>
    /// <param name="before">
    /// What the caller is shown of each report a row would replace, under the key a sheet upserts
    /// on. A row that replaces a report this does not hold is named as replacing something unseen.
    /// </param>
    public static string Of(
        TrackingCsvPlan plan,
        IReadOnlyDictionary<(Guid CaverId, DateTimeOffset At), TrackingCsvReplacedReport>? before = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Every value is written with its length in front of it, and an absent one as a mark no
        // value can be mistaken for. Without that, a note ending where the next report's line
        // begins could be moved across the boundary and still spell the same text.
        var text = new StringBuilder();
        Put(text, Shape);
        Put(text, plan.CarriesTeam ? "1" : "0");
        Put(text, plan.CarriesNote ? "1" : "0");
        // The anchor reaches only rows that claim a station, so a plan with none does not name it.
        var places = plan.Reports.Any(r => r.ViewerStationName is not null);
        Put(text, places ? plan.SurveyModelId?.ToString("N") : null);
        Put(text, places ? plan.CaveFeatureId?.ToString("N") : null);
        Put(text, plan.Reports.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var report in plan.Reports)
        {
            Put(text, report.Line.ToString(CultureInfo.InvariantCulture));
            // The instant, not how it was written: 10:00+02:00 and 08:00Z are one row of the log.
            Put(text, report.At.UtcTicks.ToString(CultureInfo.InvariantCulture));
            Put(text, report.CaverId.ToString("N"));
            Put(text, report.TeamId?.ToString("N"));
            Put(text, ((int)report.Kind).ToString(CultureInfo.InvariantCulture));
            Put(text, report.ViewerStationName);
            Put(text, Depth(report.DepthM));
            Put(text, report.Note);
            Put(text, report.Replaces ? "1" : "0");

            if (!report.Replaces) continue;
            if (before is null || !before.TryGetValue((report.CaverId, report.At), out var stood))
            {
                Put(text, null);
                continue;
            }

            Put(text, stood.Id.ToString("N"));
            Put(text, stood.TeamId?.ToString("N"));
            Put(text, ((int)stood.Kind).ToString(CultureInfo.InvariantCulture));
            Put(text, stood.SurveyModelId?.ToString("N"));
            Put(text, stood.StationName);
            Put(text, Depth(stood.DepthM));
            Put(text, stood.Note);
            Put(text, stood.Corrected ? "1" : "0");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Trailing zeros dropped, because 96 and 96.0 are one depth and are stored as one.</summary>
    private static string? Depth(decimal? metres) =>
        metres?.ToString("0.############################", CultureInfo.InvariantCulture);

    private static void Put(StringBuilder text, string? value)
    {
        if (value is null)
        {
            text.Append("-;");
            return;
        }

        text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
    }
}

/// <summary>
/// A report a row would replace, as far as the caller asking was shown it.
/// </summary>
/// <remarks>
/// Not the stored row: the station, the depth and the survey are null here for a caller the place
/// is withheld from, exactly as they were in the answer that caller read.
/// </remarks>
public sealed record TrackingCsvReplacedReport(
    Guid Id,
    Guid? TeamId,
    TripPositionEventKind Kind,
    Guid? SurveyModelId,
    string? StationName,
    decimal? DepthM,
    string? Note,
    bool Corrected);
