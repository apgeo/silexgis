// SPDX-License-Identifier: AGPL-3.0-or-later
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
    /// The caller decides who is in it. Narrowed to the trip's own participants, a sheet cannot
    /// name somebody who was not there; widened to the instance, it can — which is a decision
    /// about what an import is allowed to assert, not a matching detail, so it is made outside.
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
}

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

    public decimal? DepthM { get; init; }

    public string? Note { get; init; }

    /// <summary>Whether a report with this key already exists, so importing would change it.</summary>
    public bool Replaces { get; init; }

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

    public int Creates => Reports.Count(r => !r.Replaces);

    public int Replaces => Reports.Count(r => r.Replaces);
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
    public static TrackingCsvPlan Plan(
        IReadOnlyList<TrackingCsvRow> rows,
        TrackingCsvSubject subject)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(subject);

        var stored = subject.Stations.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var reports = new List<TrackingCsvPlannedReport>();
        var refused = new List<TrackingCsvDiagnostic>();
        var unmatched = new List<string>();
        var unmatchedSeen = new HashSet<string>(StringComparer.Ordinal);

        // What this file has already claimed, so two rows about one person at one instant are
        // reported rather than silently written one over the other. The last one wins, which is
        // the same rule the upsert against the log follows, so a sheet and a re-import of it
        // behave the same way.
        var claimed = new Dictionary<(Guid, DateTimeOffset), int>();

        foreach (var row in rows)
        {
            if (!row.Importable)
            {
                refused.AddRange(row.Diagnostics.Where(d => d.Severity == TrackingCsvSeverity.Error));
                continue;
            }

            var rowNotes = row.Diagnostics.Where(d => d.Severity == TrackingCsvSeverity.Warning).ToList();

            var (teamId, teamProblem) = MatchTeam(row.Team, subject.Teams);
            if (teamProblem is { } teamTrouble)
            {
                rowNotes.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Warning, teamTrouble, row.Line, Detail: row.Team));
            }

            var placed = Place(row, subject, stored);
            if (placed.Problem is { } placeProblem)
            {
                refused.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, placeProblem, row.Line,
                    Detail: row.StationName ?? row.PlaceLabel ?? row.DepthM?.ToString()));
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
                var notes = rowNotes;
                if (claimed.TryGetValue(key, out var earlier))
                {
                    notes = [.. rowNotes, new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Warning, TrackingCsvProblem.DuplicateInFile,
                        row.Line, Detail: earlier.ToString())];
                }

                claimed[key] = row.Line;

                reports.Add(new TrackingCsvPlannedReport
                {
                    Line = row.Line,
                    At = row.At!.Value,
                    CaverId = hit.Key,
                    CaverWritten = written,
                    CaverMatched = hit.Name,
                    MatchedBy = hit.By,
                    TeamId = teamId,
                    Kind = row.Kind!.Value,
                    ViewerStationName = placed.ViewerStationName,
                    DepthM = placed.DepthM,
                    Note = row.Note,
                    Replaces = subject.Existing.Contains(key),
                    Diagnostics = notes,
                });
            }
        }

        return new TrackingCsvPlan
        {
            Reports = reports,
            Refused = refused,
            UnmatchedCavers = unmatched,
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

    /// <summary>Where a row puts somebody, under the fixed order station, place, depth.</summary>
    private static (string? ViewerStationName, decimal? DepthM, TrackingCsvProblem? Problem) Place(
        TrackingCsvRow row, TrackingCsvSubject subject, HashSet<string> stored)
    {
        // Going in and coming out claim no station, so nothing is resolved for them — which is
        // also what keeps a sheet's entry and exit rows importable into a watch whose model has
        // gone missing.
        if (row.Kind is TripPositionEventKind.Entered or TripPositionEventKind.Exited)
        {
            return (null, null, null);
        }

        if (!subject.HasModel)
        {
            return (null, null, TrackingCsvProblem.ModelMissing);
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
                    ? (null, null, TrackingCsvProblem.StationNotInModel)
                    : (viewer, null, null);
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
                        : TrackingCsvProblem.PlaceLabelUnknown);
                }

                if (!subject.Stations.Any(s => s.ViewerName == declared.Value.ViewerStationName))
                {
                    return (null, null, TrackingCsvProblem.StationNotInModel);
                }

                // The declared depth is carried through beside the station, because the place the
                // club named is a depth as well as a station and a reader shown only the station
                // learns less than the sheet said.
                return (declared.Value.ViewerStationName, declared.Value.DepthM, null);
            }

            case TrackingCsvPlaceKind.Depth:
            {
                var placement = TrackingDepthPlacements.For(
                    subject.Declarations, subject.Stations, subject.ReferenceStationName,
                    subject.DepthFilter, row.DepthM!.Value);

                return placement.Outcome switch
                {
                    TrackingDepthPlacementOutcome.ReferenceUnknown =>
                        (null, null, TrackingCsvProblem.DepthReferenceUnknown),
                    TrackingDepthPlacementOutcome.NoStationAtDepth =>
                        (null, null, TrackingCsvProblem.NoStationAtDepth),
                    _ => (placement.ViewerStationName, row.DepthM, null),
                };
            }

            default:
                return (null, null, TrackingCsvProblem.NoPlaceAndNoState);
        }
    }
}
