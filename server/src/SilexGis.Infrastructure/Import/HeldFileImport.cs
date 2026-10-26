// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// One candidate of a file import that waits for a reviewer: what the account chose to do with it,
/// as its own review screen sent it.
/// </summary>
/// <param name="Key">The candidate's key in the source's own commit request.</param>
/// <param name="Name">What the candidate is called, for the reviewer.</param>
/// <param name="Decision">The account's decision about it, as its commit request carries it.</param>
/// <param name="Names">Existing map rows the decision names — a cave it attaches to, a parent.</param>
/// <param name="PositionProtected">
/// Whatever it creates is to be protected, so a position the decision gives is the true position
/// of a protected place-to-be and is shown to nobody but in the commit.
/// </param>
/// <param name="DependsOn">
/// Keys of candidates of the same import this one cannot be written without — a scan of a
/// recording cannot be recorded onto a trip the reviewer refused to create — so accepting it
/// accepts them too and rejecting one of them rejects it.
/// </param>
public sealed record HeldCandidate(
    string Key, string? Name, object Decision, IReadOnlyList<Guid> Names, bool PositionProtected,
    IReadOnlyList<string>? DependsOn = null);

/// <summary>Why a held candidate — or the whole held commit — could not be written.</summary>
public sealed record HeldCommitFailure(string Code, string Detail);

/// <summary>
/// The held batch a reviewer's acceptance writes a file import into, and what the source's commit
/// reports back per candidate.
/// </summary>
/// <remarks>
/// A commit given one of these creates no batch of its own: its lines are the held batch's lines,
/// which already exist; it opens no transaction, because the decision's own transaction holds it;
/// and it files its writes under the change set the decision opened. What it would have saved as
/// its lines it hands back here instead, keyed by candidate, so the held lines become the lines
/// undo works from.
/// </remarks>
public sealed class HeldCommit(Guid batchId)
{
    public Guid BatchId { get; } = batchId;

