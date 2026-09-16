// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Surveys;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Import;

/// <summary>One scan that could not be recorded, and why.</summary>
public sealed record SpeleolocImportFailure(string PointId, DateTimeOffset ScannedAt, string Code, string Reason);

/// <summary>What one confirmation did.</summary>
public sealed record SpeleolocImportCommitResult(
    ImportBatch Batch,
    Guid TripLogId,
    bool CreatedTrip,
    int CreatedEventCount,
    IReadOnlyList<SpeleolocImportFailure> Failures);

/// <summary>A confirmation refused as a whole, rather than one scan of it.</summary>
public sealed class SpeleolocImportCommitException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Turns a reviewed device recording into a tracked trip's position history, in one transaction,
/// with a batch recording what it did so the whole import can be taken back in one act.
///
/// <para>
/// Four properties are the whole of this class.
/// </para>
/// <para>
/// An imported position is location data exactly as a relayed one is. Every row it writes carries
/// the same cave snapshot the live report path stamps, so the same rule decides who may read it,
/// against the same anchor, and goes on deciding after the model itself is replaced. The column
/// that records where the row came from is not consulted by any of that and is never emitted
/// beside a withheld position — a reader who may not learn a station must not learn that a phone
/// was involved either.
/// </para>
/// <para>
/// It never decides who somebody is. A device account maps to a person because a reviewer said so,
/// one account at a time, and a scan made by an account nobody mapped is refused rather than
/// attributed. The same goes the other way: a person the mapping names who is not on the trip's
/// roster is refused, not added, because putting somebody on a trip is a statement about who went
/// and it belongs to whoever runs the trip.
/// </para>
/// <para>
/// One bad scan does not cost the rest. Everything a scan needs is settled before a row is
/// constructed, so a refusal leaves nothing half-written and needs no savepoint to unpick — which
/// is the difference between this and a spreadsheet row, where creating a cave and then failing to
/// create its trip is a real sequence. Only a confirmation in which nothing at all landed is
/// refused outright.
/// </para>
/// <para>
/// It is silent. A trip created here is already finished, which is exactly the shape the write
/// path would otherwise announce to every person named on it.
/// </para>
/// </summary>
public sealed class SpeleolocTripImportCommitService(
    SilexGisDbContext db,
    SpeleolocArchiveReader reader,
    SpeleolocTripImportResolver resolver,
    TripLogWriteService trips,
    IOptions<ImportLimitOptions> limits)
{
    public async Task<SpeleolocImportCommitResult> CommitAsync(
        StoredFile file,
        SpeleolocImportOptions options,
        SurveyModel model,
        Feature cave,
        TripLog? trip,
        TripTracking? tracking,
        IReadOnlyCollection<string> selection,
        IReadOnlyDictionary<string, SpeleolocPointDecision> decisions,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cave);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(ctx);

        // Asked again here and not only at the route, because a group the caller may read is not a
        // group they may bind content to, and this is the layer that knows what is being bound.
        if (SpeleolocImportCreateRights.Refusal(ctx, options) is { } refusal)
        {
            throw new SpeleolocImportCommitException(refusal.Code, refusal.Message);
        }

        if (selection.Count == 0)
        {
            throw new SpeleolocImportCommitException(
                SpeleolocImportCodes.SelectionEmpty, "No scans were chosen.");
        }

        var ceiling = limits.Value.MaxCommitItems;
        if (selection.Count > ceiling)
        {
            throw new SpeleolocImportCommitException(
                SpeleolocImportCodes.SelectionTooLarge,
                $"A single confirmation records at most {ceiling} positions; {selection.Count} were chosen.");
        }

        // The model the endpoint validated has to be the model whose cave it validated. The two
        // arrive as separate arguments and a caller that paired them wrongly would stamp one
        // cave's protection on another cave's stations — cheap to assert, impossible to notice.
        if (model.CaveFeatureId != cave.Id)
        {
            throw new SpeleolocImportCommitException(
                SpeleolocImportCodes.ModelUnavailable, "The survey model does not belong to that cave.");
        }

        // The archive is read again rather than trusted from the preview: what the reviewer
        // approved is a set of point identifiers under a set of choices, and reading the archive
        // under those choices is the only thing that turns them back into scans.
        var read = await ReadRecordingAsync(file, options, ct);
        var points = read.Points.ToDictionary(p => p.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var resolution = await resolver.ResolveAsync(read.Points, options, model, tracking, ctx, ct);
        var byPoint = resolution.Points.ToDictionary(p => p.PointId, StringComparer.OrdinalIgnoreCase);

        var chosen = selection
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => decisions.GetValueOrDefault(id)?.Action != SpeleolocPointAction.Skip)
            .ToList();
        var skipped = selection.Distinct(StringComparer.OrdinalIgnoreCase).Count() - chosen.Count;

        var stationNames = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id)
            .Select(s => s.Name)
            .ToListAsync(ct);
        var knownStations = new HashSet<string>(stationNames, StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var batch = new ImportBatch
        {
            Source = ImportSource.SpeleolocArchive,
            ConfirmedByUserId = ctx.UserId,
            Mode = ImportBatchMode.Reviewed,
            Options = ImportJson.Serialize(options),
        };

        var failures = new List<SpeleolocImportFailure>();
        var items = new List<ImportBatchItem>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Who each chosen scan is about, settled before anything is written: a trip this
        // confirmation creates is created with exactly these people on it, and a trip that already
        // exists is asked whether it has them. Through the shared rule, because the loop below asks
        // the same question again and the two answering it differently is how a per-row override
        // ended up naming somebody the roster was then built without.
        var wanted = chosen
            .Select(id => SpeleolocPointMeaning.CaverOf(
                byPoint.GetValueOrDefault(id), decisions.GetValueOrDefault(id)))
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .Distinct()
            .ToList();

        var createdTrip = false;
        if (trip is null)
        {
            trip = await CreateTripAsync(read.Trip, options, wanted, ctx, ct);
            createdTrip = true;
            await CreateTrackingAsync(trip.Id, model, cave, ct);
            items.Add(new ImportBatchItem
            {
                TripLogId = trip.Id,
                Action = ImportDecisionAction.Create,
                SourceProperties = ImportJson.Serialize(new
                {
                    recordingId = read.Trip.Id.ToString(),
                    startedAt = read.Trip.StartedAt,
                    endedAt = read.Trip.EndedAt,
                    documentCount = read.Trip.DocumentCount,
                }),
            });
        }

        var participants = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == trip.Id)
            .Select(p => p.CaverId)
            .ToListAsync(ct);
        var roster = new HashSet<Guid>(participants);

        var events = new List<TripPositionEvent>();
        foreach (var pointId in chosen)
        {
            if (!points.TryGetValue(pointId, out var point) || !byPoint.TryGetValue(pointId, out var meaning))
            {
                // A scan the reviewer chose that the archive no longer reads as a scan of this
                // recording: the chosen recording moved, or the upload was replaced. Reported,
                // never guessed.
                failures.Add(new SpeleolocImportFailure(
                    pointId, DateTimeOffset.UnixEpoch, SpeleolocImportCodes.PointMissing,
                    "That scan is not one this archive reads as part of the chosen recording."));
                continue;
            }

            var decision = decisions.GetValueOrDefault(pointId);
            var caverId = SpeleolocPointMeaning.CaverOf(meaning, decision);
            if (caverId is null)
            {
                failures.Add(Failure(point, SpeleolocImportCodes.CaverUnmapped,
                    "Nothing says which person this device account is."));
                continue;
            }

            if (!roster.Contains(caverId.Value))
            {
                failures.Add(Failure(point, SpeleolocImportCodes.CaverNotParticipant,
                    "That person is not on the trip's roster."));
                continue;
            }

            var station = decision?.StationName ?? meaning.Candidates.FirstOrDefault()?.StationName;
            if (string.IsNullOrWhiteSpace(station))
            {
                failures.Add(Failure(point, SpeleolocImportCodes.StationUnresolved,
                    "Nothing says which station this scan is, and none could be proposed."));
                continue;
            }

            // A station named outright is checked against the model, exactly as a hand-typed
            // station report is. A station taken from the proposals was produced out of the same
            // model moments ago and is checked for the same reason: the two paths must not differ
            // in what they will accept, or the import becomes the way to write a name the live
            // path refuses.
            //
            // Which means checking it the way the live path does: by resolving the name against the
            // model rather than comparing it with the rows, because one of the two line-plot
            // formats is spelled differently by the survey viewer than by the rows read out of the
            // same file, and either spelling can arrive here — a reviewer may type a station they
            // read off the model. The same call the live path makes, so the two cannot come to
            // differ about which names a model answers to.
            var viewerName = SurveyStationNames.ViewerNameOfMatch(
                model.Format, model.RootSurveyName, station, knownStations.Contains);
            if (viewerName is null)
            {
                failures.Add(Failure(point, SpeleolocImportCodes.StationUnknown,
                    "That station is not one of the chosen model's stations."));
                continue;
            }

            if (point.ScannedAt > now + TripTrackingRules.RecordedAtSkew)
            {
                failures.Add(Failure(point, SpeleolocImportCodes.RecordedInFuture,
                    "The scan claims a moment after the clock."));
                continue;
            }

            var note = point.Notes?.Trim();
            if (note is { Length: > TripTrackingRules.MaxNoteLength })
            {
                failures.Add(Failure(point, SpeleolocImportCodes.NoteTooLong,
                    $"The scan's note is longer than the {TripTrackingRules.MaxNoteLength} characters a position carries."));
                continue;
            }

            var row = new TripPositionEvent
            {
                TripLogId = trip.Id,
                CaverId = caverId.Value,
                Kind = TripPositionEventKind.AtStation,
                Source = TripPositionEventSource.SpeleolocArchive,
                SurveyModelId = model.Id,
                // The protection anchor, and it is the same value the live path stamps. Whether a
                // reader may learn the station below is decided against this cave's chain and
                // nothing else, so an imported position is withheld by exactly the rule that
                // withholds a relayed one.
                CaveFeatureId = cave.Id,
                // The viewer's own spelling, as the live path stores it. An imported position is
                // drawn on the model by exactly the surface that draws a relayed one, so a name in
                // the other vocabulary would be a marker that silently never appears.
                ViewerStationName = viewerName,
                // Not the place's depth. A depth on a position row is what a reporter claimed
                // about where the party was; a device place's depth is a property of a marker
                // somebody bolted to the wall years ago, and writing it here would read as a
                // report nobody made.
                DepthEnteredM = null,
                Note = string.IsNullOrEmpty(note) ? null : note,
                RecordedAt = point.ScannedAt,
                RecordedByUserId = ctx.UserId,
            };
            events.Add(row);
            items.Add(new ImportBatchItem
            {
                TripPositionEventId = row.Id,
                TripLogId = trip.Id,
                Action = ImportDecisionAction.Create,
                // The scan's own identifiers and its clock, and deliberately not the station the
                // confirmation settled on. A batch line is provenance, read back through surfaces
                // that do not evaluate location protection, and a station name kept here would be
                // the one copy of a position that the withholding rule never sees.
                SourceProperties = ImportJson.Serialize(new
                {
                    pointId = point.Id.ToString(),
                    placeId = point.PlaceId?.ToString(),
                    scannedAt = point.ScannedAt,
                    deviceUserId = point.DeviceUserId?.ToString(),
                }),
            });
        }

        if (events.Count == 0)
        {
            // Nothing landed. A batch recording only failures is a row nobody can act on, and a
            // trip created to hold a history that turned out to be empty is worse — it would leave
            // a trip nobody asked for behind a confirmation that recorded nothing.
            await transaction.RollbackAsync(ct);
            throw new SpeleolocImportCommitException(
                SpeleolocImportCodes.NothingCreated,
                failures.Count > 0
                    ? $"None of the {failures.Count} chosen scans could be recorded. {failures[0].Reason}"
                    : "Every chosen scan was set aside, so nothing would be recorded.");
        }

        db.TripPositionEvents.AddRange(events);
        foreach (var item in items)
        {
            item.ImportBatchId = batch.Id;
        }

        batch.TripLogId = trip.Id;
        batch.CreatedCount = items.Count;
        batch.SkippedCount = skipped;
        db.ImportBatches.Add(batch);
        db.ImportBatchItems.AddRange(items);
        await db.SaveChangesAsync(ct);

        // The review is spent: its decisions describe scans that are now positions, and leaving it
        // would offer to record them a second time.
        await db.SpeleolocImportSessions
            .Where(s => s.StoredFileId == file.Id && s.UserId == ctx.UserId)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
        return new SpeleolocImportCommitResult(batch, trip.Id, createdTrip, events.Count, failures);
    }

    /// <summary>The recording the choices name, or a refusal naming what went wrong with the archive.</summary>
    private async Task<(SpeleolocArchiveTrip Trip, IReadOnlyList<SpeleolocArchivePoint> Points)> ReadRecordingAsync(
        StoredFile file, SpeleolocImportOptions options, CancellationToken ct)
    {
        if (!Guid.TryParse(options.TripUuid, out var recordingId))
        {
            throw new SpeleolocImportCommitException(
                SpeleolocImportCodes.RecordingNotFound, "No recording was chosen.");
        }

        var read = await reader.ReadTripAsync(file, recordingId, ct)
            ?? throw new SpeleolocImportCommitException(
                SpeleolocImportCodes.RecordingNotFound, "The archive does not hold that recording.");
        return read;
    }

    private static SpeleolocImportFailure Failure(SpeleolocArchivePoint point, string code, string reason) =>
        new(point.Id.ToString(), point.ScannedAt, code, reason);

    /// <summary>
    /// A trip out of what the recording itself says. Its title and dates are the device's; its
    /// roster is the people the reviewer mapped, because a trip created to hold their positions
    /// with nobody on it would refuse every one of them.
    /// </summary>
    private async Task<TripLog> CreateTripAsync(
        SpeleolocArchiveTrip recording,
        SpeleolocImportOptions options,
        IReadOnlyList<Guid> cavers,
        AccessContext ctx,
        CancellationToken ct)
    {
        var (participantRole, _) = await TripLogWriteService.ShippedRosterRolesAsync(db, ct);
        var title = string.IsNullOrWhiteSpace(recording.Title)
            ? recording.StartedAt.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : recording.Title.Trim();

        var input = new TripWriteInput
        {
            Title = title,
            TripDate = DateOnly.FromDateTime(recording.StartedAt.UtcDateTime),
            TripDateEnd = recording.EndedAt is { } ended
                ? DateOnly.FromDateTime(ended.UtcDateTime)
                : null,
            Description = Trimmed(recording.Description),
            Results = Trimmed(recording.Log),
            Participants = [.. cavers.Select(c => new TripRosterEntry { CaverId = c, RoleId = participantRole })],
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
        };

        // Silent, explicitly. The default announces, and a trip that is already finished is
        // precisely the shape that would otherwise send a message to everybody on it.
        var trip = await trips.CreateAsync(
            TripCreationIntent.Report, input, ctx, ctx.UserId, TripWriteNotice.Silent, ct);

        // A recording brought out of a cave is something that already happened.
        trip.State = ActivityState.Done;
        await db.SaveChangesAsync(ct);
        return trip;
    }

    /// <summary>
    /// The tracking configuration of a trip this confirmation has just created: closed, against the
    /// model the reviewer chose, because the moment the positions land a position timeline exists
    /// and the lifecycle says nothing returns to off once it does.
    ///
    /// <para>
    /// Only for a trip created here, and that is the whole rule. A trip that already existed keeps
    /// whatever configuration it has, including none. Writing one for it looked harmless and was
    /// not: a tracking row is an operational statement about somebody else's trip — it names a
    /// survey model, snapshots a cave anchor and reports the trip as tracked-and-closed — and the
    /// undo could not take it back. Nothing on <c>import_batch_items</c> points at a tracking row,
    /// the lifecycle has no transition back to off and no route deletes one, so a reverted import
    /// left a colleague's trip permanently reporting a state it never had, with no way to clear it
    /// through the API. For a trip created here the row needs no line of its own: it is keyed by the
    /// trip and cascades with it, so the one undo that removes the trip removes this too.
    /// </para>
    /// <para>
    /// The positions themselves lose nothing by the absence. Each row carries its own survey model
    /// and its own cave anchor, which is what the withholding rule is evaluated against and what the
    /// history surfaces read — the configuration is the live watch's settings, not the history's.
    /// </para>
    /// </summary>
    private async Task CreateTrackingAsync(Guid tripLogId, SurveyModel model, Feature cave, CancellationToken ct)
    {
        db.TripTrackings.Add(new Domain.Entities.TripTracking
        {
            TripLogId = tripLogId,
            State = TripTrackingState.Closed,
            SurveyModelId = model.Id,
            CaveFeatureId = cave.Id,
            ClosedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
