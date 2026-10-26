// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// What one upload's sections share while they run: the database, who is uploading, the set the
/// upload goes into, and the batch everything it writes is recorded under.
/// </summary>
internal sealed class SyncUploadRun
{
    public required SilexGisDbContext Db { get; init; }

    public required AccessContext Access { get; init; }

    public required SyncSet Set { get; init; }

    public required ImportBatch Batch { get; init; }

    /// <summary>
    /// Set while the upload of an account under review is checked: every item that would be
    /// written is kept here as a proposal instead, and what was written to find that out is rolled
    /// back once the check is over. Null for an upload nobody reviews.
    /// </summary>
    public SyncHoldRecorder? Hold { get; init; }
}

/// <summary>
/// What a section's first step decided about one item: either the answer is already known and
/// nothing is to be written (a refusal, a conflict, a resend that changes nothing), or there is a
/// proposal for the second step to write.
/// </summary>
internal sealed record SyncResolution<TProposal>(SyncUploadRowResultDto? Settled, TProposal? Proposal)
    where TProposal : class
{
    public static SyncResolution<TProposal> Answer(SyncUploadRowResultDto result) => new(result, null);

    public static SyncResolution<TProposal> Write(TProposal proposal) => new(null, proposal);
}

/// <summary>
/// What a section's second step did with one proposal: the answer for the item, and the
/// provenance line to record — null for an item that changed nothing, so that undoing the batch
/// does not reach anything it never touched.
/// </summary>
internal readonly record struct SyncApplied(SyncUploadRowResultDto Result, ImportBatchItem? Item);

/// <summary>Everything one section answered and recorded.</summary>
internal sealed record SyncSectionOutcome(
    IReadOnlyList<SyncUploadRowResultDto> Results,
    IReadOnlyList<ImportBatchItem> Items)
{
    public static readonly SyncSectionOutcome Empty = new([], []);
}

/// <summary>
/// One kind of thing an upload carries — feature rows, phone users, and the kinds that follow —
/// as the handler sees it: a name that is both the request's array and the answer's, and a run
/// over the items in the order the device gave them.
/// </summary>
internal interface ISyncUploadSection
{
    /// <summary>The member name of this section's array, in the request and in the answer alike.</summary>
    string Name { get; }

    Task<SyncSectionOutcome> RunAsync(SyncUploadRun run, SyncUploadRequest request, CancellationToken ct);

    /// <summary>
    /// Writes a proposal this section kept earlier, from its stored form — the second step alone,
    /// by the same code an upload nobody holds runs.
    /// </summary>
    Task<SyncApplied> ApplyHeldAsync(SyncUploadRun run, string proposal, CancellationToken ct);

    /// <summary>
    /// Writes a held proposal a reviewer accepted: both steps again, on the item as the phone sent
    /// it, so every check is made once more against the registry as it stands now and with the
    /// rights the run carries — the uploader's.
    /// </summary>
    /// <param name="fields">For an update, the fields the reviewer accepted; null for all of them.</param>
    /// <param name="overCurrent">
    /// Whether a row that changed since the phone read it is written over its current state. When
    /// false, such a row writes nothing and is answered as changed since.
    /// </param>
    Task<SyncHeldAcceptance> AcceptHeldAsync(
        SyncUploadRun run, string proposal, IReadOnlyList<string>? fields, bool overCurrent, CancellationToken ct);

    /// <summary>
    /// What showing a kept proposal to somebody depends on: the existing row it changes, the
    /// revision it was checked against, the other rows it names, and whether it carries a position.
    /// </summary>
    SyncHeldFacts FactsOf(string proposal);
}

/// <summary>The kinds of row a held proposal can change or name.</summary>
internal enum SyncHeldTargetKind
{
    Feature,
    Document,
    Recording,
    Point,
}

/// <summary>One row a held proposal changes or names.</summary>
internal readonly record struct SyncHeldTarget(SyncHeldTargetKind Kind, Guid Id);

