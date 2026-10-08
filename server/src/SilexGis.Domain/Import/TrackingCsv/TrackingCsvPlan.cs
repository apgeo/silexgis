// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// Everything about the trip a sheet is being imported into that deciding its rows needs.
/// </summary>
/// <remarks>
/// Handed in rather than looked up, so the whole of this reasoning can be exercised without a
/// database — and so the one caller that has a database loads each of these once for the file
/// instead of once per row.
/// </remarks>
public sealed record TrackingCsvSubject
{
    /// <summary>Who a written name may be matched against, as (id, full name).</summary>
    /// <remarks>
    /// <para>
    /// The caller decides who is in it. Narrowed to the trip's own participants, a sheet cannot
    /// name somebody who was not there; widened to the instance, it can — which is a decision
    /// about what an import is allowed to assert, not a matching detail, so it is made outside.
    /// </para>
    /// <para>
    /// A person may be in it under more than one name, with one id: the roster's own entry and
    /// the name the person's account goes by are two spellings of one person, and a sheet may
    /// have been written in either.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(Guid Key, string? Name)> Roster { get; init; } = [];

    /// <summary>The trip's own teams, as (id, name).</summary>
    public IReadOnlyList<(Guid Id, string Name)> Teams { get; init; } = [];

    /// <summary>What the cave has declared its depths and places to be.</summary>
    public IReadOnlyCollection<DeclaredDepthPlaces.Declared> Declarations { get; init; } = [];

    /// <summary>The stations of the survey the watch is on, under both of their names.</summary>
    public IReadOnlyCollection<TrackingDepthResolver.Station> Stations { get; init; } = [];

    /// <summary>Whether the watch has a survey model at all.</summary>
    public bool HasModel { get; init; }

    /// <summary>
    /// The survey a placed row is anchored to, and the cave it draws: the watch's own, as far as
    /// the caller may place anybody on it, and null otherwise.
    /// </summary>
    /// <remarks>
    /// Here because a station name alone does not say where somebody was: two surveys can both
    /// have a station "2", and a watch can be moved to another survey between a sheet being read
    /// and being written. What the rows would be anchored to is part of what the plan would write.
    /// </remarks>
    public Guid? SurveyModelId { get; init; }

    /// <inheritdoc cref="SurveyModelId"/>
    public Guid? CaveFeatureId { get; init; }

    public SurveyModelFormat Format { get; init; }

    public string? RootSurveyName { get; init; }

    /// <summary>The trip's depth datum, in whichever spelling it was stored.</summary>
    public string? ReferenceStationName { get; init; }

    public IReadOnlyCollection<string> DepthFilter { get; init; } = [];

    /// <summary>
    /// The reports the log already holds, as the key an import upserts on.
    /// </summary>
    /// <remarks>
    /// Given so a preview can say which rows would change something and which would add
    /// something, which is the difference a reviewer is actually deciding about.
    /// </remarks>
    public IReadOnlySet<(Guid CaverId, DateTimeOffset At)> Existing { get; init; } =
        new HashSet<(Guid, DateTimeOffset)>();

    /// <summary>
    /// The keys under which the log holds <em>more than one</em> report.
    /// </summary>
    /// <remarks>
    /// Nothing stops a log from holding two reports about one person at one instant: a typed
    /// "entered" and a typed note both filed at 08:15 is an ordinary write-up. A sheet's row under
    /// that key is one report, and which of the two it corrects is not the importer's to guess —
    /// so such rows are refused, and this set is how the planner knows which they are.
    /// </remarks>
    public IReadOnlySet<(Guid CaverId, DateTimeOffset At)> ExistingSeveralTimes { get; init; } =
        new HashSet<(Guid, DateTimeOffset)>();

