// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Reading a club's spreadsheet of tracking reports onto a trip's log — previewed, then committed.
///
/// <para>
/// A sheet of reports is what a coordinator actually keeps while a trip is underground: a line per
/// phone call, a time, who was on it, how deep they were. Typing it in twice is how the second
/// copy ends up disagreeing with the first, so this reads the sheet itself.
/// </para>
/// <para>
/// <b>Every gate a typed report passes, an imported one passes too, through the same code.</b> The
/// watch must be writable, a claimed station must resolve against the survey the watch is on, a
/// depth means whichever station the cave declared it to be or else the nearest one, and only the
/// trip's own participants can be reported. Nothing here is a shortcut into the log — an import is
/// a faster way of saying the same things, not a way of saying more.
/// </para>
/// <para>
/// <b>The key is the person and the instant.</b> Two runs of the same sheet leave the same log, and
/// a corrected sheet re-imported changes the rows it corrected instead of doubling them. That is
/// the property the whole feature turns on, and it is why overwriting is asked for explicitly:
/// re-import is the intended use and also the one act that silently rewrites history.
/// </para>
/// </summary>
public static class TrackingCsvImportEndpoints
{
    /// <summary>
    /// A sample sheet, in the spellings the installation detects, with invented rows.
    /// </summary>
    /// <remarks>
    /// Offered for download because the fastest way to explain a column layout is a file somebody
    /// can open in the spreadsheet they already use. The rows are deliberately a whole short trip:
    /// two people going in together, splitting up, a place named rather than measured, a note, and
    /// both of them coming out — so the shape of an entry row and of an exit row is visible rather
    /// than described.
    /// </remarks>
    /// <remarks>
    /// Its first line is the one a log written out as a sheet begins with, taken from the same
    /// place, so the sample and an exported log cannot come to describe two layouts.
    /// </remarks>
    private static readonly string SampleCsv =
        TrackingCsvWriter.HeaderLine
        + "12.09.2026 08:15,0,,,\"Ion Popescu; Maria Pop\",Echipa 1,intrat in pestera,intrare\r\n"
        + "12.09.2026 09:40,96,,,\"Ion Popescu; Maria Pop\",Echipa 1,,\r\n"
        + "12.09.2026 10:05,,,Meandru,Maria Pop,Echipa 1,apa mare in meandru,\r\n"
        + "12.09.2026 10:30,150,,,Ion Popescu,Echipa 1,,\r\n"
        + "12.09.2026 11:15,,3.14,,Ion Popescu,Echipa 1,la baza puitului,\r\n"
        + "12.09.2026 14:00,96,,,\"Ion Popescu; Maria Pop\",Echipa 1,revenire pe traseu,\r\n"
        + "12.09.2026 16:45,0,,,\"Ion Popescu; Maria Pop\",Echipa 1,,iesire\r\n";

    public static RouteGroupBuilder MapTrackingCsvImportEndpoints(this RouteGroupBuilder api)
    {
        var import = api.MapGroup("/trip-logs/{tripLogId:guid}/tracking/csv-import")
            .WithTags("TripTracking");

        import.MapPost("/preview", PreviewAsync).WithValidation<TrackingCsvImportRequest>()
            .WithSummary("Reads a sheet of reports against this trip and answers what importing it would do — nothing is written.");
        import.MapPost("/commit", CommitAsync).WithValidation<TrackingCsvCommitRequest>()
            .WithSummary("Records the sheet's rows as reports, keyed on the person and the instant so a re-import corrects rather than doubles.");

        var fields = api.MapGroup("/tracking-csv-import").WithTags("TripTracking");
        fields.MapGet("/fields", GetFields)
            .WithSummary("The column roles a tracking sheet can carry, and the header spellings each one is detected under.");
        fields.MapGet("/template", GetTemplate)
            .WithSummary("A sample sheet with invented rows, in the spellings this installation detects.");

        api.MapGet("/trip-logs/{tripLogId:guid}/tracking/events/export", ExportAsync)
            .WithTags("TripTracking")
            .WithSummary("The whole log as a sheet the import reads back, oldest report first, with every place this caller may not be told left out.");

        return api;
    }