/// <summary>What a held proposal says about the rows around it, for whoever is shown it.</summary>
/// <param name="Edited">The existing row an update or removal changes; null for a create.</param>
/// <param name="BaseRevision">The revision of <paramref name="Edited"/> the proposal was checked against.</param>
/// <param name="Mentions">The other rows it cannot be written without: its container, document, recording, place.</param>
/// <param name="CarriesPosition">Whether the proposal holds a point, altitude and the like.</param>
internal sealed record SyncHeldFacts(
    SyncHeldTarget? Edited, DateTimeOffset? BaseRevision, IReadOnlyList<SyncHeldTarget> Mentions, bool CarriesPosition)
{
    public static readonly SyncHeldFacts None = new(null, null, [], false);
}

/// <summary>What accepting one held proposal did.</summary>
/// <param name="ChangedSince">
/// The row changed on the server after the phone read it, and the reviewer did not say to write
/// over it: nothing was written.
/// </param>
internal readonly record struct SyncHeldAcceptance(SyncApplied Applied, bool ChangedSince);

/// <summary>
/// A section in its two steps. <see cref="ResolveAsync"/> does everything that decides — the
/// input checks, finding the target, the caller's rights, the withhold gate, the revision
/// arbitration — and writes nothing; <see cref="ApplyAsync"/> writes what was decided.
/// </summary>
/// <remarks>
/// The split is what lets an upload be held for somebody to look at before it lands: the first
/// step can run now and its proposal be kept, and the second step run later, by the same code
/// that would have run without the hold. An upload nobody holds runs both, item by item.
/// </remarks>
internal interface ISyncUploadSection<in TItem, TProposal> : ISyncUploadSection
    where TProposal : class
{
    Task<SyncResolution<TProposal>> ResolveAsync(SyncUploadRun run, TItem item, CancellationToken ct);

    Task<SyncApplied> ApplyAsync(SyncUploadRun run, TProposal proposal, CancellationToken ct);
}

