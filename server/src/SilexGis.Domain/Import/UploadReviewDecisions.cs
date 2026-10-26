// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import;

/// <summary>One held line as the review rules see it: its number, the held lines it cannot be
/// written without (a link needs both its document and the place it hangs on), and where it
/// stands.</summary>
public readonly record struct HeldLine(long Id, IReadOnlyList<long> DependsOn, ImportReviewDecision? Decision);

/// <summary>
/// What a reviewer's decision reaches beyond the lines it names, and where a batch stands once its
/// lines are decided — the rules the review routes, the queue and the phone's reviews stream all
/// read, stated once.
/// </summary>
/// <remarks>
/// <para>
/// A held line can depend on another held line: a place under a cave that is itself waiting, a
/// link to a held document, a scan on a held recording. Accepting the place without the cave would
/// write a place under nothing, so accepting a line accepts the waiting lines it depends on, before
/// it. Rejecting the cave leaves the place with nothing to hang under, so rejecting a line rejects
/// every waiting line that depends on it.
/// </para>
/// <para>
/// Only waiting lines are pulled in or cascaded to. A dependency that has already been accepted is
/// written and needs nothing more; one that failed makes the dependent unwritable, which is the
/// dependent's own outcome when it is accepted, not a reason to reach further.
/// </para>
/// <para>
/// A reviewer may change their mind about a rejection while its bytes are still kept. A rejected
/// line named as <c>reopenable</c> is accepted like a waiting one, and pulls in the rejected lines
/// it depends on that are reopenable too — a place whose cave was rejected with it is accepted
/// with its cave.
/// </para>
/// </remarks>
public static class UploadReviewDecisions
{
    /// <summary>
    /// The waiting lines accepting <paramref name="chosen"/> writes, each after every line it
    /// depends on, ties in the order the lines were kept.
    /// </summary>
    public static IReadOnlyList<long> AcceptOrder(
        IReadOnlyCollection<HeldLine> lines, IEnumerable<long> chosen, IReadOnlySet<long>? reopenable = null)
    {
        var byId = lines.ToDictionary(l => l.Id);
        var ordered = new List<long>();
        var placed = new HashSet<long>();
        var visiting = new HashSet<long>();

        void Visit(long id)
        {
            if (placed.Contains(id) || !byId.TryGetValue(id, out var line)
                || !(line.Decision == ImportReviewDecision.Pending
                    || (line.Decision == ImportReviewDecision.Rejected && reopenable?.Contains(id) == true))
                || !visiting.Add(id))
            {
                return;
            }

            foreach (var on in line.DependsOn)
            {
                Visit(on);
            }

            visiting.Remove(id);
            if (placed.Add(id))
            {
                ordered.Add(id);
            }
        }

        foreach (var id in chosen.Distinct().Order())
        {
            Visit(id);
        }

        return ordered;
    }

    /// <summary>
    /// The waiting lines rejecting <paramref name="chosen"/> rejects: those lines, and every
    /// waiting line that depends on one of them, however far down.
    /// </summary>
    public static IReadOnlyList<long> RejectReach(IReadOnlyCollection<HeldLine> lines, IEnumerable<long> chosen)
    {
        var dependents = lines
            .Where(l => l.Decision == ImportReviewDecision.Pending)
            .SelectMany(l => l.DependsOn.Select(on => (On: on, l.Id)))
            .ToLookup(x => x.On, x => x.Id);
        var pending = lines.Where(l => l.Decision == ImportReviewDecision.Pending).Select(l => l.Id).ToHashSet();
        var reached = new HashSet<long>();
        var queue = new Queue<long>(chosen.Where(pending.Contains));
        while (queue.TryDequeue(out var id))
        {
            if (!reached.Add(id))
            {
                continue;
            }

            foreach (var next in dependents[id])
            {
                queue.Enqueue(next);
            }
        }

        return [.. reached.Order()];
    }

    /// <summary>
    /// Where a held batch stands, read off its lines: waiting while any line waits; otherwise
    /// accepted when nothing a reviewer decided went any other way, rejected when nothing was
    /// accepted, and partly accepted in between. Superseded lines were never the reviewer's to
    /// decide and count for neither side, so a batch every line of which the phone replaced later
    /// stands as rejected — nothing of it will ever be written.
    /// </summary>
    public static ImportReviewState StateOf(IEnumerable<ImportReviewDecision?> decisions)
    {
        var accepted = 0;
        var refused = 0;
        foreach (var decision in decisions)
        {
            switch (decision)
            {
                case ImportReviewDecision.Pending:
                    return ImportReviewState.Pending;
                case ImportReviewDecision.Accepted:
                    accepted++;
                    break;
                case ImportReviewDecision.Rejected:
                case ImportReviewDecision.Failed:
                    refused++;
                    break;
            }
        }

        return accepted == 0 ? ImportReviewState.Rejected
            : refused == 0 ? ImportReviewState.Accepted
            : ImportReviewState.PartlyAccepted;
    }
}
