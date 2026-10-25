// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Surveys;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Trips;

/// <summary>What one conversion attempt did with one recording.</summary>
public enum TripRecordingConversionOutcome
{
    /// <summary>A trip was made with a position for every scan.</summary>
    Converted,

    /// <summary>A trip was made with the scans that settled, and flagged for the rest.</summary>
    ConvertedWithAttention,

    /// <summary>Something could not be settled and the installation keeps such recordings as recordings.</summary>
    LeftWaiting,

    /// <summary>The recording is still open on the phone; it is converted once it is closed.</summary>
    StillOpen,

    /// <summary>The recording already has its trip; this upload changed it, and the trip was flagged.</summary>
    ChangedAfterConversion,

    /// <summary>Nothing to do: already converted by this same upload, or its trip is gone.</summary>
    AlreadyConverted,

    /// <summary>The recording, or its cave, is no longer here.</summary>
    Missing,

    /// <summary>The uploader may no longer make this trip; the recording waits for somebody who may.</summary>
    Refused,
}

/// <summary>What one conversion attempt did.</summary>
public sealed record TripRecordingConversionResult(
    Guid RecordingId, TripRecordingConversionOutcome Outcome, Guid? TripLogId, int Positions, int Unsettled);

/// <summary>
/// Makes a trip report out of a recording a phone sent, with nobody choosing anything: the
/// recording's own title, dates and cave, a position for every scan that settles on exactly one
/// station, and the people its phone users are mapped to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is decided by the rules the archive import already has.</b> The scans are read by
/// the archive import's resolver — the one home of the rule that turns a marked place into
/// stations and a phone user into a person — and a scan becomes a position only when that rule's
/// answer is a single answer. What is not settled is never guessed: it is left out of the trip and
/// named, by the phone's identifiers, on the trip's attention row.
/// </para>
/// <para>
/// <b>Done as the uploader.</b> The trip is the uploader's, in the club and audience the recording
/// carries, and the places, the cave and the survey model are read under the uploader's own rights
/// — so a conversion can never make a position out of a station the uploader could not have been
/// shown, and a trip it makes is one the uploader could have made by hand. When those rights no
/// longer allow it, nothing is made and the recording waits.
/// </para>
/// <para>
/// <b>Silent, and once.</b> The trip is finished the moment it exists, which is exactly the shape
/// the trip write path would otherwise announce to everybody named on it. A recording is claimed
/// by a write guarded by "has no trip yet", so two conversions of one recording at once make one
/// trip; and a re-run of the same upload's conversion finds its own work already done.
/// </para>
/// <para>
/// <b>A recording that changes after its trip was made does not rewrite the trip.</b> Somebody may
/// have worked on it since; the trip is flagged instead, and the people who look after phone
/// uploads are told.
/// </para>
/// <para>
/// The caller provides the unit of work: one recording per context, because a refusal half-way
/// leaves staged rows in the context that must not ride out on anybody else's save.
/// </para>
/// </remarks>
public sealed class TripRecordingConverter(
    SilexGisDbContext db,
    SpeleolocTripImportResolver resolver,
    TripLogWriteService trips,
    IAccessService access,
    FeatureProtection protection,
    IChangeContext changes,
    TimeProvider clock,
    ILogger<TripRecordingConverter> logger)
{
    /// <summary>
    /// Converts one recording that upload <paramref name="importBatchId"/> wrote or changed.
    /// </summary>
    /// <param name="changedPointIds">The recording's scans the upload wrote, for a change after conversion.</param>
    /// <param name="convertWithProblems">The installation's choice for a recording that does not fully settle.</param>
    public async Task<TripRecordingConversionResult> ConvertAsync(
        Guid recordingId,
        Guid importBatchId,
        IReadOnlyCollection<Guid> changedPointIds,
        bool convertWithProblems,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changedPointIds);

        // Read past the deletion filter: a phone deleting a recording that already has its trip is
        // a change after conversion like any other, and the trip is flagged for it. A deleted one
        // with no trip has nothing to become.
        var recording = await db.TripRecordings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null || (recording.DeletedAt is not null && recording.ConvertedTripLogId is null))
        {
            return Result(recordingId, TripRecordingConversionOutcome.Missing);
        }

        // Every history row this writes names the upload the conversion came out of, so the history
        // says the trip was made from a phone, and an undo of that upload can tell whether anybody
        // has touched the trip since.
        changes.Begin(ChangeSource.TripConversion, importBatchId);
        changes.ActFor(recording.OwnerUserId);

        if (recording.ConvertedTripLogId is { } convertedTripId)
        {
            return await FlagChangeAsync(recording, convertedTripId, importBatchId, changedPointIds, ct);
        }

        // A phone sends a recording while its trip is still under way as readily as afterwards. A
        // trip report made from half a trip would be flagged as changed by every scan after it, so
        // an open recording waits; the upload that closes it converts it.
        if (recording.EndedAt is null)
        {
            return Result(recording.Id, TripRecordingConversionOutcome.StillOpen);
        }

        var cave = await db.Features.AsNoTracking().FirstOrDefaultAsync(
            f => f.Id == recording.CaveFeatureId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null)
        {
            return Result(recording.Id, TripRecordingConversionOutcome.Missing);
        }

        var ctx = await AccessContextResolver.ResolveAsync(db, recording.OwnerUserId, ct);
        var model = await ModelOfAsync(cave, ctx, ct);
        var points = await db.TripRecordingPoints.AsNoTracking()
            .Where(p => p.RecordingId == recording.Id && p.DeletedAt == null)
            .OrderBy(p => p.ScannedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var options = new SpeleolocImportOptions
        {
            TripUuid = recording.Id.ToString(),
            SurveyModelId = model?.Id,
            CavingGroupId = recording.CavingGroupId,
            Visibility = recording.Visibility,
            CandidateCount = TripRecordingConversionRules.CandidatesAsked,
        };
        var resolution = await resolver.ResolveAsync(recording, points, options, model, tracking: null, ctx, ct);
        var verdicts = await VerdictsAsync(resolution, model, ct);
        var reasons = TripRecordingConversionRules.Collect(verdicts);
        var unsettled = verdicts.Count(v => !v.Recordable);

        if (reasons.Count > 0 && !convertWithProblems)
        {
            await NotifyWaitingAsync(recording, cave, unsettled, ct);
            await db.SaveChangesAsync(ct);
            return Result(recording.Id, TripRecordingConversionOutcome.LeftWaiting, unsettled: unsettled);
        }

        // The people on the trip: everybody a scan's phone user is mapped to — a scan whose station
        // did not settle still says its author was underground — and whoever started the recording.
        var starter = recording.CreatedByDeviceUserId is { } starterDevice
            ? (await DevicePersonMapping.LinksOfAsync(db, [starterDevice], ct)).GetValueOrDefault(starterDevice)
            : (Guid?)null;
        var cavers = verdicts.Where(v => v.CaverId is not null).Select(v => v.CaverId!.Value)
            .Prepend(starter ?? Guid.Empty)
            .Where(c => c != Guid.Empty)
            .Distinct()
            .ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        TripLog trip;
        try
        {
            trip = await CreateTripAsync(recording, cave, cavers, ctx, ct);
        }
        catch (TripWriteException e)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            logger.LogWarning(
                "Recording {RecordingId} was not made into a trip: {Code}", recording.Id, e.Code);
            return Result(recording.Id, TripRecordingConversionOutcome.Refused, unsettled: unsettled);
        }

        // The claim. Guarded by the condition that selected the recording, so a second conversion
        // racing this one — or an archive import of the same recording — finds the recording
        // taken and makes nothing.
        var claimed = await db.TripRecordings
            .Where(r => r.Id == recording.Id && r.ConvertedTripLogId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConvertedTripLogId, trip.Id), ct);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result(recording.Id, TripRecordingConversionOutcome.AlreadyConverted);
        }

        if (model is not null)
        {
            // Closed, against the model the positions are placed on, exactly as the archive import
            // leaves a trip it creates: a position timeline exists from this moment, and it is the
            // finished record of a trip that is over.
            db.TripTrackings.Add(new TripTracking
            {
                TripLogId = trip.Id,
                State = TripTrackingState.Closed,
                SurveyModelId = model.Id,
                CaveFeatureId = cave.Id,
                ClosedAt = clock.GetUtcNow(),
            });
        }

        var byPoint = points.ToDictionary(p => p.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var positions = 0;
        foreach (var verdict in verdicts.Where(v => v.Recordable))
        {
            var point = byPoint[verdict.PointId];
            db.TripPositionEvents.Add(new TripPositionEvent
            {
                TripLogId = trip.Id,
                CaverId = verdict.CaverId!.Value,
                Kind = TripPositionEventKind.AtStation,
                Source = TripPositionEventSource.SpeleolocSync,
                SurveyModelId = model!.Id,
                // The protection anchor the live report path and the archive import stamp: whether
                // a reader may learn the station is decided against this cave and nothing else.
                CaveFeatureId = cave.Id,
                ViewerStationName = verdict.StationName,
                // A place's depth is a property of a marker, not a report of where the party was.
                DepthEnteredM = null,
                Note = TripRecordingConversionRules.NoteOf(point.Notes),
                RecordedAt = point.ScannedAt,
                RecordedByUserId = recording.OwnerUserId,
            });
            positions++;
        }

        if (reasons.Count > 0)
        {
            db.TripAttentions.Add(new TripAttention
            {
                TripLogId = trip.Id,
                Reasons = TripRecordingConversionRules.Serialize(reasons),
                RaisedAt = clock.GetUtcNow(),
            });
            await NotifyAttentionAsync(trip, recording.OwnerUserId, unsettled, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new TripRecordingConversionResult(
            recording.Id,
            reasons.Count > 0 ? TripRecordingConversionOutcome.ConvertedWithAttention : TripRecordingConversionOutcome.Converted,
            trip.Id,
            positions,
            unsettled);
    }

    /// <summary>
    /// The verdict on every scan, with the one check the resolver does not make: that the station it
    /// proposed is a name the model answers to, asked the way the live report path asks it, so a
    /// conversion cannot write a station name a hand-typed report would be refused.
    /// </summary>
    private async Task<List<TripRecordingScanVerdict>> VerdictsAsync(
        SpeleolocImportResolutionSet resolution, SurveyModel? model, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var verdicts = resolution.Points.Select(r => TripRecordingConversionRules.Judge(r, now)).ToList();
        if (model is null || verdicts.All(v => v.StationName is null))
        {
            return verdicts;
        }

        var names = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id)
            .Select(s => s.Name)
            .ToListAsync(ct);
        var known = new HashSet<string>(names, StringComparer.Ordinal);

        return [.. verdicts.Select(v =>
        {
            if (v.StationName is null)
            {
                return v;
            }

            var viewerName = SurveyStationNames.ViewerNameOfMatch(
                model.Format, model.RootSurveyName, v.StationName, known.Contains);
            return viewerName is not null
                ? v with { StationName = viewerName }
                : v with { StationName = null, Reasons = [.. v.Reasons, TripAttentionReasons.UnresolvedStation] };
        })];
    }

    /// <summary>
    /// The model a cave's positions are placed on: its current line plot, or its newest finished one
    /// when none carries the mark — and only when the uploader may see the cave's models at all,
    /// because a station is a position in the cave the protection withholds.
    /// </summary>
    private async Task<SurveyModel?> ModelOfAsync(Feature cave, AccessContext ctx, CancellationToken ct)
    {
        if (!await SurveyModelVisibility.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return null;
        }

        var linePlots = SurveyModelKinds.FormatsOf(SurveyModelKind.LinePlot);
        return await db.SurveyModels.AsNoTracking()
            .Where(m => m.CaveFeatureId == cave.Id && m.Status == SurveyModelStatus.Ready && linePlots.Contains(m.Format))
            .OrderByDescending(m => m.IsCurrent)
            .ThenByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The trip, out of what the recording says: its title (or its first day), its days, its
    /// description and log, its cave, the people mapped from its phone users, and the recording's own
    /// club and audience. Finished, and created silently.
    /// </summary>
    private async Task<TripLog> CreateTripAsync(
        TripRecording recording, Feature cave, IReadOnlyList<Guid> cavers, AccessContext ctx, CancellationToken ct)
    {
        var (participantRole, _) = await TripLogWriteService.ShippedRosterRolesAsync(db, ct);
        var title = string.IsNullOrWhiteSpace(recording.Title)
            ? recording.StartedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : recording.Title.Trim();

        var input = new TripWriteInput
        {
            Title = title,
            TripDate = DateOnly.FromDateTime(recording.StartedAt.UtcDateTime),
            // An end before the start is a phone's clock; the trip is the day it started rather than
            // a range the trip write refuses, losing the whole recording over a figure nobody typed.
            TripDateEnd = recording.EndedAt is { } ended && DateOnly.FromDateTime(ended.UtcDateTime) > DateOnly.FromDateTime(recording.StartedAt.UtcDateTime)
                ? DateOnly.FromDateTime(ended.UtcDateTime)
                : null,
            Description = Trimmed(recording.Description),
            Results = Trimmed(recording.Log),
            Participants = [.. cavers.Select(c => new TripRosterEntry { CaverId = c, RoleId = participantRole })],
            CavingGroupId = recording.CavingGroupId,
            Visibility = recording.Visibility,
            CaveIds = [cave.Id],
        };

        var trip = await trips.CreateAsync(
            TripCreationIntent.Report, input, ctx, recording.OwnerUserId, TripWriteNotice.Silent, ct);
        trip.State = ActivityState.Done;
        return trip;
    }

    /// <summary>
    /// A recording that already has its trip was changed by an upload: the trip is flagged, not
    /// rewritten. A re-run of the very upload that made the trip — or that already flagged it — is
    /// recognised by the history it wrote, and changes nothing.
    /// </summary>
    private async Task<TripRecordingConversionResult> FlagChangeAsync(
        TripRecording recording,
        Guid tripId,
        Guid importBatchId,
        IReadOnlyCollection<Guid> changedPointIds,
        CancellationToken ct)
    {
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId, ct);
        if (trip is null)
        {
            return Result(recording.Id, TripRecordingConversionOutcome.AlreadyConverted, tripId);
        }

        var key = tripId.ToString();
        var alreadyHere = await db.AuditEntries.AsNoTracking().AnyAsync(
            a => a.ImportBatchId == importBatchId
                && a.Source == ChangeSource.TripConversion
                && ((a.EntityType == nameof(TripLog) && a.EntityId == key)
                    || (a.RootEntityType == nameof(TripLog) && a.RootEntityId == key)),
            ct);
        if (alreadyHere)
        {
            return Result(recording.Id, TripRecordingConversionOutcome.AlreadyConverted, tripId);
        }

        var raised = new[] { TripRecordingConversionRules.ChangedAfterConversion(changedPointIds.Select(p => p.ToString())) };

        // Raised against the row as it was read. The flag carries a version (its write time), so a
        // clearance committed in between, or another upload's job raising it first, makes this save
        // fail rather than land a reason on a row that reads as cleared, or a second row for one
        // trip; it is then read again and merged again.
        for (var attempt = 1; ; attempt++)
        {
            var attention = await db.TripAttentions.FirstOrDefaultAsync(a => a.TripLogId == tripId, ct);
            if (attention is null)
            {
                db.TripAttentions.Add(new TripAttention
                {
                    TripLogId = tripId,
                    Reasons = TripRecordingConversionRules.Serialize(raised),
                    RaisedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                attention.Reasons = TripRecordingConversionRules.Serialize(
                    TripRecordingConversionRules.Merge(TripRecordingConversionRules.Parse(attention.Reasons), raised));
                attention.RaisedAt = clock.GetUtcNow();
                attention.ClearedAt = null;
                attention.ClearedByUserId = null;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException) when (attempt < MaxFlagAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }

        await NotifyAttentionAsync(trip, recording.OwnerUserId, 0, ct);
        await db.SaveChangesAsync(ct);
        return Result(recording.Id, TripRecordingConversionOutcome.ChangedAfterConversion, tripId);
    }

    /// <summary>How many times a flag is read and merged again when somebody else wrote it in between.</summary>
    private const int MaxFlagAttempts = 4;

    private async Task NotifyAttentionAsync(TripLog trip, Guid uploaderUserId, int unsettled, CancellationToken ct)
    {
        var recipients = await TripAttentionRecipients.WhoMayBeToldAsync(db, access, trip, uploaderUserId, ct);
        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.TripNeedsAttention,
                MessageTemplateCatalog.NotifyTripNeedsAttention,
                new Dictionary<string, string>
                {
                    ["tripTitle"] = trip.Title,
                    ["tripDate"] = trip.TripDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["unsettledCount"] = unsettled.ToString(CultureInfo.InvariantCulture),
                    ["url"] = $"/trip-logs/{trip.Id}",
                },
                NotificationTargetKind.TripLog,
                trip.Id);
        }
    }

    private async Task NotifyWaitingAsync(TripRecording recording, Feature cave, int unsettled, CancellationToken ct)
    {
        // The recording has no page of its own; the cave it was made in does, and whether the reader
        // may open it is decided again when they read the message.
        var recipients = await TripAttentionRecipients.WhoMayBeToldAsync(db, access, cave, recording.OwnerUserId, ct);
        var title = string.IsNullOrWhiteSpace(recording.Title)
            ? recording.StartedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : recording.Title.Trim();
        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.TripNeedsAttention,
                MessageTemplateCatalog.NotifyTripRecordingWaiting,
                new Dictionary<string, string>
                {
                    ["tripTitle"] = title,
                    ["tripDate"] = recording.StartedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["unsettledCount"] = unsettled.ToString(CultureInfo.InvariantCulture),
                    ["url"] = $"/features/{cave.Id}",
                },
                NotificationTargetKind.Feature,
                cave.Id);
        }
    }

    private static TripRecordingConversionResult Result(
        Guid recordingId, TripRecordingConversionOutcome outcome, Guid? tripId = null, int unsettled = 0) =>
        new(recordingId, outcome, tripId, 0, unsettled);

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