/// <summary>
/// The loop every section runs the same way, so the rules that make an upload safe to apply
/// item by item have one home rather than one copy per kind.
/// </summary>
internal abstract class SyncUploadSection<TItem, TProposal> : ISyncUploadSection<TItem, TProposal>
    where TProposal : class
{
    public abstract string Name { get; }

    /// <summary>This section's items in the request, or null when the device sent none.</summary>
    protected abstract IReadOnlyList<TItem>? ItemsOf(SyncUploadRequest request);

    /// <summary>
    /// Once per upload, before the first item: the questions worth asking for all items at once
    /// rather than one at a time (what the caller may not place, the vocabularies).
    /// </summary>
    protected virtual Task PrepareAsync(SyncUploadRun run, IReadOnlyList<TItem> items, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Once per upload, after the last item: anything the answers can only learn once every write
    /// has landed — a revision the database stamped, for one.
    /// </summary>
    protected virtual Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct) =>
        Task.FromResult(results);

    public abstract Task<SyncResolution<TProposal>> ResolveAsync(
        SyncUploadRun run, TItem item, CancellationToken ct);

    public abstract Task<SyncApplied> ApplyAsync(SyncUploadRun run, TProposal proposal, CancellationToken ct);

    /// <summary>
    /// The ids of other rows a proposal cannot be written without — its container, its document,
    /// its recording. A held proposal naming a row that is itself a held create depends on it.
    /// </summary>
    protected virtual IEnumerable<Guid> Refers(TProposal proposal) => [];

    /// <summary>
    /// The revision the proposal's row has now, before anything is written: what a held row is
    /// answered with, so the device keeps arbitrating against the row as it stands. Null for a
    /// create.
    /// </summary>
    protected virtual DateTimeOffset? StandingRevision(TProposal proposal) => null;

    /// <summary>
    /// How a held proposal is known: the id a resend of the same row is matched by and a later
    /// row refers to it by, and the id the device is answered with. Both are the written row's id
    /// unless the section's rows have no identity of their own the device chose.
    /// </summary>
    protected virtual (Guid RowId, Guid AnswerId) HeldIdentity(TProposal proposal, SyncUploadRowResultDto written) =>
        (written.Id, written.Id);

    /// <summary>
    /// The staged upload whose bytes the proposal would adopt when it is written, if any — kept
    /// while the proposal waits rather than left to its own, shorter expiry.
    /// </summary>
    protected virtual Guid? HeldUpload(TProposal proposal) => null;

    public Task<SyncApplied> ApplyHeldAsync(SyncUploadRun run, string proposal, CancellationToken ct) =>
        ApplyAsync(run, ReadHeld(proposal), ct);

    public SyncHeldFacts FactsOf(string proposal) => Facts(ReadHeld(proposal));

    /// <summary>What a proposal of this section changes and names; nothing by default.</summary>
    protected virtual SyncHeldFacts Facts(TProposal proposal) => SyncHeldFacts.None;

    /// <summary>The item as the phone sent it, which a held proposal carries whole.</summary>
    protected abstract TItem ItemOf(TProposal proposal);

    /// <summary>
    /// The item arbitrated against <paramref name="revision"/> instead of the revision the phone
    /// read — what writing a held change over the row's current state means. Null for a section
    /// whose rows carry no revision.
    /// </summary>
    protected virtual TItem? Rebased(TItem item, DateTimeOffset revision) => default;

    /// <summary>
    /// The proposal cut down to the fields a reviewer accepted. A section that cannot accept part
    /// of a change takes no field list; the review routes refuse one before it gets here.
    /// </summary>
    protected virtual TProposal Narrowed(TProposal proposal, IReadOnlyList<string> fields) => proposal;

    public async Task<SyncHeldAcceptance> AcceptHeldAsync(
        SyncUploadRun run, string proposal, IReadOnlyList<string>? fields, bool overCurrent, CancellationToken ct)
    {
        // Resolved again rather than applied from the kept proposal: rights, the withhold gate,
        // the container and the vocabularies are all asked again, now, with the uploader's access
        // as it is today. Whatever the first check found is no evidence about the registry a
        // week later, and a review must never land what the account could not write itself.
        var item = ItemOf(ReadHeld(proposal));
        await PrepareAsync(run, [item], ct);
        var resolution = await ResolveAsync(run, item, ct);
        if (resolution.Settled is { Status: SyncRowStatus.Conflict, Revision: { } current }
            && Rebased(item, current) is { } rebased)
        {
            if (!overCurrent)
            {
                return new SyncHeldAcceptance(new SyncApplied(resolution.Settled, null), ChangedSince: true);
            }

            resolution = await ResolveAsync(run, rebased, ct);
        }

        if (resolution.Proposal is not { } fresh)
        {
            return new SyncHeldAcceptance(new SyncApplied(resolution.Settled!, null), ChangedSince: false);
        }

        return new SyncHeldAcceptance(
            await ApplyAsync(run, fields is null ? fresh : Narrowed(fresh, fields), ct), ChangedSince: false);
    }

    private TProposal ReadHeld(string proposal) =>
        JsonSerializer.Deserialize<TProposal>(proposal, SyncUploadJson.Options)
            ?? throw new InvalidOperationException($"A held {Name} proposal could not be read back.");

    public async Task<SyncSectionOutcome> RunAsync(SyncUploadRun run, SyncUploadRequest request, CancellationToken ct)
    {
        var items = ItemsOf(request);
        if (items is null || items.Count == 0)
        {
            return SyncSectionOutcome.Empty;
        }

        await PrepareAsync(run, items, ct);

        var results = new List<SyncUploadRowResultDto>(items.Count);
        var lines = new List<ImportBatchItem>();
        foreach (var item in items)
        {
            var resolution = await ResolveAsync(run, item, ct);
            var applied = resolution.Proposal is { } proposal
                ? await ApplyAsync(run, proposal, ct)
                : new SyncApplied(resolution.Settled!, null);

            // Held: the item was written only so the items after it are checked against it, and is
            // answered as a proposal kept for review. The write itself is rolled back with the
            // rest of the check, so its provenance line would point at nothing.
            var result = applied.Result;
            if (run.Hold is { } hold && resolution.Proposal is { } kept && SyncUploadTally.IsWritten(result.Status))
            {
                result = await hold.KeepAsync(
                    run, Name, HeldIdentity(kept, result), JsonSerializer.Serialize(kept, SyncUploadJson.Options),
                    [.. Refers(kept)], StandingRevision(kept),
                    applied.Item?.Action ?? ImportDecisionAction.Create, HeldUpload(kept), ct);
            }
            else if (applied.Item is not null)
            {
                lines.Add(applied.Item);
            }

            results.Add(result);
            SyncUploadTally.Count(run.Batch, result.Status);

            // Item by item, inside the batch's own transaction. Items arrive in the order they
            // depend on each other — a cave before its entrances, an area before the places
            // inside it — and the checks the next item runs are database questions: whether its
            // container exists, whether this caller may read it, what its ancestry is. An item
            // still sitting unsaved in the tracker answers none of them, so a cave and its
            // entrance sent together would have the entrance refused for having no cave.
            //
            // An item that wrote nothing leaves whatever it had begun to change behind it, and
            // none of that may reach the database: the refusal is the whole of what happened to
            // it. Clearing the tracker is safe only because every earlier item was saved as it
            // landed — which is why no section may keep unsaved work across items.
            if (SyncUploadTally.IsWritten(applied.Result.Status))
            {
                await run.Db.SaveChangesAsync(ct);
            }
            else
            {
                run.Db.ChangeTracker.Clear();
            }
        }

        return new SyncSectionOutcome(await FinishAsync(run, results, ct), lines);
    }
}