    public Dictionary<string, ImportBatchItem> Written { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, HeldCommitFailure> Failed { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Lines the commit wrote that stand for no candidate of their own — a cave a trip-sheet row
    /// named and created, the recording an archive's report was made from. They join the held
    /// batch as ordinary lines, so its undo takes them back with the rest.
    /// </summary>
    public List<ImportBatchItem> Extra { get; } = [];

    /// <summary>
    /// The trip an earlier acceptance of this same held batch already wrote, which this one goes
    /// on writing — the only case in which a recording already made into that trip's report is
    /// written onto it again rather than refused. Null for a first acceptance.
    /// </summary>
    public Guid? ContinuesTripLogId { get; set; }
}

/// <summary>
/// Holds a file import of an account under review: the commit request kept exactly as the account
/// sent it, and one waiting line per candidate it would write.
/// </summary>
/// <remarks>
/// The account still previews and chooses on its own screen; pressing commit is what waits. Its
/// review session and its file are left where they are — nothing of them is spent until the commit
/// runs — and a reviewer's acceptance runs the source's own commit over the request with every
/// candidate not accepted turned into a skip.
/// </remarks>
public static class HeldFileImport
{
    /// <summary>
    /// Records <paramref name="batch"/> as waiting with its candidates, tells the reviewers, and
    /// saves. Nothing the request proposes is written.
    /// </summary>
    public static async Task HoldAsync(
        SilexGisDbContext db,
        ImportBatch batch,
        string heldRequest,
        string section,
        IReadOnlyList<HeldCandidate> candidates,
        CancellationToken ct)
    {
        // A held batch with no waiting line can never be decided: the route refuses such a
        // confirmation, with its source's own code, before it gets here.
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("A file import is held only with something for a reviewer to decide.");
        }

        batch.ReviewState = ImportReviewState.Pending;
        batch.HeldAt = DateTimeOffset.UtcNow;
        batch.HeldRequest = heldRequest;
        db.ImportBatches.Add(batch);
        ImportBatchItem LineOf(HeldCandidate c) => new()
        {
            ImportBatchId = batch.Id,
            ReviewDecision = ImportReviewDecision.Pending,
            ReviewId = Guid.CreateVersion7(),
            ProposalSection = section,
            Proposal = ImportJson.Serialize(new HeldProposal(
                c.Key, c.Name, JsonSerializer.SerializeToElement(c.Decision, ImportJson.Options), c.Names, c.PositionProtected)),
        };

        // A line names what it depends on by the row id of that line, so the candidates nothing
        // depends on are saved first and the rest point at them.
        var first = candidates.Where(c => c.DependsOn is not { Count: > 0 })
            .ToDictionary(c => c.Key, LineOf, StringComparer.Ordinal);
        db.ImportBatchItems.AddRange(first.Values);
        await db.SaveChangesAsync(ct);
        db.ImportBatchItems.AddRange(candidates.Where(c => c.DependsOn is { Count: > 0 }).Select(c =>
        {
            var line = LineOf(c);
            line.DependsOnItemIds = [.. c.DependsOn!.Where(first.ContainsKey).Select(k => first[k].Id)];
            return line;
        }));
        await UploadReviewNotices.AwaitingAsync(db, batch, candidates.Count, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The candidate key a held line stands for; null when its proposal cannot be read.</summary>
    public static string? KeyOf(ImportBatchItem line) => Read(line)?.Key;

    internal static HeldProposal? Read(ImportBatchItem line)
    {
        if (line.Proposal is null)
        {
            return null;
        }

        try
        {
            return ImportJson.Deserialize<HeldProposal>(line.Proposal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What a held file-import line stores as its proposal.</summary>
    internal sealed record HeldProposal(
        string Key, string? Name, JsonElement Decision, IReadOnlyList<Guid> Names, bool PositionProtected);
}

/// <summary>
/// Review of one kind of held file import: its lines shown under the same read and position rules
/// as any held line, and accepted lines written by one run of the source's own commit.
/// </summary>
public abstract class HeldFileImportHandler(SilexGisDbContext db, FeatureProtection protection) : IHeldBatchHandler
{
    private const string Savepoint = "held_file_import";

    /// <summary>The request's context, which the source's commit writes through.</summary>
    protected SilexGisDbContext Db { get; } = db;

    /// <summary>Decision members that place something; never shown for a protected place-to-be.</summary>
    private static readonly string[] PositionMembers = ["position", "longitude", "latitude"];

    public abstract ImportSource Source { get; }

    public bool WritesWhole => true;

    /// <summary>A candidate is accepted whole: its decision is one choice, not a set of fields.</summary>
    public IReadOnlyList<string> FieldsOf(ImportBatchItem line) => [];

    /// <summary>
    /// Shown only when the viewer may read, at its exact position, every existing row the decision
    /// names — a candidate filed under a protected cave says where that cave is — and never with
    /// a position for something that is to be protected.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, HeldLineView>> ShowAsync(
        IReadOnlyList<ImportBatchItem> lines, AccessContext viewer, CancellationToken ct)
    {
        var proposals = lines.ToDictionary(l => l.Id, HeldFileImport.Read);
        var named = proposals.Values.Where(p => p is not null).SelectMany(p => p!.Names).Distinct().ToList();
        var readable = named.Count == 0
            ? []
            : await Db.Features.AsNoTracking()
                .Where(f => named.Contains(f.Id))
                .VisibleTo(viewer, Db.Features, Db.FeatureSetMembers)
                .Select(f => f.Id)
                .ToListAsync(ct);
        var placeable = readable.Count == 0 ? [] : await protection.ExactViewIdsAsync(viewer, readable, ct);

        var shown = new Dictionary<long, HeldLineView>();
        foreach (var (id, proposal) in proposals)
        {
            if (proposal is null || proposal.Names.Any(n => !placeable.Contains(n)))
            {
                shown[id] = new HeldLineView(null, false);
                continue;
            }

            var node = JsonSerializer.SerializeToNode(proposal, ImportJson.Options)!.AsObject();
            if (proposal.PositionProtected && node["decision"] is JsonObject decision)
            {
                foreach (var member in PositionMembers)
                {
                    decision.Remove(member);
                }
            }

            node.Remove("positionProtected");
            shown[id] = new HeldLineView(node.ToJsonString(ImportJson.Options), false);
        }

        return shown;
    }

    /// <summary>
    /// Runs the source's commit once over the accepted candidates, inside the decision's
    /// transaction. A refusal of the whole commit — the uploader lost the right to create, the
    /// file is gone — undoes whatever the attempt wrote and decides every line failed with it.
    /// </summary>
    public async Task<IReadOnlyList<HeldLineOutcome>> ApplyAsync(
        ImportBatch batch, IReadOnlyList<ImportBatchItem> lines, AccessContext uploader, bool overCurrent, CancellationToken ct)
    {
        var keys = lines.ToDictionary(l => l.Id, l => HeldFileImport.KeyOf(l) ?? string.Empty);
        var transaction = Db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A held file import is written inside the decision's transaction.");
        var into = new HeldCommit(batch.Id);
        await transaction.CreateSavepointAsync(Savepoint, ct);
        var refused = await CommitAsync(batch, keys.Values.Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal), into, uploader, ct);
        if (refused is not null)
        {
            await transaction.RollbackToSavepointAsync(Savepoint, ct);
            Db.ChangeTracker.Clear();
        }
        else
        {
            await transaction.ReleaseSavepointAsync(Savepoint, ct);

            // What the commit wrote beyond one line per candidate joins the batch as it would have
            // in a batch that landed at once, and is counted with it.
            if (into.Extra.Count > 0)
            {
                foreach (var extra in into.Extra)
                {
                    extra.ImportBatchId = batch.Id;
                }

                Db.ImportBatchItems.AddRange(into.Extra);
                await Db.SaveChangesAsync(ct);
                var created = into.Extra.Count(i => i.Action == ImportDecisionAction.Create);
                var attached = into.Extra.Count - created;
                await Db.ImportBatches.Where(b => b.Id == batch.Id).ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.CreatedCount, b => b.CreatedCount + created)
                    .SetProperty(b => b.AttachedCount, b => b.AttachedCount + attached), ct);
            }
        }

        return [.. lines.Select(line =>
        {
            var key = keys[line.Id];
            var failure = into.Failed.GetValueOrDefault(key) ?? refused;
            if (failure is not null)
            {
                return new HeldLineOutcome(line.Id, false, null, failure.Code, failure.Detail);
            }

            return key.Length == 0
                ? new HeldLineOutcome(line.Id, false, null, UnreadableCode, "The held line cannot be read.")
                : new HeldLineOutcome(line.Id, true, into.Written.GetValueOrDefault(key));
        })];
    }

    /// <summary>A held line whose proposal this build cannot read.</summary>
    public const string UnreadableCode = "review.proposal_unreadable";

    /// <summary>
    /// Runs the source's own commit of <paramref name="batch"/>'s held request with the
    /// uploader's access decisions, every candidate outside <paramref name="accepted"/> turned
    /// into a skip, written into <paramref name="into"/>. Answers the refusal of the whole commit,
    /// if it was refused, having asked again everything the commit route asks before it commits.
    /// </summary>
    protected abstract Task<HeldCommitFailure?> CommitAsync(
        ImportBatch batch, IReadOnlySet<string> accepted, HeldCommit into, AccessContext uploader, CancellationToken ct);
}
