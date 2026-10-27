// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The fields a reviewer may accept a held edit by on their own, kind by kind. A feature row is
/// accepted by name, description, properties and position; a photo or document by title, caption
/// and its new file; a recording by title, description, log, times and cave; a scan by its notes,
/// place and time; a raster map by title, map type, order, area and its new picture. A document
/// link, a pin and a phone user are accepted or rejected whole: each is one fact.
/// </summary>
internal static class SyncHeldFields
{
    public const string Name = "name";
    public const string Description = "description";
    public const string Properties = "properties";

    /// <summary>The point, altitude, position quality and main-entrance flag, which move together.</summary>
    public const string Position = "position";

    public const string Title = "title";

    /// <summary>New bytes, taken whole: a file is never written in part.</summary>
    public const string File = "file";

    public const string Log = "log";

    /// <summary>When a recording started and ended, which move together.</summary>
    public const string Times = "times";

    public const string Cave = "cave";
    public const string Notes = "notes";
    public const string Place = "place";

    /// <summary>When a scan was made.</summary>
    public const string Time = "time";

    public const string MapType = "mapType";
    public const string SortOrder = "sortOrder";
    public const string CaveArea = "caveArea";
}

/// <summary>How a section states one field of a held edit beside the row as it stands.</summary>
internal static class SyncHeldComparisons
{
    /// <summary>Both values as the upload's own JSON writes them, compared as JSON.</summary>
    public static HeldFieldComparison Of(string field, object? current, object? proposed)
    {
        var now = JsonSerializer.SerializeToNode(current, SyncUploadJson.Options);
        var then = JsonSerializer.SerializeToNode(proposed, SyncUploadJson.Options);
        return new HeldFieldComparison(field, now, then, !JsonNode.DeepEquals(now, then));
    }

    /// <summary>
    /// A field whose value is a feature: shown by id, for the screen to name through the feature's
    /// own route. A current one the viewer may not read is left out.
    /// </summary>
    public static HeldFieldComparison Feature(string field, Guid? current, Guid? proposed, IReadOnlySet<Guid> readable) =>
        current is { } id && !readable.Contains(id)
            ? new HeldFieldComparison(field, null, JsonSerializer.SerializeToNode(proposed, SyncUploadJson.Options), true, CurrentWithheld: true)
            : Of(field, current, proposed);

    /// <summary>
    /// A moment at the precision the database keeps it, in UTC, so a value that went to a phone and
    /// came back unchanged compares equal to the stored one.
    /// </summary>
    public static DateTimeOffset? Moment(DateTimeOffset? value) =>
        value is { } at ? new DateTimeOffset(at.UtcTicks / 10 * 10, TimeSpan.Zero) : null;
}