    /// <summary>
    /// What each report the log holds once says about where the person was, as far as the reader
    /// of the sheet may be told it, under the key an import upserts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Given so that a row which says of a stored report exactly what that report already says is
    /// planned as leaving its place alone. A report is anchored to the survey it was made on, and
    /// a station name or a depth read again later is read against whatever survey, datum and
    /// declared places the watch has by then: resolving an unchanged statement a second time would
    /// move a report nobody touched onto another survey, or refuse it for a station that survey
    /// does not have.
    /// </para>
    /// <para>
    /// A report whose place the caller may not be told is left out of this, and so is planned as
    /// any other row is. Otherwise a station or a depth tried against the key would be answered
    /// differently when it was the right one.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<(Guid CaverId, DateTimeOffset At), TrackingCsvStoredPlace> Stored { get; init; } =
        new Dictionary<(Guid, DateTimeOffset), TrackingCsvStoredPlace>();

    /// <summary>
    /// The clock a row's moment is measured against, so a report about the future is refused here
    /// and shown in the preview rather than discovered at the write.
    /// </summary>
    /// <remarks>
    /// Handed in rather than read, for the reason everything else here is: the preview and the
    /// commit have to decide every row the same way, and two readings of the clock are two
    /// decisions.
    /// </remarks>
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Where a report the log already holds says somebody was.</summary>
/// <param name="Kind">What the stored report is.</param>
/// <param name="ViewerStationName">The station it is anchored to, as the log keeps it.</param>
/// <param name="DepthM">The depth it was given as, for a depth report.</param>
public sealed record TrackingCsvStoredPlace(
    TripPositionEventKind Kind,
    string? ViewerStationName,
    decimal? DepthM);

/// <summary>One report a sheet's row turned into, ready to be written.</summary>
public sealed record TrackingCsvPlannedReport
{
    public required int Line { get; init; }

    public required DateTimeOffset At { get; init; }

    public required Guid CaverId { get; init; }

    /// <summary>The name as the sheet wrote it, kept so a reviewer can see what matched what.</summary>
    public required string CaverWritten { get; init; }

    /// <summary>The name on the roster it matched.</summary>
    public required string CaverMatched { get; init; }

    public required CaverNameLadder.Rung MatchedBy { get; init; }

    public Guid? TeamId { get; init; }

    public required TripPositionEventKind Kind { get; init; }

    public string? ViewerStationName { get; init; }

    /// <summary>
    /// The place as the sheet named it, where the station came from a name the cave declared.
    /// </summary>
    /// <remarks>
    /// Kept beside the station it became because the two are in different vocabularies: the sheet
    /// says what the club calls the place and the log keeps a survey station, and a reviewer shown
    /// only the station cannot tell whether the name they wrote was understood. Null wherever the
    /// station was not arrived at through a declared name — a row that named a station outright,
    /// one that gave a depth, and one that claims no place at all.
    /// </remarks>
    public string? PlaceLabel { get; init; }

    public decimal? DepthM { get; init; }

    public string? Note { get; init; }

    /// <summary>Whether a report with this key already exists, so importing would change it.</summary>
    public bool Replaces { get; init; }

    /// <summary>
    /// Whether the row says where the person was in the words the stored report already says it,
    /// so that the report's kind, station, depth and the survey it is anchored to are left as the
    /// log holds them.
    /// </summary>
    /// <remarks>
    /// The station and the depth above are then the stored report's own, not a second resolution
    /// of them: only the team and the note of such a row can change anything.
    /// </remarks>
    public bool KeepsStoredPlace { get; init; }

    /// <summary>Things worth saying about this report that did not stop it.</summary>
    public IReadOnlyList<TrackingCsvDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>What a whole sheet would do to a trip's log, decided but not yet done.</summary>
public sealed record TrackingCsvPlan
{
    public IReadOnlyList<TrackingCsvPlannedReport> Reports { get; init; } = [];

    /// <summary>Why each row, or each name on a row, could not be imported.</summary>
    public IReadOnlyList<TrackingCsvDiagnostic> Refused { get; init; } = [];

    /// <summary>Names nobody answered to, once, however many rows wrote them.</summary>
    /// <remarks>
    /// Collected apart from the per-row refusals because it is the one list a reviewer acts on:
    /// a sheet naming somebody who is not on the trip is usually a missing participant rather
    /// than a thousand broken rows, and one name shown once says that, while a refusal per row
    /// buries it.
    /// </remarks>
    public IReadOnlyList<string> UnmatchedCavers { get; init; } = [];

