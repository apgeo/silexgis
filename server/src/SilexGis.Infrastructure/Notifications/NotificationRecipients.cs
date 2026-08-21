// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Who, of a set of candidates, may actually be told about one protected row.
/// </summary>
/// <remarks>
/// <para>
/// The one rule every notification about a protected row obeys: nobody is told about something
/// they could not open. A message to somebody the row is closed to would be useless to them and
/// would still hand them whatever the message says about it — a title, a date, the fact that it
/// exists at all — outside every filter the read paths apply. So the recipient's own right to read
/// is decided here, from their own access as it stands now, rather than assumed from their being
/// named on the row.
/// </para>
/// <para>
/// It lives here rather than beside any one producer because the producers are not all requests.
/// A background pass that mails people about rows nobody is looking at needs exactly this rule and
/// has no request context to borrow — and a second copy of it would be a second answer to the
/// question of who may be told, free to drift from this one.
/// </para>
/// <para>
/// One context is resolved per candidate, so the cost is one round trip each. That is affordable
/// because the candidate sets are the people on one activity, and it is the only honest shape: an
/// access context belongs to one account and cannot be borrowed from the person who caused the
/// change, who is usually the one person who can read everything.
/// </para>
/// </remarks>
public static class NotificationRecipients
{
    /// <summary>
    /// The candidates whose own access lets them read the row, in the order given, without
    /// duplicates and without <paramref name="excluding"/>.
    /// </summary>
    /// <param name="excluding">
    /// Whoever caused the change, when there is one. Nobody is told about their own action.
    /// </param>
    public static async Task<List<Guid>> WhoMayReadAsync(
        SilexGisDbContext db,
        IAccessService access,
        IProtectedEntity row,
        IEnumerable<Guid> candidateUserIds,
        Guid? excluding,
        CancellationToken ct = default)
    {
        var candidates = Narrow(candidateUserIds, excluding);
        var contexts = new Dictionary<Guid, AccessContext>(candidates.Count);
        foreach (var candidate in candidates)
        {
            contexts[candidate] = await AccessContextResolver.ResolveAsync(db, candidate, ct);
        }

        return await WhoMayReadAsync(access, row, candidates, contexts, ct);
    }

    /// <summary>
    /// The same rule against contexts the caller has already resolved, for a caller asking about
    /// several rows in turn. Resolving a context costs several round trips and depends on nothing
    /// but the account, so a caller weighing the same people against six rows resolves them once
    /// and asks six times — while the rule about who may be told still lives only here.
    /// </summary>
    /// <param name="candidateContexts">
    /// A context for every candidate. A candidate with none is left out: the caller decides who is
    /// asked about, and somebody they never resolved was never a candidate.
    /// </param>
    public static async Task<List<Guid>> WhoMayReadAsync(
        IAccessService access,
        IProtectedEntity row,
        IEnumerable<Guid> candidateUserIds,
        IReadOnlyDictionary<Guid, AccessContext> candidateContexts,
        CancellationToken ct = default)
    {
        var recipients = new List<Guid>();
        foreach (var candidate in Narrow(candidateUserIds, null))
        {
            if (candidateContexts.TryGetValue(candidate, out var theirs)
                && (await access.DecideAsync(theirs, AccessAction.Read, row, ct)).Allowed)
            {
                recipients.Add(candidate);
            }
        }

        return recipients;
    }

    // Distinct is load-bearing rather than defensive: one person can be reached by several of the
    // lists a caller unions together — named on the roster and asked on the invitation list, or
    // holding as many places on a trip as they did jobs — and one change is one message however
    // many times the same account turns up in it.
    private static List<Guid> Narrow(IEnumerable<Guid> candidateUserIds, Guid? excluding) =>
        [.. candidateUserIds
            .Where(id => excluding is not { } actor || id != actor)
            .Distinct()];
}