/// <summary>
/// Whether the revision a device sends back is the one this server stamped — the one comparison
/// every section arbitrates by. Compared at the precision the database keeps rather than the
/// precision .NET keeps: the value goes out with tick resolution and comes back off a column that
/// stores microseconds, so an exact comparison would report a conflict against a row nobody had
/// touched.
/// </summary>
internal static class SyncRevisions
{
    public static bool Same(DateTimeOffset stored, DateTimeOffset claimed) =>
        stored.UtcTicks / 10 == claimed.UtcTicks / 10;
}

/// <summary>
/// How item answers are counted — into the batch's own counters and into the answer's
/// <c>written</c> and <c>refused</c> — stated once, so the live answer, a resend's answer and the
/// batch's record cannot count the same upload three different ways.
/// </summary>
internal static class SyncUploadTally
{
    public static bool IsWritten(SyncRowStatus status) =>
        status is SyncRowStatus.Created or SyncRowStatus.Updated or SyncRowStatus.Deleted;

    public static bool IsRefused(SyncRowStatus status) =>
        status is SyncRowStatus.Conflict or SyncRowStatus.Rejected;

    public static void Count(ImportBatch batch, SyncRowStatus status)
    {
        switch (status)
        {
            case SyncRowStatus.Created:
                batch.CreatedCount++;
                break;
            case SyncRowStatus.Updated:
            case SyncRowStatus.Deleted:
                batch.AttachedCount++;
                break;
            case SyncRowStatus.Held:
                // Nothing was written, and nothing was skipped either: the lines waiting for a
                // reviewer are the batch's count of what it holds.
                break;
            default:
                batch.SkippedCount++;
                break;
        }
    }
}

/// <summary>
/// The sections an upload is applied in, and the size of an upload across all of them.
/// </summary>
internal static class SyncUploadSections
{
    /// <summary>
    /// The name of the feature-row section, which is also the only array of a request from before
    /// an upload had sections.
    /// </summary>
    public const string Rows = "rows";

    /// <summary>The phone-user section.</summary>
    public const string People = "people";

    /// <summary>The photos and documents a phone created, changed or removed.</summary>
    public const string Documents = "documents";

    /// <summary>Where a phone hung its photos and documents, and where it took them down.</summary>
    public const string DocumentLinks = "documentLinks";

    /// <summary>The phone's trip recordings.</summary>
    public const string TripRecordings = "tripRecordings";

    /// <summary>The scans on the phone's trip recordings, applied after the recordings.</summary>
    public const string TripPoints = "tripPoints";

    /// <summary>The raster maps of the phone, each with its picture.</summary>
    public const string RasterMaps = "rasterMaps";

    /// <summary>The places the phone pinned on those maps, applied last of all.</summary>
    public const string RasterPins = "rasterPins";