    /// <summary>Whether the sheet has a column for the team.</summary>
    /// <remarks>
    /// What a sheet does not carry it says nothing about, and that matters once, where a row
    /// replaces a report the log already holds: a sheet with no team column leaves that report's
    /// team standing, where a sheet that has the column and left the cell empty says the report
    /// has none. A fact about the sheet rather than about a row, because an empty cell under a
    /// column and no column at all read alike on the row.
    /// </remarks>
    public bool CarriesTeam { get; init; }

    /// <summary>Whether the sheet has a column for the note, or for the details that are folded into it.</summary>
    public bool CarriesNote { get; init; }

    /// <summary>The survey every row that claims a station would be anchored to.</summary>
    public Guid? SurveyModelId { get; init; }

    /// <summary>The cave every row that claims a station would be anchored to.</summary>
    public Guid? CaveFeatureId { get; init; }

    public int Creates => Reports.Count(r => !r.Replaces);

    public int Replaces => Reports.Count(r => r.Replaces);

    /// <summary>The name a commit gives back to say that this is the plan it was shown.</summary>
    /// <param name="before">What the caller is shown of each report a row would replace.</param>
    public string Digest(
        IReadOnlyDictionary<(Guid CaverId, DateTimeOffset At), TrackingCsvReplacedReport>? before = null) =>
        TrackingCsvPlanDigest.Of(this, before);
}

/// <summary>
/// Turning read rows into reports on one trip: who each name is, which station each place is, and
/// which of them the log already holds.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the parser because these are questions about a trip, not about a file, and
/// separate from the endpoint because none of them needs a database once the trip has been loaded.
/// Both halves are wanted twice — a preview and a commit have to agree about every row, and the
/// only way to be sure they do is for them to be the same reasoning run twice.
/// </para>
/// <para>
/// <b>What a row becomes.</b> One row is one moment for one or more people, so it fans out into
/// one report per person; the place, the note and the team are the same on each. A name that
/// matches nobody costs that person's report and not the row — the others on it still import,
/// because a party of four with one unknown name is three reports and a question, not nothing.
/// </para>
/// </remarks>
public static class TrackingCsvPlanner
{
    /// <remarks>
    /// Handed the whole reading rather than its rows, because what a plan may write depends on
    /// which columns the sheet has as well as on what its rows say.
    /// </remarks>
    public static TrackingCsvPlan Plan(
        TrackingCsvParseResult parsed,
        TrackingCsvSubject subject)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(subject);

        var rows = parsed.Rows;

        var stored = subject.Stations.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var reports = new List<TrackingCsvPlannedReport>();
        var refused = new List<TrackingCsvDiagnostic>();
        var unmatched = new List<string>();
        var unmatchedSeen = new HashSet<string>(StringComparer.Ordinal);

        // What this file has already claimed, so two rows about one person at one instant are
        // one report and not two. The last one wins and the earlier one is dropped from the plan,
        // which is the rule the upsert against the log follows for a re-import — and it is decided
        // here, in the plan, so that the commit never meets a key the same file just wrote: a plan
        // that counted both as creates would preview two reports and write one, refusing the
        // other as already recorded by a row the log never held. Both rows are told about it.
        var claimed = new Dictionary<(Guid, DateTimeOffset), TrackingCsvPlannedReport>();

        // The latest instant so far among the rows whose day the importer named, and the line it
        // is on. A row further down that falls before it is, in a sheet kept in order, a row
        // written after midnight and filed on the day before. Measured against the latest instant
        // rather than against the row just above, so that every row after the midnight is told,
        // not only the first: 23:50, 00:30, 01:10 is two rows on the wrong day.
        //
        // One clock for the sheet and not one per person, because the midnight is the sheet's: a
        // second party that goes in at 00:30 has no earlier row of its own to be measured against,
        // and its first row is exactly as much on the wrong day as anybody else's. The price is a
        // sheet typed person by person, each from the morning again, where rows are told that
        // crossed no midnight; that is a warning to read past, against a report filed a day early
        // with nothing said.
        (DateTimeOffset At, int Line)? latestOnNamedDay = null;

