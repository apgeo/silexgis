// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
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
}

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

            results.Add(applied.Result);
            if (applied.Item is not null)
            {
                lines.Add(applied.Item);
            }

            SyncUploadTally.Count(run.Batch, applied.Result.Status);

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
        + (request.TripPoints?.Count ?? 0);
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
