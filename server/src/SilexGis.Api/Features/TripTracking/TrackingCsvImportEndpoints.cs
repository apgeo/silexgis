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
    private const string SampleCsv =
        "Data si ora,Adancime,Statie,Loc,Speologi,Echipa,Nota,Stare\r\n"
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

    // ---- preview ------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingCsvPreviewDto>, ProblemHttpResult>> PreviewAsync(
        Guid tripLogId, TrackingCsvImportRequest request, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var loaded = await LoadAsync(tripLogId, db, access, protection, accessAccessor, ct);
        if (loaded.Refusal is { } refusal) return refusal;

        var (options, mapping) = Read(request.Options);
        var parsed = TrackingCsvParser.Parse(request.Text, options, mapping);
        var plan = TrackingCsvPlanner.Plan(parsed.Rows, loaded.Subject!);

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
            [.. plan.Refused.Select(Diagnostic)]));
    }

    // ---- commit -------------------------------------------------------------------------

    private static async Task<Results<Ok<TrackingCsvCommitDto>, ProblemHttpResult>> CommitAsync(
        Guid tripLogId, TrackingCsvCommitRequest request, SilexGisDbContext db, IAccessService access,
        FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var loaded = await LoadAsync(tripLogId, db, access, protection, accessAccessor, ct);
        if (loaded.Refusal is { } refusal) return refusal;

        var user = await userAccessor.GetAsync(ct);
        if (user is null) return ApiProblems.NotFound("trip_log.not_found");

        if (!TripTrackingRules.MayWriteLog(loaded.Tracking!.State))
        {
            return ApiProblems.Conflict("tracking.not_writable",
                "A report lands on a watch that is armed or has been closed — arm the watch first.");
        }

        var (options, mapping) = Read(request.Options);
        var parsed = TrackingCsvParser.Parse(request.Text, options, mapping);
        var plan = TrackingCsvPlanner.Plan(parsed.Rows, loaded.Subject!);

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
        var skipped = 0;

        // Loaded once for the whole file rather than looked up per row: a trip's log is thousands
        // of rows at most, and a query per report is what turns importing a season into a minute.
        //
        // Grouped rather than keyed directly, because nothing makes the person and the instant
        // unique in the log: a typed "entered" and a typed note filed at the same minute for one
        // person are two rows under one key, and a dictionary built straight off them would throw
        // and fail the whole sheet with nothing naming the rows. A key the log holds once maps to
        // that row; a key it holds several times maps to null, which the loop below refuses on
        // the row exactly as the planner does — the planner is the ordinary gate for it, and this
        // is the same answer for a report typed in between the plan and the write.
        var existing = (await db.TripPositionEvents
                .Where(e => e.TripLogId == tripLogId)
                .ToListAsync(ct))
            .GroupBy(e => (e.CaverId, e.RecordedAt))
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First() : null);

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
                row.TeamId = report.TeamId;
                row.Kind = report.Kind;
                row.SurveyModelId = report.ViewerStationName is null ? null : loaded.SurveyModelId;
                row.CaveFeatureId = report.ViewerStationName is null ? null : loaded.CaveFeatureId;
                row.ViewerStationName = report.ViewerStationName;
                row.DepthEnteredM = report.Kind == TripPositionEventKind.AtDepth ? report.DepthM : null;
                row.Note = report.Note;
                updated++;
                continue;
            }

            var fresh = new TripPositionEvent
            {
                TripLogId = tripLogId,
                CaverId = report.CaverId,
                TeamId = report.TeamId,
                Kind = report.Kind,
                SurveyModelId = report.ViewerStationName is null ? null : loaded.SurveyModelId,
                CaveFeatureId = report.ViewerStationName is null ? null : loaded.CaveFeatureId,
                ViewerStationName = report.ViewerStationName,
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
            created, updated, skipped, [.. refused.Select(Diagnostic)]));
    }

    // ---- the trip a sheet is read against -----------------------------------------------

    /// <summary>What was loaded about the trip, or why the caller gets nothing.</summary>
    private sealed record Loaded(
        TrackingCsvSubject? Subject,
        Domain.Entities.TripTracking? Tracking,
        Guid? SurveyModelId,
        Guid? CaveFeatureId,
        ProblemHttpResult? Refusal);

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
    /// </remarks>
    private static async Task<Loaded> LoadAsync(
        Guid tripLogId, SilexGisDbContext db, IAccessService access, FeatureProtection protection,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null
            ? null
            : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return new Loaded(null, null, null, null, refusal);

        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking is null)
        {
            return new Loaded(null, null, null, null,
                ApiProblems.Conflict("tracking.not_configured", "This trip has no watch to import reports onto."));
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

        // Every key with how many rows the log holds under it. A key held once is one a sheet
        // corrects; a key held more than once is one the planner refuses, because which of the
        // rows the sheet means is not the importer's to guess.
        var existing = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == tripLogId)
            .GroupBy(e => new { e.CaverId, e.RecordedAt })
            .Select(g => new { g.Key.CaverId, g.Key.RecordedAt, Count = g.Count() })
            .ToListAsync(ct);

        var subject = new TrackingCsvSubject
        {
            Roster = [.. roster.Select(r => (r.Id, (string?)r.FullName))],
            Teams = [.. teams.Select(t => (t.Id, t.Name))],
            Declarations = declarations,
            Stations = stations,
            HasModel = usable is not null,
            Format = usable?.Model.Format ?? default,
            RootSurveyName = usable?.Model.RootSurveyName,
            ReferenceStationName = tracking.ReferenceStationName,
            DepthFilter = tracking.DepthFilter,
            Existing = existing.Select(e => (e.CaverId, e.RecordedAt)).ToHashSet(),
            ExistingSeveralTimes = existing.Where(e => e.Count > 1)
                .Select(e => (e.CaverId, e.RecordedAt)).ToHashSet(),
            Now = DateTimeOffset.UtcNow,
        };

        return new Loaded(subject, tracking, usable?.Model.Id, usable?.Cave.Id, null);
    }

    // ---- request and answer shapes ------------------------------------------------------

    /// <summary>
    /// The caller's choices as the reader wants them.
    /// </summary>
    /// <remarks>
    /// A field the caller did not name is left out of the mapping rather than given an empty
    /// header: the parser treats a named field as "look for exactly this and say so if it is
    /// missing", so naming a field with nothing would report every column as absent.
    /// </remarks>
    private static (TrackingCsvOptions Options, TrackingCsvColumnMapping Mapping) Read(
        TrackingCsvImportOptionsDto? dto)
    {
        var options = TrackingCsvOptions.Default;
        var mapping = TrackingCsvColumnMapping.Auto;
        if (dto is null)
        {
            return (options, mapping);
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
        if (dto.WentInWords is { Count: > 0 } || dto.CameOutWords is { Count: > 0 })
        {
            var shipped = TrackingCsvStateWords.Default;
            options = options with
            {
                StateWords = new TrackingCsvStateWords
                {
                    WentIn = dto.WentInWords is { Count: > 0 } wentIn ? wentIn : shipped.WentIn,
                    CameOut = dto.CameOutWords is { Count: > 0 } cameOut ? cameOut : shipped.CameOut,
                },
            };
        }

        foreach (var (name, header) in dto.Columns ?? new Dictionary<string, string>())
        {
            if (Enum.TryParse<TrackingCsvField>(name, ignoreCase: true, out var field)
                && !string.IsNullOrWhiteSpace(header))
            {
                mapping = mapping.With(field, header);
            }
        }

        return (options, mapping);
    }

    private static TrackingCsvPreviewRowDto Row(TrackingCsvPlannedReport report) =>
        new(report.Line, report.At, report.CaverId, report.CaverWritten, report.CaverMatched,
            report.MatchedBy.ToString(), report.TeamId, report.Kind, report.ViewerStationName,
            report.DepthM, report.Note, report.Replaces,
            [.. report.Diagnostics.Select(Diagnostic)]);

    private static TrackingCsvDiagnosticDto Diagnostic(TrackingCsvDiagnostic d) =>
        new(d.Severity.ToString(), d.Problem.ToString(), d.Line, d.Column, d.Detail);
}