        foreach (var row in rows)
        {
            if (!row.Importable)
            {
                refused.AddRange(row.Diagnostics.Where(d => d.Severity == TrackingCsvSeverity.Error));
                continue;
            }

            // Refused here and not only at the write, so the preview shows it: a sheet with a
            // mistyped year should say so before anything is written, not after everything else
            // has been.
            if (TripTrackingRules.MomentIsInFuture(row.At!.Value, subject.Now))
            {
                refused.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentInFuture,
                    row.Line, Detail: row.At.Value.ToString("O")));
                continue;
            }

            var rowNotes = row.Diagnostics.Where(d => d.Severity == TrackingCsvSeverity.Warning).ToList();

            if (row.OnNamedDay)
            {
                if (latestOnNamedDay is { } latest && row.At.Value < latest.At)
                {
                    // Imported where the importer said the sheet was, and told: moving the row to
                    // the next day would be right for a sheet that crossed midnight and wrong for
                    // one typed out of order, and the two cannot be told apart here.
                    rowNotes.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Warning, TrackingCsvProblem.ClockRunsBackwards,
                        row.Line, Detail: latest.Line.ToString(CultureInfo.InvariantCulture)));
                }
                else
                {
                    latestOnNamedDay = (row.At.Value, row.Line);
                }
            }

            var (teamId, teamProblem) = MatchTeam(row.Team, subject.Teams);
            if (teamProblem is { } teamTrouble)
            {
                rowNotes.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Warning, teamTrouble, row.Line, Detail: row.Team));
            }

            var placed = Place(row, subject, stored);

            // A place that cannot be resolved costs the row, once, before anybody on it is looked
            // for — unless the row only repeats, for somebody on it, what the log already holds
            // for them at that moment. Such a statement is not resolved at all, so it cannot fail
            // to resolve: the people it holds for keep their reports, and the refusal is said once
            // for whoever else the row names.
            var placeRefused = false;
            void RefusePlace(TrackingCsvProblem problem)
            {
                if (placeRefused) return;
                placeRefused = true;
                refused.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, problem, row.Line,
                    Detail: row.StationName ?? row.PlaceLabel ?? row.DepthM?.ToString()));
            }

            if (placed.Problem is { } unplaced
                && !row.Cavers.Any(name =>
                    CaverNameLadder.Match(name, subject.Roster) is [var only]
                    && StoodAs(row, subject, (only.Key, row.At!.Value)) is not null))
            {
                RefusePlace(unplaced);
                continue;
            }

            foreach (var written in row.Cavers)
            {
                var hits = CaverNameLadder.Match(written, subject.Roster);
                if (hits.Count == 0)
                {
                    refused.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Error, TrackingCsvProblem.CaverNotOnRoster,
                        row.Line, Detail: written));
                    if (unmatchedSeen.Add(written))
                    {
                        unmatched.Add(written);
                    }

                    continue;
                }

                if (hits.Count > 1)
                {
                    refused.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Error, TrackingCsvProblem.CaverAmbiguous,
                        row.Line, Detail: $"{written}: {string.Join(", ", hits.Select(h => h.Name))}"));
                    continue;
                }

                var hit = hits[0];
                var key = (hit.Key, row.At!.Value);
                if (subject.ExistingSeveralTimes.Contains(key))
                {
                    // Refused rather than resolved onto one of them, for the same reason a name
                    // two people answer to is: choosing would quietly rewrite a report the sheet
                    // may not have meant. Refused in the preview as well as the commit, so the
                    // reviewer learns it before writing anything.
                    refused.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Error, TrackingCsvProblem.AlreadyRecordedSeveralTimes,
                        row.Line, Detail: written));
                    continue;
                }

                // What the log holds under this key, where the row says the same of it. Asked
                // before the place the row resolved to is used, because it replaces that place.
                var stood = StoodAs(row, subject, key);
                if (stood is null && placed.Problem is { } placeProblem)
                {
                    RefusePlace(placeProblem);
                    continue;
                }

                var notes = rowNotes;
                if (claimed.TryGetValue(key, out var earlier))
                {
                    notes = [.. rowNotes, new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Warning, TrackingCsvProblem.DuplicateInFile,
                        row.Line, Detail: earlier.Line.ToString())];
                    refused.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Warning, TrackingCsvProblem.DuplicateInFile,
                        earlier.Line, Detail: row.Line.ToString()));
                    reports.Remove(earlier);
                }

                var report = new TrackingCsvPlannedReport
                {
                    Line = row.Line,
                    At = row.At!.Value,
                    CaverId = hit.Key,
                    CaverWritten = written,
                    CaverMatched = hit.Name,
                    MatchedBy = hit.By,
                    TeamId = teamId,
                    Kind = row.Kind!.Value,
                    ViewerStationName = stood is null ? placed.ViewerStationName : stood.ViewerStationName,
                    PlaceLabel = stood is null ? placed.PlaceLabel : null,
                    DepthM = stood is null ? placed.DepthM : stood.DepthM,
                    Note = row.Note,
                    Replaces = subject.Existing.Contains(key),
                    KeepsStoredPlace = stood is not null,
                    Diagnostics = notes,
                };
                claimed[key] = report;
                reports.Add(report);
            }
        }

        return new TrackingCsvPlan
        {
            Reports = reports,
            Refused = refused,
            UnmatchedCavers = unmatched,
            SurveyModelId = subject.SurveyModelId,
            CaveFeatureId = subject.CaveFeatureId,
            CarriesTeam = parsed.ResolvedColumns.ContainsKey(TrackingCsvField.Team),
            CarriesNote = parsed.ResolvedColumns.ContainsKey(TrackingCsvField.Note)
                || parsed.ResolvedColumns.ContainsKey(TrackingCsvField.Details),
        };
    }

    /// <summary>
    /// Which of this trip's teams a written name is, or why it is none of them.
    /// </summary>
    /// <remarks>
    /// A team nobody recognises is a warning and not a refusal, because a team is indicative: the
    /// report belongs to the person on it, and losing the grouping beside them costs a label on a
    /// map, while refusing the row costs the position itself.
    /// </remarks>
    private static (Guid? TeamId, TrackingCsvProblem? Problem) MatchTeam(
        string? written, IReadOnlyList<(Guid Id, string Name)> teams)
    {
        if (written is null)
        {
            return (null, null);
        }

        var asked = FoldedText.Of(written).Value;
        var found = teams.Where(t => FoldedText.Of(t.Name).Value == asked).ToList();
        return found.Count == 1
            ? (found[0].Id, null)
            : (null, TrackingCsvProblem.TeamNotOnTrip);
    }

    /// <summary>
    /// The place the log already holds under a key, where the row says exactly that of it; null
    /// where the log holds nothing the row can be compared with, or the row says something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compared in the words a log written out as a sheet uses, which are the stored report's own:
    /// the station under the name the log keeps, or the depth as a number. A station is compared
    /// as text and not resolved, because resolving is done against the survey the watch is on now
    /// and the stored report may have been made on another — where the row names the station by
    /// another of its spellings it is simply resolved as any row is, and on the survey the report
    /// was made on that arrives at the same station.
    /// </para>
    /// <para>
    /// Only a row that names a station outright or gives a depth can say the same as a stored
    /// report. One that names a place the cave declared is always resolved: the name is not kept
    /// on a report, so there is nothing stored for it to be the same as. Going in, coming out and
    /// a note claim no place, and are written as they always were.
    /// </para>
    /// </remarks>
    private static TrackingCsvStoredPlace? StoodAs(
        TrackingCsvRow row, TrackingCsvSubject subject, (Guid CaverId, DateTimeOffset At) key)
    {
        if (!subject.Stored.TryGetValue(key, out var stood) || row.Kind != stood.Kind)
        {
            return null;
        }

        var same = (row.Kind, row.Decides) switch
        {
            (TripPositionEventKind.AtStation, TrackingCsvPlaceKind.Station) =>
                stood.ViewerStationName is not null
                && string.Equals(
                    row.StationName, TripCsv.TripCsvValues.Tidy(stood.ViewerStationName), StringComparison.Ordinal),
            (TripPositionEventKind.AtDepth, TrackingCsvPlaceKind.Depth) =>
                stood.DepthM is not null && row.DepthM == stood.DepthM,
            _ => false,
        };
        return same ? stood : null;
    }

    /// <summary>Where a row puts somebody, under the fixed order station, place, depth.</summary>
    /// <remarks>
    /// The place label comes back only on the path that used it, so a row that named a place and
    /// also said "out" — where the standing wins and no station is claimed — is not shown as a
    /// place that resolved to nothing.
    /// </remarks>
    private static (string? ViewerStationName, decimal? DepthM, TrackingCsvProblem? Problem, string? PlaceLabel) Place(
        TrackingCsvRow row, TrackingCsvSubject subject, HashSet<string> stored)
    {
        // Going in, coming out and a note claim no station, so nothing is resolved for them —
        // which is also what keeps those rows of a sheet importable into a watch whose model has
        // gone missing: what somebody said on the telephone does not depend on a survey.
        if (row.Kind is TripPositionEventKind.Entered or TripPositionEventKind.Exited
            or TripPositionEventKind.Note)
        {
            return (null, null, null, null);
        }

        if (!subject.HasModel)
        {
            return (null, null, TrackingCsvProblem.ModelMissing, null);
        }

        switch (row.Decides)
        {
            case TrackingCsvPlaceKind.Station:
            {
                // Resolved against the model rather than compared as strings: the two sides of
                // this application spell one station of a Therion model differently, so a name
                // copied off the viewer is a real station a string comparison calls unknown.
                var viewer = SurveyStationNames.ViewerNameOfMatch(
                    subject.Format, subject.RootSurveyName, row.StationName!, stored.Contains);
                return viewer is null
                    ? (null, null, TrackingCsvProblem.StationNotInModel, null)
                    : (viewer, null, null, null);
            }

            case TrackingCsvPlaceKind.Place:
            {
                var declared = DeclaredDepthPlaces.ByLabel(subject.Declarations, row.PlaceLabel);
                if (declared is null)
                {
                    // Told apart from "no such name" on purpose: two declarations sharing a label
                    // is something to go and fix in the cave, and a misspelled label in the sheet
                    // is something to go and fix in the sheet.
                    var sharing = subject.Declarations.Count(d =>
                        d.PlaceLabel is not null
                        && FoldedText.Of(d.PlaceLabel).Value == FoldedText.Of(row.PlaceLabel!).Value);
                    return (null, null, sharing > 1
                        ? TrackingCsvProblem.PlaceLabelAmbiguous
                        : TrackingCsvProblem.PlaceLabelUnknown, null);
                }

                if (!subject.Stations.Any(s => s.ViewerName == declared.Value.ViewerStationName))
                {
                    return (null, null, TrackingCsvProblem.StationNotInModel, null);
                }

                // The declared depth is carried through beside the station, because the place the
                // club named is a depth as well as a station and a reader shown only the station
                // learns less than the sheet said.
                return (declared.Value.ViewerStationName, declared.Value.DepthM, null, row.PlaceLabel);
            }

            case TrackingCsvPlaceKind.Depth:
            {
                var placement = TrackingDepthPlacements.For(
                    subject.Declarations, subject.Stations, subject.ReferenceStationName,
                    subject.DepthFilter, row.DepthM!.Value);

                return placement.Outcome switch
                {
                    TrackingDepthPlacementOutcome.ReferenceUnknown =>
                        (null, null, TrackingCsvProblem.DepthReferenceUnknown, null),
                    TrackingDepthPlacementOutcome.NoStationAtDepth =>
                        (null, null, TrackingCsvProblem.NoStationAtDepth, null),
                    _ => (placement.ViewerStationName, row.DepthM, null, null),
                };
            }

            default:
                return (null, null, TrackingCsvProblem.NoPlaceAndNoState, null);
        }
    }
}