/// <summary>
/// Writes the lines a reviewer accepted of a held phone upload, through each section's own two
/// steps — the code an upload nobody holds runs — with the uploader's access decisions.
/// </summary>
/// <remarks>
/// The upload's own checks are made again for every line, as they stand now: that the set is
/// still the uploader's, that its club may still be bound to, and then every check the section
/// makes. A refusal is the line's outcome, in the words the upload would have used.
/// </remarks>
internal sealed class SyncHeldBatchHandler(
    SilexGisDbContext db,
    FeatureWriteService writer,
    FeatureProtection protection,
    IAccessService access,
    IAppSettingsService settings,
    DocumentWriteService documents,
    UploadIngestService ingest,
    ContentIntake intake) : IHeldBatchHandler
{
    public ImportSource Source => ImportSource.DeviceSync;

    /// <summary>
    /// The batches this decision has already queued the trip conversion for. A decision hands its
    /// lines over a few at a time, each after what it depends on, and one batch's recordings are
    /// converted by one job however many calls wrote them. The handler lives for one request, so
    /// this never outlasts the decision.
    /// </summary>
    private readonly HashSet<Guid> convertQueued = [];

    private IReadOnlyDictionary<string, ISyncUploadSection>? sections;

    /// <summary>The sections by name, built once for the request the handler lives in.</summary>
    private async Task<IReadOnlyDictionary<string, ISyncUploadSection>> SectionsAsync(CancellationToken ct) =>
        sections ??= SyncUploadSections.InApplyOrder(
                writer, protection, access, await settings.GetSyncAsync(ct), documents, ingest, intake)
            .ToDictionary(s => s.Name, StringComparer.Ordinal);

    /// <summary>What the line's own section says it may be accepted by; nothing for a proposal it cannot read.</summary>
    public async Task<IReadOnlyList<string>> FieldsOfAsync(ImportBatchItem line, CancellationToken ct)
    {
        if (line.ProposalSection is not { } name || line.Proposal is not { } proposal
            || !(await SectionsAsync(ct)).TryGetValue(name, out var section))
        {
            return [];
        }

        try
        {
            return section.FieldsOf(proposal);
        }
        catch (JsonException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<HeldLineOutcome>> ApplyAsync(
        ImportBatch batch,
        IReadOnlyList<ImportBatchItem> lines,
        AccessContext uploader,
        bool overCurrent,
        CancellationToken ct)
    {
        var setId = SetOf(batch);
        var set = setId is null
            ? null
            : await db.SyncSets.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == setId && x.OwnerUserId == batch.ConfirmedByUserId, ct);
        if (set is null)
        {
            return Refuse(lines, "sync.set_not_found", "The set this upload went into no longer exists.");
        }

        if (set.CavingGroupId is { } groupId && !CavingGroupBindingRules.MayBind(uploader, AccessDomain.Features, groupId))
        {
            return Refuse(lines, "sync.caving_group_forbidden", "The uploader may no longer add rows to that caving group.");
        }

        var sections = SyncUploadSections.InApplyOrder(
                writer, protection, access, await settings.GetSyncAsync(ct), documents, ingest, intake)
            .ToDictionary(s => s.Name, StringComparer.Ordinal);
        var run = new SyncUploadRun { Db = db, Access = uploader, Set = set, Batch = batch };

        var outcomes = new List<HeldLineOutcome>(lines.Count);
        var changedSince = new List<Guid>();
        var recordings = false;
        foreach (var line in lines)
        {
            if (line.ProposalSection is not { } name || !sections.TryGetValue(name, out var section)
                || line.Proposal is not { } proposal)
            {
                outcomes.Add(new HeldLineOutcome(line.Id, false, null, "review.proposal_unreadable",
                    "This line keeps no proposal this server can write."));
                continue;
            }

            var accepted = await section.AcceptHeldAsync(run, proposal, line.AcceptedFields, overCurrent, ct);
            var result = accepted.Applied.Result;
            if (accepted.ChangedSince)
            {
                changedSince.Add(line.ReviewId ?? Guid.Empty);
            }

            if (SyncUploadTally.IsWritten(result.Status))
            {
                await db.SaveChangesAsync(ct);
                var written = accepted.Applied.Item;
                recordings |= written is { TripRecordingId: not null } or { TripRecordingPointId: not null };
                outcomes.Add(new HeldLineOutcome(line.Id, true, written));
                continue;
            }

            // Whatever the refused write had begun to change stays out of the database, exactly
            // as in an upload: the refusal is the whole of what happened to the line.
            db.ChangeTracker.Clear();
            outcomes.Add(result.Status == SyncRowStatus.Unchanged
                ? new HeldLineOutcome(line.Id, true, null)
                : new HeldLineOutcome(line.Id, false, null, result.Code ?? "sync.conflict", result.Detail));
        }

        if (changedSince.Count > 0)
        {
            throw new HeldChangedSinceException(changedSince);
        }

        // Recordings this acceptance wrote are made into trips by the same job an upload queues,
        // in the same transaction, so it exists exactly when the recordings do.
        if (recordings && convertQueued.Add(batch.Id))
        {
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.TripRecordingConvert,
                Payload = JsonSerializer.Serialize(
                    new Infrastructure.Jobs.TripRecordingConvertPayload(batch.Id), JsonSerializerOptions.Web),
                RequestedBy = null,
            });
            await db.SaveChangesAsync(ct);
        }

        return outcomes;
    }

    public async Task<IReadOnlyDictionary<long, HeldLineView>> ShowAsync(
        IReadOnlyList<ImportBatchItem> lines, AccessContext viewer, CancellationToken ct)
    {
        var shown = new Dictionary<long, HeldLineView>(lines.Count);
        if (lines.Count == 0)
        {
            return shown;
        }

        var sections = await SectionsAsync(ct);
        var facts = new Dictionary<long, SyncHeldFacts>();
        foreach (var line in lines)
        {
            if (FactsOf(sections, line.ProposalSection, line.Proposal) is { } read)
            {
                facts[line.Id] = read;
            }
        }

        // A held create can hang under another held create — an entrance under a cave sent beside
        // it, a scan on a recording still waiting — and so on up to a row that exists. Whether the
        // viewer may read or place the line is decided by the existing rows at the top of that
        // chain, so the uploader's held creates are followed until every name is either a row in
        // the database or something nobody holds any more.
        var batchId = lines[0].ImportBatchId;
        var uploaderId = await db.ImportBatches.AsNoTracking()
            .Where(b => b.Id == batchId).Select(b => b.ConfirmedByUserId).FirstAsync(ct);
        var existing = new Dictionary<SyncHeldTarget, DateTimeOffset>();
        var heldCreates = new Dictionary<Guid, SyncHeldFacts?>();
        var asked = new HashSet<SyncHeldTarget>();
        var frontier = facts.Values.SelectMany(TargetsOf).ToHashSet();
        for (var round = 0; frontier.Count > 0 && round < MaxChainDepth; round++)
        {
            frontier.ExceptWith(asked);
            asked.UnionWith(frontier);
            foreach (var (target, revision) in await ExistingAsync(frontier, ct))
            {
                existing[target] = revision;
            }

            var missing = frontier.Where(t => !existing.ContainsKey(t)).Select(t => t.Id)
                .Where(id => !heldCreates.ContainsKey(id)).Distinct().ToList();
            frontier = [];
            if (missing.Count == 0)
            {
                continue;
            }

            var held = await (from item in db.ImportBatchItems.AsNoTracking()
                              join batch in db.ImportBatches.AsNoTracking() on item.ImportBatchId equals batch.Id
                              where batch.ConfirmedByUserId == uploaderId
                                  && batch.Source == ImportSource.DeviceSync
                                  && item.Action == ImportDecisionAction.Create
                                  && item.ProposalRowId != null
                                  && missing.Contains(item.ProposalRowId.Value)
                              orderby item.Id descending
                              select new { RowId = item.ProposalRowId!.Value, item.ProposalSection, item.Proposal })
                .ToListAsync(ct);
            foreach (var id in missing)
            {
                var latest = held.FirstOrDefault(h => h.RowId == id);
                var read = latest is null ? null : FactsOf(sections, latest.ProposalSection, latest.Proposal);
                heldCreates[id] = read;
                if (read is not null)
                {
                    frontier.UnionWith(read.Mentions);
                }
            }
        }

        var readable = await ReadableAsync([.. existing.Keys], viewer, ct);
        var comparing = new SyncHeldComparing
        {
            Db = db,
            ReadableFeatures = (ids, token) => ReadableFeaturesAsync(ids, viewer, token),
        };
        var withheld = await SyncWithhold.WithheldIdsAsync(
            protection, viewer,
            [.. readable.Where(t => t.Kind == SyncHeldTargetKind.Feature).Select(t => t.Id)],
            ct);

        foreach (var line in lines)
        {
            if (!facts.TryGetValue(line.Id, out var fact))
            {
                // A proposal this build cannot read is shown to nobody but as its decision: what
                // it names cannot be checked, so nothing of it is let out.
                shown[line.Id] = new HeldLineView(null, false);
                continue;
            }

            var reach = new Reach();
            foreach (var target in TargetsOf(fact))
            {
                Walk(target, existing, heldCreates, reach, new HashSet<Guid>());
            }

            if (reach.Rows.Any(t => !readable.Contains(t)))
            {
                shown[line.Id] = new HeldLineView(null, false);
                continue;
            }

            var changedSince = line.ReviewDecision == ImportReviewDecision.Pending
                && fact is { Edited: { } edited, BaseRevision: { } claimed }
                && existing.TryGetValue(edited, out var current)
                && !SyncRevisions.Same(current, claimed);

            // A position under a row the viewer may not place, or under a row that can no longer
            // be found, is not shown: the second fails closed, since what it hung under is unknown.
            var hidePosition = fact.CarriesPosition
                && (reach.Unresolved || reach.Rows.Any(t => t.Kind == SyncHeldTargetKind.Feature && withheld.Contains(t.Id)));

            // A waiting edit the reviewer can take in part is drawn field by field, by its section,
            // only now that the viewer may be shown the row it changes and everything it names.
            var compared = line.ReviewDecision == ImportReviewDecision.Pending
                && sections.TryGetValue(line.ProposalSection!, out var section)
                && (await FieldsOfAsync(line, ct)).Count > 0
                    ? await section.CompareAsync(comparing, line.Proposal!, ct)
                    : null;
            shown[line.Id] = new HeldLineView(
                hidePosition ? WithoutPosition(line.Proposal!) : line.Proposal, changedSince, compared);
        }

        return shown;
    }

    /// <summary>Of <paramref name="ids"/>, the features <paramref name="viewer"/> may read.</summary>
    private async Task<IReadOnlySet<Guid>> ReadableFeaturesAsync(
        IReadOnlyCollection<Guid> ids, AccessContext viewer, CancellationToken ct) =>
        ids.Count == 0
            ? new HashSet<Guid>()
            : (await db.Features.AsNoTracking()
                    .Where(f => ids.Contains(f.Id))
                    .VisibleTo(viewer, db.Features, db.FeatureSetMembers)
                    .Select(f => f.Id)
                    .ToListAsync(ct))
                .ToHashSet();

    /// <summary>
    /// How many held creates deep a chain is followed. A phone's tree is a cave, its entrances
    /// and the places under them; anything deeper than this is treated as unresolved, which hides
    /// a position rather than shows one.
    /// </summary>
    private const int MaxChainDepth = 16;

    /// <summary>
    /// Where a held proposal keeps the item the phone sent, and the members of it that say where
    /// something is: a feature row's point and what moves with it, a pin's pixel on its map.
    /// </summary>
    private static readonly (string Item, string[] Members)[] PositionMembers =
    [
        ("row", ["geometry", "altitude", "positionQuality", "isMain"]),
        ("pin", ["x", "y"]),
    ];

    private sealed class Reach
    {
        public HashSet<SyncHeldTarget> Rows { get; } = [];

        public bool Unresolved { get; set; }
    }

    private static IEnumerable<SyncHeldTarget> TargetsOf(SyncHeldFacts facts) =>
        facts.Edited is { } edited ? facts.Mentions.Prepend(edited) : facts.Mentions;

    private static void Walk(
        SyncHeldTarget target,
        Dictionary<SyncHeldTarget, DateTimeOffset> existing,
        Dictionary<Guid, SyncHeldFacts?> heldCreates,
        Reach reach,
        HashSet<Guid> path)
    {
        if (existing.ContainsKey(target))
        {
            reach.Rows.Add(target);
            return;
        }

        if (!path.Add(target.Id) || !heldCreates.TryGetValue(target.Id, out var held) || held is null)
        {
            reach.Unresolved = true;
            return;
        }

        foreach (var next in held.Mentions)
        {
            Walk(next, existing, heldCreates, reach, path);
        }

        path.Remove(target.Id);
    }

    private static SyncHeldFacts? FactsOf(
        IReadOnlyDictionary<string, ISyncUploadSection> sections, string? section, string? proposal)
    {
        if (section is null || proposal is null || !sections.TryGetValue(section, out var owner))
        {
            return null;
        }

        try
        {
            return owner.FactsOf(proposal);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The rows of <paramref name="targets"/> that exist, with their current revision.</summary>
    private async Task<IReadOnlyList<(SyncHeldTarget Target, DateTimeOffset Revision)>> ExistingAsync(
        IReadOnlyCollection<SyncHeldTarget> targets, CancellationToken ct)
    {
        var found = new List<(SyncHeldTarget, DateTimeOffset)>();
        var features = IdsOf(targets, SyncHeldTargetKind.Feature);
        if (features.Count > 0)
        {
            found.AddRange((await db.Features.AsNoTracking().IgnoreQueryFilters()
                    .Where(f => features.Contains(f.Id)).Select(f => new { f.Id, f.UpdatedAt }).ToListAsync(ct))
                .Select(r => (new SyncHeldTarget(SyncHeldTargetKind.Feature, r.Id), r.UpdatedAt)));
        }

        var docs = IdsOf(targets, SyncHeldTargetKind.Document);
        if (docs.Count > 0)
        {
            found.AddRange((await db.Documents.AsNoTracking().IgnoreQueryFilters()
                    .Where(d => docs.Contains(d.Id)).Select(d => new { d.Id, d.UpdatedAt }).ToListAsync(ct))
                .Select(r => (new SyncHeldTarget(SyncHeldTargetKind.Document, r.Id), r.UpdatedAt)));
        }

        var recordings = IdsOf(targets, SyncHeldTargetKind.Recording);
        if (recordings.Count > 0)
        {
            found.AddRange((await db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
                    .Where(r => recordings.Contains(r.Id)).Select(r => new { r.Id, r.UpdatedAt }).ToListAsync(ct))
                .Select(r => (new SyncHeldTarget(SyncHeldTargetKind.Recording, r.Id), r.UpdatedAt)));
        }

        var points = IdsOf(targets, SyncHeldTargetKind.Point);
        if (points.Count > 0)
        {
            found.AddRange((await db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
                    .Where(p => points.Contains(p.Id)).Select(p => new { p.Id, p.UpdatedAt }).ToListAsync(ct))
                .Select(r => (new SyncHeldTarget(SyncHeldTargetKind.Point, r.Id), r.UpdatedAt)));
        }

        var maps = IdsOf(targets, SyncHeldTargetKind.RasterMap);
        if (maps.Count > 0)
        {
            found.AddRange((await db.RasterMaps.AsNoTracking().IgnoreQueryFilters()
                    .Where(m => maps.Contains(m.Id)).Select(m => new { m.Id, m.UpdatedAt }).ToListAsync(ct))
                .Select(r => (new SyncHeldTarget(SyncHeldTargetKind.RasterMap, r.Id), r.UpdatedAt)));
        }

        var pins = IdsOf(targets, SyncHeldTargetKind.RasterPin);
        if (pins.Count > 0)
        {
            found.AddRange((await db.RasterPins.AsNoTracking().IgnoreQueryFilters()
                    .Where(p => pins.Contains(p.Id)).Select(p => new { p.Id, p.UpdatedAt }).ToListAsync(ct))
                .Select(r => (new SyncHeldTarget(SyncHeldTargetKind.RasterPin, r.Id), r.UpdatedAt)));
        }

        return found;
    }

    /// <summary>
    /// Of existing rows, the ones the viewer may read, each by the rule its own listing reads it
    /// by: features by their visibility, documents by theirs and by what they hang on, recordings
    /// as trip logs, a scan through its recording, a raster map through its cave, and a pin through
    /// its map's cave and its own place.
    /// </summary>
    private async Task<HashSet<SyncHeldTarget>> ReadableAsync(
        IReadOnlyCollection<SyncHeldTarget> targets, AccessContext viewer, CancellationToken ct)
    {
        var readable = new HashSet<SyncHeldTarget>();
        var features = IdsOf(targets, SyncHeldTargetKind.Feature);
        if (features.Count > 0)
        {
            readable.UnionWith((await db.Features.AsNoTracking()
                    .Where(f => features.Contains(f.Id))
                    .VisibleTo(viewer, db.Features, db.FeatureSetMembers)
                    .Select(f => f.Id).ToListAsync(ct))
                .Select(id => new SyncHeldTarget(SyncHeldTargetKind.Feature, id)));
        }

        var docs = IdsOf(targets, SyncHeldTargetKind.Document);
        if (docs.Count > 0)
        {
            var query = await SyncDocuments.ReadableAsync(db, viewer, db.Documents.AsNoTracking().Where(d => docs.Contains(d.Id)), ct);
            readable.UnionWith((await query.Select(d => d.Id).ToListAsync(ct))
                .Select(id => new SyncHeldTarget(SyncHeldTargetKind.Document, id)));
        }

        var points = IdsOf(targets, SyncHeldTargetKind.Point);
        var pointRecordings = points.Count == 0
            ? []
            : await db.TripRecordingPoints.AsNoTracking().IgnoreQueryFilters()
                .Where(p => points.Contains(p.Id)).Select(p => new { p.Id, p.RecordingId }).ToListAsync(ct);
        var recordings = IdsOf(targets, SyncHeldTargetKind.Recording)
            .Concat(pointRecordings.Select(p => p.RecordingId)).Distinct().ToList();
        if (recordings.Count > 0)
        {
            var visible = (await db.TripRecordings.AsNoTracking()
                    .Where(r => recordings.Contains(r.Id))
                    .VisibleTo(viewer, AccessDomain.TripLogs)
                    .Select(r => r.Id).ToListAsync(ct))
                .ToHashSet();
            readable.UnionWith(visible.Select(id => new SyncHeldTarget(SyncHeldTargetKind.Recording, id)));
            readable.UnionWith(pointRecordings.Where(p => visible.Contains(p.RecordingId))
                .Select(p => new SyncHeldTarget(SyncHeldTargetKind.Point, p.Id)));
        }

        var pinIds = IdsOf(targets, SyncHeldTargetKind.RasterPin);
        var pins = pinIds.Count == 0
            ? []
            : await db.RasterPins.AsNoTracking().IgnoreQueryFilters()
                .Where(p => pinIds.Contains(p.Id)).Select(p => new { p.Id, p.RasterMapId, p.PlaceFeatureId })
                .ToListAsync(ct);
        var mapIds = IdsOf(targets, SyncHeldTargetKind.RasterMap)
            .Concat(pins.Select(p => p.RasterMapId)).Distinct().ToList();
        if (mapIds.Count > 0)
        {
            var maps = await db.RasterMaps.AsNoTracking().IgnoreQueryFilters()
                .Where(m => mapIds.Contains(m.Id)).Select(m => new { m.Id, m.CaveFeatureId }).ToListAsync(ct);
            var asked = maps.Select(m => m.CaveFeatureId).Concat(pins.Select(p => p.PlaceFeatureId)).Distinct().ToList();
            var visible = (await db.Features.AsNoTracking()
                    .Where(f => asked.Contains(f.Id))
                    .VisibleTo(viewer, db.Features, db.FeatureSetMembers)
                    .Select(f => f.Id).ToListAsync(ct))
                .ToHashSet();
            var readableMaps = maps.Where(m => visible.Contains(m.CaveFeatureId)).Select(m => m.Id).ToHashSet();
            readable.UnionWith(readableMaps.Select(id => new SyncHeldTarget(SyncHeldTargetKind.RasterMap, id)));
            readable.UnionWith(pins.Where(p => readableMaps.Contains(p.RasterMapId) && visible.Contains(p.PlaceFeatureId))
                .Select(p => new SyncHeldTarget(SyncHeldTargetKind.RasterPin, p.Id)));
        }

        return readable;
    }

    private static List<Guid> IdsOf(IEnumerable<SyncHeldTarget> targets, SyncHeldTargetKind kind) =>
        [.. targets.Where(t => t.Kind == kind).Select(t => t.Id).Distinct()];

    private static string WithoutPosition(string proposal)
    {
        if (JsonNode.Parse(proposal) is not JsonObject root)
        {
            return proposal;
        }

        foreach (var (name, members) in PositionMembers)
        {
            if (root[name] is JsonObject item)
            {
                foreach (var member in members)
                {
                    item.Remove(member);
                }
            }
        }

        return root.ToJsonString();
    }

    private static Guid? SetOf(ImportBatch batch)
    {
        using var options = JsonDocument.Parse(batch.Options);
        return options.RootElement.TryGetProperty("syncSetId", out var id) && id.TryGetGuid(out var setId)
            ? setId
            : null;
    }

    private static IReadOnlyList<HeldLineOutcome> Refuse(IReadOnlyList<ImportBatchItem> lines, string code, string detail) =>
        [.. lines.Select(l => new HeldLineOutcome(l.Id, false, null, code, detail))];
}