    /// <summary>
    /// Every section, in the fixed order an upload is applied in. A later section may refer to
    /// what an earlier one created — a document link to a document sent beside it, a trip point
    /// to its recording — so the order is the dependency order and never the order the arrays
    /// happen to sit in the request. The upload and the acceptance of a held upload both build
    /// their sections here, so the two cannot apply in different orders.
    /// </summary>
    public static ISyncUploadSection[] InApplyOrder(
        FeatureWriteService writer,
        FeatureProtection protection,
        IAccessService access,
        SyncSettings settings,
        DocumentWriteService documents,
        UploadIngestService ingest,
        ContentIntake intake) =>
    [
        new SyncRowsSection(writer, protection, access),
        new SyncPeopleSection(settings),
        new SyncDocumentsSection(documents, ingest, intake, access),
        new SyncRasterMapsSection(documents, intake, access),
        new SyncTripRecordingsSection(access),
        new SyncTripPointsSection(access),
        new SyncDocumentLinksSection(protection, access),
        new SyncRasterPinsSection(access),
    ];

    /// <summary>
    /// How many items an upload carries, over every section together. The announced ceiling is a
    /// bound on one transaction's work, and that work is the same whichever array an item sits
    /// in — so a device cannot pass a large upload by spreading it over several of them.
    /// </summary>
    public static int ItemCount(SyncUploadRequest request) =>
        (request.Rows?.Count ?? 0)
        + (request.People?.Count ?? 0)
        + (request.Documents?.Count ?? 0)
        + (request.DocumentLinks?.Count ?? 0)
        + (request.TripRecordings?.Count ?? 0)
        + (request.TripPoints?.Count ?? 0)
        + (request.RasterMaps?.Count ?? 0)
        + (request.RasterPins?.Count ?? 0);

    /// <summary>
    /// The ids of the rows an upload carries, every section together, in the identity each section
    /// answers them by.
    /// </summary>
    public static IReadOnlySet<Guid> RowIds(SyncUploadRequest request) =>
        new HashSet<Guid>(
            (request.Rows ?? []).Select(r => r.Id)
                .Concat((request.People ?? []).Select(p => p.DeviceUserId))
                .Concat((request.Documents ?? []).Select(d => d.Id))
                .Concat((request.DocumentLinks ?? []).Select(SyncDocumentLinksSection.RowIdOf))
                .Concat((request.TripRecordings ?? []).Select(r => r.Id))
                .Concat((request.TripPoints ?? []).Select(p => p.Id))
                .Concat((request.RasterMaps ?? []).Select(m => m.Id))
                .Concat((request.RasterPins ?? []).Select(p => p.Id)));
}

/// <summary>
/// The decisions an upload recorded, per section, as kept on its import batch so a resend can be
/// answered without applying anything again.
/// </summary>
/// <remarks>
/// <para>
/// Written as one object with a member per section that had items. Read back from that shape
/// and from the older one, a bare array of feature-row answers, which every upload recorded
/// before the request had more than one array — a batch recorded that way is still a batch a
/// phone may resend, so it must still be answered.
/// </para>
/// <para>
/// Decisions only, never positions or anything else a later reader might no longer be entitled
/// to: whatever an answer carries beside the decisions is worked out afresh each time it is given.
/// </para>
/// </remarks>
internal static class SyncUploadRecord
{
    public static string Write(IReadOnlyList<(string Section, IReadOnlyList<SyncUploadRowResultDto> Results)> sections)
    {
        var record = new JsonObject();
        foreach (var (section, results) in sections)
        {
            if (results.Count > 0)
            {
                record[section] = JsonSerializer.SerializeToNode(results, SyncUploadJson.Options);
            }
        }

        return record.ToJsonString(SyncUploadJson.Options);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<SyncUploadRowResultDto>> Read(string stored)
    {
        var node = JsonNode.Parse(stored);
        var sections = new Dictionary<string, IReadOnlyList<SyncUploadRowResultDto>>(StringComparer.Ordinal);
        switch (node)
        {
            case JsonArray rows:
                sections[SyncUploadSections.Rows] = rows.Deserialize<List<SyncUploadRowResultDto>>(SyncUploadJson.Options) ?? [];
                break;
            case JsonObject record:
                foreach (var (section, value) in record)
                {
                    sections[section] = value?.Deserialize<List<SyncUploadRowResultDto>>(SyncUploadJson.Options) ?? [];
                }

                break;
        }

        return sections;
    }

    public static IReadOnlyList<SyncUploadRowResultDto> Of(
        IReadOnlyDictionary<string, IReadOnlyList<SyncUploadRowResultDto>> sections, string section) =>
        sections.TryGetValue(section, out var results) ? results : [];
}