    private static Ok<IReadOnlyList<TrackingCsvFieldDto>> GetFields()
    {
        IReadOnlyList<TrackingCsvFieldDto> fields =
        [
            .. TrackingCsvColumnMapping.AllFields.Select(f =>
                new TrackingCsvFieldDto(f.ToString(), TrackingCsvColumnMapping.CandidatesFor(f)))
        ];
        return TypedResults.Ok(fields);
    }

    /// <summary>The sample sheet, as a file a spreadsheet will open.</summary>
    /// <remarks>
    /// <para>
    /// The byte-order mark is deliberate: without it a spreadsheet on Windows opens the file in the
    /// system code page and turns every Romanian diacritic into a different letter, which makes the
    /// sample look like a file this application mangled.
    /// </para>
    /// <para>
    /// <b>Named in a Content-Disposition</b>, because the only way a browser can fetch this route is
    /// with the caller's bearer token — which plain anchor navigation cannot carry — so the client
    /// fetches the bytes and saves them itself, under the name stated here. Without it the reviewer
    /// gets a file called <c>export</c> with no extension, which a spreadsheet will not open by
    /// being double-clicked.
    /// </para>
    /// </remarks>
    private static FileContentHttpResult GetTemplate() =>
        TypedResults.File(
            Encoding.UTF8.GetBytes("﻿" + SampleCsv),
            "text/csv; charset=utf-8",
            TemplateFileName);

    /// <summary>What the sample sheet is saved as. Named once, because a test asserts it.</summary>
    public const string TemplateFileName = "tracking-reports-sample.csv";

    // ---- the log as a sheet ---------------------------------------------------------------

    /// <summary>The trip's whole log, written as the sheet the import reads.</summary>
    /// <remarks>
    /// <para>
    /// <b>A file leaves the installation, so what goes into it is decided here and nowhere
    /// later.</b> It is gated by reading the trip, exactly as the log's list is, and each report
    /// passes through the same per-row rule the list answers by: a place this caller may not be
    /// told is not in the bytes. Such a row is still written — that somebody reported at that
    /// moment, and what they said, is the trip's — with its place marked as kept back, in a way
    /// the import refuses, so that the sheet brought back cannot write "nowhere" over the place
    /// the log holds.
    /// </para>
    /// <para>
    /// <b>People are named as this caller is shown them</b> on every other signed-in screen, by
    /// the one rule that decides it, and not by the roster's own entry: a file is not a way to
    /// read a name the screen does not show. The import finds a person by that name as well as by
    /// the roster's entry, so the sheet reads back whichever of the two it was written in.
    /// </para>
    /// <para>
    /// <b>A station or a depth is written as the report holds it, with nothing saying which survey
    /// it was made on.</b> It does not need to: the import leaves the place of a report alone
    /// where the row says of it what the report already says, so a report made on a survey the
    /// watch has since left comes back as itself and is not read again against another.
    /// </para>
    /// <para>
    /// <b>Named by the trip's id and the day it was taken</b>, never by the trip's title or its
    /// cave: a file's name is read by everything the file passes through.
    /// </para>
    /// <para>
    /// Oldest first, which is the order a coordinator's own sheet is kept in, with the same
    /// tie-break the list uses so that two reports stamped with one instant keep their order from
    /// one download to the next.
    /// </para>
    /// </remarks>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        Guid tripLogId, SilexGisDbContext db, IAccessService access, FeatureProtection protection,
        IAccessContextAccessor accessAccessor, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return ApiProblems.NotFound("trip_log.not_found");
        var trip = await TripTrackingEndpoints.ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null) return ApiProblems.NotFound("trip_log.not_found");

        // What is said about people, and nothing else. A row of the sheet is found again by its
        // person and its moment, and a note about the cave has no person: written out, it would
        // come back as a row naming nobody, which the reader refuses, so a log written out here
        // would no longer read back clean. It is left out, and the reader below never sees the
        // stored ones either — a sheet can neither replace nor duplicate a note about the cave.
        var events = await db.TripPositionEvents.AsNoTracking().AboutPeople()
            .Where(e => e.TripLogId == tripLogId)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);
        var openCaves = await TrackingWithholding.OpenCavesOfAsync(db, access, protection, ctx, events, ct);
        var names = await CaverDirectory.ResolveLabelsAsync(
            db, await userAccessor.GetAsync(ct), events.Select(e => e.Person()), ct);
        var teamIds = events.Where(e => e.TeamId is not null).Select(e => e.TeamId!.Value).Distinct().ToList();
        var teams = teamIds.Count == 0
            ? []
            : await db.TripTeams.AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Title, ct);

        var sheet = TrackingCsvWriter.Write(events.Select(e =>
        {
            // The place comes from the one rule that turns a stored report into what a caller
            // reads, not from the stored row: what it leaves null is what this file leaves out.
            var shown = TrackingWithholding.Shown(e, openCaves);
            return new TrackingCsvExportRow(
                shown.RecordedAt,
                names.GetValueOrDefault(e.Person()) ?? string.Empty,
                e.TeamId is { } team ? teams.GetValueOrDefault(team) : null,
                shown.Kind,
                shown.StationName,
                shown.DepthEnteredM,
                shown.Note,
                shown.ToStationName);
        }));

        return TypedResults.File(
            Encoding.UTF8.GetBytes("\uFEFF" + sheet),
            "text/csv; charset=utf-8",
            $"tracking-log-{tripLogId.ToString("N")[..8]}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ---- preview ------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingCsvPreviewDto>, ProblemHttpResult>> PreviewAsync(
        Guid tripLogId, TrackingCsvImportRequest request, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var loaded = await LoadAsync(
            tripLogId, forWriting: false, db, access, protection, accessAccessor, userAccessor, ct);
        if (loaded.Refusal is { } refusal) return refusal;

        if (Read(request.Options, out var options, out var mapping) is { } unreadable) return unreadable;
        var parsed = TrackingCsvParser.Parse(request.Text, options, mapping);
        var plan = TrackingCsvPlanner.Plan(parsed, loaded.Subject!);

        var before = await ShownBeforeAsync(plan, loaded, db, access, protection, ct);

        // The team a row is shown with is the team the report would have once imported. For a
        // row whose team cell fits more than one team that is the team the report has now — the
        // commit leaves it alone — and not the "none" the row itself could arrive at; a team is
        // the trip's and is never among what is withheld from somebody who may write its log.
        Guid? TeamAfter(TrackingCsvPlannedReport report) =>
            report is { KeepsStoredTeam: true, Replaces: true }
                ? before.GetValueOrDefault((report.CaverId, report.At))?.TeamId
                : report.TeamId;

        TrackingCsvPreviewRowDto Row(TrackingCsvPlannedReport report) =>
            new(report.Line, report.At, report.CaverId, report.CaverWritten, report.CaverMatched,
                report.MatchedBy.ToString(), TeamAfter(report), report.Kind, report.ViewerStationName,
                report.ViewerToStationName, report.PlaceLabel, report.DepthM, report.Note, report.Replaces,
                report.Replaces ? before.GetValueOrDefault((report.CaverId, report.At)) : null,
                [.. report.Diagnostics.Select(Diagnostic)]);

        return TypedResults.Ok(new TrackingCsvPreviewDto(
            parsed.Header,
            parsed.ResolvedColumns.ToDictionary(p => p.Key.ToString(), p => p.Value),
            parsed.UnmappedColumns,
            parsed.DateOrder,
            parsed.DateOrderSource.ToString(),
            parsed.Rows.Count,
            plan.Creates,
            plan.Replaces,
            plan.UnmatchedCavers,
            [.. plan.Reports.Select(Row)],
            [.. parsed.FileDiagnostics.Select(Diagnostic)],
            [.. plan.Refused.Select(Diagnostic)],
            ZoneAsAsked(request.Options?.TimeZone, options.Zone),
            parsed.NamedDay,
            plan.Digest(Bound(before))));
    }

    // ---- commit -------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingCsvCommitDto>, ProblemHttpResult>> CommitAsync(
        Guid tripLogId, TrackingCsvCommitRequest request, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var loaded = await LoadAsync(
            tripLogId, forWriting: true, db, access, protection, accessAccessor, userAccessor, ct);
        if (loaded.Refusal is { } refusal) return refusal;

        var user = await userAccessor.GetAsync(ct);
        if (user is null) return ApiProblems.NotFound("trip_log.not_found");

        if (Read(request.Options, out var options, out var mapping) is { } unreadable) return unreadable;
        var parsed = TrackingCsvParser.Parse(request.Text, options, mapping);
        var plan = TrackingCsvPlanner.Plan(parsed, loaded.Subject!);

        // A sheet that cannot be read at all is refused, not answered with nothing done. Its plan
        // is empty, so without this it would commit as a success that created no report and gave
        // no reason — a sheet of times of day sent without its day, most often. The preview says
        // the same thing as a finding about the file, because there it is something to look at;
        // here nothing can come of the request as it stands.
        if (!parsed.Readable)
        {
            var findings = string.Join(", ", parsed.FileDiagnostics
                .Where(d => d.Severity == TrackingCsvSeverity.Error)
                .Select(d => d.Problem.ToString())
                .Distinct());
            return ApiProblems.BadRequest(SheetUnreadableCode,
                $"The sheet cannot be read as it stands ({findings}); a preview of it says what to change.");
        }

        // The plan the caller was shown, or else nothing is written. Asked before anything is
        // changed and decided on the whole plan rather than on the lines chosen from it: a plan
        // that moved under the reviewer is one they have not read, whichever of its rows they
        // would have kept. What the preview said is in the log now is part of what was shown, so
        // it is worked out again here exactly as the preview worked it out — a report corrected
        // by a colleague in between is a report this reviewer has not read either.
        if (request.PlanDigest is { } shown
            && !string.Equals(
                shown,
                plan.Digest(Bound(await ShownBeforeAsync(plan, loaded, db, access, protection, ct))),
                StringComparison.Ordinal))
        {
            return ApiProblems.Conflict(PlanChangedCode,
                "The trip or its log has changed since this sheet was previewed, and importing it would no longer do what the preview showed. Preview it again.");
        }

        // Null is the caller saying nothing, so every importable row goes; an empty list is the
        // caller saying none, and it commits nothing. The two must not read alike: a screen
        // whose reviewer unticked every row and pressed the button anyway would otherwise
        // import the whole sheet, which is the one outcome a selection exists to prevent.
        var chosen = request.Lines?.ToHashSet();
        // The clock the plan was decided against, so a row the plan let through is not refused
        // here by a later reading of it.
        var now = loaded.Subject!.Now;
        var refused = new List<TrackingCsvDiagnostic>(plan.Refused);
        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var skipped = 0;

        // The log as the plan was decided against it: the same rows, read once. A key the log
        // holds once maps to that row; a key it holds several times maps to null, which the loop
        // below refuses on the row exactly as the planner did.
        var existing = loaded.Stored!;

        foreach (var report in plan.Reports)
        {
            if (chosen is not null && !chosen.Contains(report.Line))
            {
                skipped++;
                continue;
            }

            if (TripTrackingRules.MomentIsInFuture(report.At, now))
            {
                refused.Add(new TrackingCsvDiagnostic(
                    TrackingCsvSeverity.Error, TrackingCsvProblem.MomentInFuture,
                    report.Line, Detail: report.At.ToString("O")));
                continue;
            }

            if (existing.TryGetValue((report.CaverId, report.At), out var row))
            {
                if (row is null)
                {
                    refused.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Error, TrackingCsvProblem.AlreadyRecordedSeveralTimes,
                        report.Line, Detail: report.CaverWritten));
                    continue;
                }

                if (!request.ReplaceExisting)
                {
                    refused.Add(new TrackingCsvDiagnostic(
                        TrackingCsvSeverity.Warning, TrackingCsvProblem.AlreadyRecorded,
                        report.Line, Detail: report.CaverWritten));
                    skipped++;
                    continue;
                }

                // Corrected in place, not deleted and re-entered. A re-entered report is a new row
                // with a new identity, so anything hanging off the old one is orphaned by a fixed
                // typo — and the change is audited either way.
                //
                // Only what the sheet carries is written. The place and the kind are — a row is
                // a statement of where somebody was — unless the row says of the report exactly
                // what the report already says. Then the place is left as the log holds it, with
                // the survey and the cave it is anchored to: the report may have been made on a
                // survey the watch has since left, or under a datum and declared places that have
                // changed, and writing it again would read an untouched statement against all of
                // that and move it. The team and the note are written where the sheet has a
                // column for them, an empty cell included; a sheet with no such column says
                // nothing about them, and a note typed onto the report by hand is not something a
                // sheet of times and depths was asked to erase. Who recorded the report first is
                // left as it is: the audit names who changed it.
                if (!report.KeepsStoredPlace)
                {
                    row.Kind = report.Kind;
                    row.SurveyModelId = report.ViewerStationName is null ? null : plan.SurveyModelId;
                    row.CaveFeatureId = report.ViewerStationName is null ? null : plan.CaveFeatureId;
                    row.ViewerStationName = report.ViewerStationName;
                    // With the first station, always: the far end of a stretch belongs to the
                    // place the row states, and a row stating another place states no stretch
                    // unless it names one.
                    row.ViewerToStationName = report.ViewerToStationName;
                    row.DepthEnteredM = report.Kind == TripPositionEventKind.AtDepth ? report.DepthM : null;
                }

                // The team is written where the sheet has a column for it — unless the cell is
                // the name of more than one of the trip's teams. Which of them was meant cannot
                // be decided, and that is not the sheet saying "no team": the report keeps the
                // team it has, and the row carries the finding that says so.
                if (plan.CarriesTeam && !report.KeepsStoredTeam) row.TeamId = report.TeamId;

                // A note is written only where it reads differently. A cell is tidied as it is
                // read — a line break and a doubled space become one space, and a cell that only
                // ever said "nothing here" becomes no note — while a typed note is stored as it
                // was typed. So the stored note is read the way its own cell would be before the
                // two are compared, and a note nobody touched keeps its line breaks instead of
                // being rewritten as its tidied self and marked as corrected.
                if (plan.CarriesNote
                    && !string.Equals(
                        TripCsvValues.Single(row.Note, options.SkipTokens), report.Note, StringComparison.Ordinal))
                {
                    row.Note = report.Note;
                }

                // Counted as changed only where a stored value really is another one, asked of the
                // same comparison that decides whether the row is written at all — so the count is
                // the number of rows this request wrote over, and a sheet imported a second time
                // reports none and marks none as corrected.
                if (db.Entry(row).State == EntityState.Modified) updated++;
                else unchanged++;
                continue;
            }

            var fresh = new TripPositionEvent
            {
                TripLogId = tripLogId,
                CaverId = report.CaverId,
                TeamId = report.TeamId,
                Kind = report.Kind,
                SurveyModelId = report.ViewerStationName is null ? null : plan.SurveyModelId,
                CaveFeatureId = report.ViewerStationName is null ? null : plan.CaveFeatureId,
                ViewerStationName = report.ViewerStationName,
                ViewerToStationName = report.ViewerToStationName,
                DepthEnteredM = report.Kind == TripPositionEventKind.AtDepth ? report.DepthM : null,
                Note = report.Note,
                RecordedAt = report.At,
                RecordedByUserId = user.UserId,
            };
            db.TripPositionEvents.Add(fresh);
            // Kept in the same map the rows above are matched against, so two rows of one file
            // that are the same report land on one row of the log rather than on two.
            existing[(report.CaverId, report.At)] = fresh;
            created++;
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new TrackingCsvCommitDto(
            created, updated, unchanged, skipped, [.. refused.Select(Diagnostic)]));
    }

    // ---- what a row would replace -------------------------------------------------------

    /// <summary>
    /// What each row that would replace a report would replace, as this caller may be shown it,
    /// under the key a sheet upserts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stored report is a stored position, and these routes are reached on the right to write
    /// the trip — which somebody may hold without the right to be told where the cave is. So it
    /// is shown through the rule the log's own list answers by, evaluated against the cave each
    /// stored report was placed in rather than against the watch's present survey: nothing
    /// reaches a caller here that the list would withhold from them.
    /// </para>
    /// <para>
    /// <b>Worked out in one place for the preview and the commit.</b> The preview answers it and
    /// names the plan by it; the commit names its own plan by it and compares the two. Two ways
    /// of arriving at it would be two names for one plan, and every commit refused.
    /// </para>
    /// </remarks>
    private static async Task<Dictionary<(Guid CaverId, DateTimeOffset At), TrackingEventDto>> ShownBeforeAsync(
        TrackingCsvPlan plan, Loaded loaded, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, CancellationToken ct)
    {
        var stored = loaded.Stored!;
        var replaced = plan.Reports
            .Where(r => r.Replaces)
            .Select(r => stored.GetValueOrDefault((r.CaverId, r.At)))
            .OfType<TripPositionEvent>()
            .Distinct()
            .ToList();
        var openCaves = await TrackingWithholding.OpenCavesOfAsync(
            db, access, protection, loaded.Access, replaced, ct);
        return replaced.ToDictionary(
            e => (CaverId: e.Person(), At: e.RecordedAt),
            e => TrackingWithholding.Shown(e, openCaves));
    }

    /// <summary>
    /// What the caller was shown, as the plan's name takes it in: the answer they read and
    /// nothing the answer left out, so a withheld place is as absent from the name as from the
    /// screen.
    /// </summary>
    private static Dictionary<(Guid CaverId, DateTimeOffset At), TrackingCsvReplacedReport> Bound(
        Dictionary<(Guid CaverId, DateTimeOffset At), TrackingEventDto> before) =>
        before.ToDictionary(
            b => b.Key,
            b => new TrackingCsvReplacedReport(
                b.Value.Id, b.Value.TeamId, b.Value.Kind, b.Value.SurveyModelId, b.Value.StationName,
                b.Value.DepthEnteredM, b.Value.Note, b.Value.Corrected, b.Value.ToStationName));

    // ---- the trip a sheet is read against -----------------------------------------------

    /// <summary>What was loaded about the trip, or why the caller gets nothing.</summary>
    /// <param name="Stored">
    /// The trip's log under the key a sheet upserts on, from the one read of it this request makes:
    /// the report itself where the log holds one under the key, null where it holds several.
    /// </param>
    /// <param name="Access">Who is asking, for what a stored report may show them.</param>
    private sealed record Loaded(
        TrackingCsvSubject? Subject,
        Domain.Entities.TripTracking? Tracking,
        Dictionary<(Guid CaverId, DateTimeOffset At), TripPositionEvent?>? Stored,
        AccessContext? Access,
        ProblemHttpResult? Refusal)
    {
        internal static Loaded Refused(ProblemHttpResult refusal) =>
            new(null, null, null, null, refusal);
    }

    /// <summary>
    /// The trip, its roster, its teams, its survey and its cave's declared places.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Guarded exactly as recording a report is: the same write guard, and the same question about
    /// whether this account may place anybody on the watch's model. A preview names stations, so it
    /// tells the caller as much as recording would and must not be reachable on weaker rights.
    /// </para>
    /// <para>
    /// <b>A watch with no usable model is loaded rather than refused.</b> Entry and exit rows claim
    /// no station, so a sheet of them imports into a watch whose survey has gone missing; the rows
    /// that do claim a place are then refused one by one, with the reason on the row.
    /// </para>
    /// <para>
    /// <b>Every refusal a sheet can meet before it is read is decided here, for the preview and
    /// the commit alike</b> — the log that cannot be written among them. A preview that answered
    /// where the commit refuses would have a reviewer read a sheet, tick its rows and only then
    /// learn that none of it can be written; decided in one place, the two cannot come to differ.
    /// </para>
    /// <para>
    /// <b>The log is read once</b>, and both what the planner is told the log holds and the rows
    /// the commit writes over come from that reading. Two readings are two logs: a report typed
    /// between them would be a create to the plan and an overwrite to the write.
    /// </para>
    /// </remarks>
    /// <param name="forWriting">
    /// Whether the stored reports are loaded to be changed. A preview changes nothing and reads
    /// them untracked.
    /// </param>
    private static async Task<Loaded> LoadAsync(
        Guid tripLogId, bool forWriting, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null
            ? null
            : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return Loaded.Refused(refusal);

        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking is null)
        {
            return Loaded.Refused(
                ApiProblems.Conflict(TrackingProblemCodes.NotConfigured, "This trip has no watch to import reports onto."));
        }

        if (!TripTrackingRules.MayWriteLog(tracking.State))
        {
            return Loaded.Refused(ApiProblems.Conflict(TrackingProblemCodes.NotWritable,
                "A report lands on a watch that is armed or has been closed — arm the watch first."));
        }

        // Distinct by person, not by roster row: the roster is one row per person per job, so the
        // leader who also proposed the trip is two rows and one person. Handed in twice they would
        // be two hits under one key, and the ladder's rule that several hits is an ambiguity would
        // refuse the one person a sheet is most likely to name.
        var roster = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId)
            .Join(db.Cavers.AsNoTracking(), p => p.CaverId, c => c.Id, (_, c) => new { c.Id, c.FullName })
            .Distinct()
            .ToListAsync(ct);

        // Each person under the roster's own entry and, where it is another, under the name this
        // caller is shown for them everywhere else — which is the name a log written out as a
        // sheet carries. An account's name can change after the roster entry was made, and the
        // entry can be renamed by whoever keeps the roster, so the two drift; a sheet may have
        // been written in either, and both mean the one person. Nothing is disclosed by matching
        // the second: it is the name this caller already reads on the trip.
        var labels = await CaverDirectory.ResolveLabelsAsync(
            db, await userAccessor.GetAsync(ct), roster.Select(r => r.Id), ct);
        List<(Guid Key, string? Name)> names = [];
        foreach (var person in roster)
        {
            names.Add((person.Id, person.FullName));
            if (labels.TryGetValue(person.Id, out var label)
                && !string.Equals(label, person.FullName, StringComparison.Ordinal))
            {
                names.Add((person.Id, label));
            }
        }

        var teams = await db.TripTeams.AsNoTracking()
            .Where(t => t.TripLogId == tripLogId)
            .Select(t => new { t.Id, Name = t.Title })
            .ToListAsync(ct);

        var usable = await TripTrackingEndpoints.UsableModelAsync(
            db, access, protection, ctx!, tracking.SurveyModelId, ct);

        IReadOnlyList<TrackingDepthResolver.Station> stations = [];
        IReadOnlyList<DeclaredDepthPlaces.Declared> declarations = [];
        if (usable is { } model)
        {
            stations = await TripTrackingEndpoints.StationsOfAsync(db, model.Model, ct);
            declarations = await TripTrackingEndpoints.DeclaredPlacesOfAsync(db, model.Cave.Id, ct);
        }

        // Loaded once for the whole file rather than looked up per row: a trip's log is thousands
        // of rows at most, and a query per report is what turns importing a season into a minute.
        //
        // Grouped rather than keyed directly, because nothing makes the person and the instant
        // unique in the log: a typed "entered" and a typed note filed at the same minute for one
        // person are two rows under one key, and a dictionary built straight off them would throw
        // and fail the whole sheet with nothing naming the rows. A key held once is one a sheet
        // corrects; a key held more than once maps to null and is one the planner refuses,
        // because which of the rows the sheet means is not the importer's to guess.
        //
        // The reports about people only. A sheet's row is about a person at a moment, so a note
        // about the cave can be neither the row it corrects nor a second holder of its key; it is
        // not loaded, and whatever the sheet does it is still on the log afterwards as it was.
        var log = forWriting ? db.TripPositionEvents : db.TripPositionEvents.AsNoTracking();
        var stored = (await log.AboutPeople().Where(e => e.TripLogId == tripLogId).ToListAsync(ct))
            .GroupBy(e => (CaverId: e.Person(), At: e.RecordedAt))
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First() : null);

        // Where each report held once says the person was, for the rows that only repeat it —
        // as far as this caller may be told. A report whose place is withheld from them is left
        // out, so a station or a depth tried against it is planned as any other row is and the
        // answer does not depend on whether it was the right one.
        var placedRows = stored.Values.OfType<TripPositionEvent>().Where(TrackingWithholding.HasPosition).ToList();
        var openCaves = await TrackingWithholding.OpenCavesOfAsync(db, access, protection, ctx, placedRows, ct);
        var storedPlaces = placedRows
            .Where(e => TrackingWithholding.PositionOpen(e, openCaves))
            .ToDictionary(
                e => (CaverId: e.Person(), At: e.RecordedAt),
                e => new TrackingCsvStoredPlace(e.Kind, e.ViewerStationName, e.DepthEnteredM, e.ViewerToStationName));

        var subject = new TrackingCsvSubject
        {
            Roster = names,
            Teams = [.. teams.Select(t => (t.Id, t.Name))],
            Declarations = declarations,
            Stations = stations,
            HasModel = usable is not null,
            SurveyModelId = usable?.Model.Id,
            CaveFeatureId = usable?.Cave.Id,
            Format = usable?.Model.Format ?? default,
            RootSurveyName = usable?.Model.RootSurveyName,
            ReferenceStationName = tracking.ReferenceStationName,
            DepthFilter = tracking.DepthFilter,
            Existing = stored.Keys.ToHashSet(),
            ExistingSeveralTimes = stored.Where(e => e.Value is null).Select(e => e.Key).ToHashSet(),
            Stored = storedPlaces,
            Now = DateTimeOffset.UtcNow,
        };

        return new Loaded(subject, tracking, stored, ctx, null);
    }

    // ---- request and answer shapes ------------------------------------------------------

    /// <summary>What a sheet is refused with when the zone it names is not one this server can read it in.</summary>
    public const string ZoneUnknownCode = "tracking_csv.zone_unknown";

    /// <summary>What a commit is refused with when the sheet has no readable rows at all, for a reason about the file.</summary>
    public const string SheetUnreadableCode = "tracking_csv.sheet_unreadable";

    /// <summary>What a commit is refused with when it would no longer write what its preview showed.</summary>
    public const string PlanChangedCode = "tracking_csv.plan_changed";

    /// <summary>
    /// The caller's choices as the reader wants them, or the refusal where one of them cannot be
    /// honoured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field the caller did not name is left out of the mapping rather than given an empty
    /// header: the parser treats a named field as "look for exactly this and say so if it is
    /// missing", so naming a field with nothing would report every column as absent.
    /// </para>
    /// <para>
    /// <b>A zone this server does not know refuses the whole request, here, for the preview and
    /// the commit alike.</b> It cannot be dropped the way a malformed delimiter is: reading the
    /// sheet without it moves every time by the zone's offset, so a commit that fell back would
    /// write instants its preview never showed. And it cannot be a finding on a row, because it is
    /// not about any row. An empty name is the caller saying nothing, as with every other choice
    /// here.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The zone a sheet was read in, under the name the caller chose it by.
    /// </summary>
    /// <remarks>
    /// The caller's spelling and not the zone's own, because the two differ for a place the zone
    /// database has respelled, and the screen that sent "Europe/Kiev" formats times and words its
    /// summary with the name it gets back: a browser old enough to offer only the old spelling may
    /// not know the new one. UTC is the exception only in its letter case.
    /// </remarks>
    private static string? ZoneAsAsked(string? asked, TimeZoneInfo? zone) =>
        zone is null ? null
        : ReferenceEquals(zone, TimeZoneInfo.Utc) || asked is null ? zone.Id
        : asked;

    private static ProblemHttpResult? Read(
        TrackingCsvImportOptionsDto? dto,
        out TrackingCsvOptions options,
        out TrackingCsvColumnMapping mapping)
    {
        options = TrackingCsvOptions.Default;
        mapping = TrackingCsvColumnMapping.Auto;
        if (dto is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dto.TimeZone))
        {
            if (!TrackingCsvZones.TryFind(dto.TimeZone, out var zone))
            {
                return ApiProblems.BadRequest(ZoneUnknownCode,
                    "The time zone must be an IANA zone name this server knows, such as 'Europe/Bucharest' or 'UTC'.");
            }

            options = options with { Zone = zone };
        }

        if (dto.Day is { } day)
        {
            options = options with { Day = day };
        }

        if (dto.Delimiter is { Length: 1 } delimiter)
        {
            options = options with { Delimiter = delimiter[0] };
        }

        if (dto.MultiValueSeparators is { Length: > 0 } separators)
        {
            options = options with { MultiValueSeparators = [.. separators.Distinct()] };
        }

        if (dto.DateOrder is { } order)
        {
            options = options with { DateOrder = order };
        }

        // Named words replace the shipped list rather than adding to it, and each side is replaced
        // on its own: a club that writes "down" for going in may still write "iesire" for coming
        // out, and a caller naming one list has said nothing about the other.
        if (dto.WentInWords is { Count: > 0 } || dto.CameOutWords is { Count: > 0 }
            || dto.NotedWords is { Count: > 0 })
        {
            var shipped = TrackingCsvStateWords.Default;
            options = options with
            {
                StateWords = new TrackingCsvStateWords
                {
                    WentIn = dto.WentInWords is { Count: > 0 } wentIn ? wentIn : shipped.WentIn,
                    CameOut = dto.CameOutWords is { Count: > 0 } cameOut ? cameOut : shipped.CameOut,
                    Noted = dto.NotedWords is { Count: > 0 } noted ? noted : shipped.Noted,
                },
            };
        }

        foreach (var (name, header) in dto.Columns ?? new Dictionary<string, string>())
        {
            if (RouteEnums.TryParse<TrackingCsvField>(name, out var field)
                && !string.IsNullOrWhiteSpace(header))
            {
                mapping = mapping.With(field, header);
            }
        }

        return null;
    }

    private static TrackingCsvDiagnosticDto Diagnostic(TrackingCsvDiagnostic d) =>
        new(d.Severity.ToString(), d.Problem.ToString(), d.Line, d.Column, d.Detail);
}
