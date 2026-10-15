// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// The one rule for access rows when what they name is removed for good: they go in the same
/// unit of work, loaded and removed through the change tracker so that the audit trail records
/// every rule that went and whom it had concerned.
/// </summary>
/// <remarks>
/// <para>
/// A rule whose object is gone grants and denies nothing, but it does not look like that: it is
/// listed on every surface that shows rules by subject, it is counted in a ruleset, and nobody
/// can withdraw it from its object's own permissions tab because there is no object. So it goes
/// with the object.
/// </para>
/// <para>
/// Every row removed through the change tracker reaches this through
/// <see cref="AccessAnchorCleanupInterceptor"/>, so a delete handler does nothing to earn it. A
/// statement that deletes rows by itself — <c>ExecuteDelete</c>, raw SQL — never passes through
/// the interceptor, and its caller has to ask here first, then save, then delete; an architecture
/// test keeps a list of the places that do.
/// </para>
/// <para>
/// Refusing a delete is a separate matter and stays with the slice that owns the delete: a club
/// with a deny anchored on it, or a feature set or a cabinet a rule still scopes to, is refused
/// before anything is removed, because silently cancelling a deny is the one side effect the
/// model never allows. What reaches this rule is a delete that is going ahead.
/// </para>
/// </remarks>
public static class AccessAnchorCleanup
{
    /// <summary>
    /// Marks for removal every access entry and permission-group member that names one of these
    /// rows, or a row a database cascade from them would remove. The caller saves.
    /// </summary>
    public static Task RemoveRowsNamingAsync(
        SilexGisDbContext db, Type deleted, IEnumerable<Guid> ids, CancellationToken ct = default) =>
        RemoveRowsNamingAsync(db, new Dictionary<Type, HashSet<Guid>> { [deleted] = [.. ids] }, ct);

    /// <inheritdoc cref="RemoveRowsNamingAsync(SilexGisDbContext, Type, IEnumerable{Guid}, CancellationToken)"/>
    public static async Task RemoveRowsNamingAsync(
        SilexGisDbContext db, IReadOnlyDictionary<Type, HashSet<Guid>> deleted, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(deleted);

        var reached = new Dictionary<Type, HashSet<Guid>>();
        var pending = new Queue<(Type Type, Guid[] Ids)>(deleted.Select(d => (d.Key, d.Value.ToArray())));
        while (pending.TryDequeue(out var step))
        {
            var fresh = Add(reached, step.Type, step.Ids);
            if (fresh.Length == 0)
            {
                continue;
            }

            foreach (var cascade in AccessAnchors.Cascades.Where(c => c.Principal == step.Type))
            {
                var dependents = await cascade.Dependents(db, fresh).ToArrayAsync(ct);
                if (dependents.Length > 0)
                {
                    pending.Enqueue((cascade.Dependent, dependents));
                }
            }
        }

        foreach (var (type, ids) in reached)
        {
            var array = ids.ToArray();
            foreach (var arm in AccessAnchors.Arms.Where(a => a.Targets.Contains(type)))
            {
                if (arm.Column == AccessReferenceColumn.PermissionMember)
                {
                    db.PermissionGroupMembers.RemoveRange(await MembersNaming(db, arm, array).ToListAsync(ct));
                }
                else
                {
                    db.AccessEntries.RemoveRange(await EntriesNaming(db, arm, array).ToListAsync(ct));
                }
            }
        }
    }

    /// <summary>
    /// The synchronous twin, for a synchronous save. Same walk, same queries.
    /// </summary>
    public static void RemoveRowsNaming(SilexGisDbContext db, IReadOnlyDictionary<Type, HashSet<Guid>> deleted)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(deleted);

        var reached = new Dictionary<Type, HashSet<Guid>>();
        var pending = new Queue<(Type Type, Guid[] Ids)>(deleted.Select(d => (d.Key, d.Value.ToArray())));
        while (pending.TryDequeue(out var step))
        {
            var fresh = Add(reached, step.Type, step.Ids);
            if (fresh.Length == 0)
            {
                continue;
            }

            foreach (var cascade in AccessAnchors.Cascades.Where(c => c.Principal == step.Type))
            {
                var dependents = cascade.Dependents(db, fresh).ToArray();
                if (dependents.Length > 0)
                {
                    pending.Enqueue((cascade.Dependent, dependents));
                }
            }
        }

        foreach (var (type, ids) in reached)
        {
            var array = ids.ToArray();
            foreach (var arm in AccessAnchors.Arms.Where(a => a.Targets.Contains(type)))
            {
                if (arm.Column == AccessReferenceColumn.PermissionMember)
                {
                    db.PermissionGroupMembers.RemoveRange(MembersNaming(db, arm, array).ToList());
                }
                else
                {
                    db.AccessEntries.RemoveRange(EntriesNaming(db, arm, array).ToList());
                }
            }
        }
    }

    /// <summary>
    /// Records ids against their type and answers the ones not seen before — a file converted from
    /// a file converted from it would otherwise walk the same cascade for ever.
    /// </summary>
    private static Guid[] Add(Dictionary<Type, HashSet<Guid>> reached, Type type, Guid[] ids)
    {
        if (!reached.TryGetValue(type, out var seen))
        {
            seen = [];
            reached[type] = seen;
        }

        return [.. ids.Where(seen.Add)];
    }

    private static IQueryable<AccessEntry> EntriesNaming(SilexGisDbContext db, AccessAnchorArm arm, Guid[] ids)
    {
        var domain = arm.Domain;
        var scope = arm.ScopeKind;
        var subject = arm.SubjectKind;
        return arm.Column switch
        {
            AccessReferenceColumn.ObjectScope => db.AccessEntries.Where(e =>
                e.ScopeKind == AccessScopeKind.Object && e.Domain == domain
                && e.ScopeId != null && ids.Contains(e.ScopeId.Value)),
            AccessReferenceColumn.ScopeId => db.AccessEntries.Where(e =>
                e.ScopeKind == scope && e.ScopeId != null && ids.Contains(e.ScopeId.Value)),
            AccessReferenceColumn.Subject => db.AccessEntries.Where(e =>
                e.SubjectKind == subject && e.SubjectId != null && ids.Contains(e.SubjectId.Value)),
            AccessReferenceColumn.ScopeFeature => db.AccessEntries.Where(e =>
                e.ScopeFeatureId != null && ids.Contains(e.ScopeFeatureId.Value)),
            AccessReferenceColumn.GrantedViaExpedition => db.AccessEntries.Where(e =>
                e.GrantedViaExpeditionId != null && ids.Contains(e.GrantedViaExpeditionId.Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm.Column, "Not a column of the access entries."),
        };
    }

    private static IQueryable<PermissionGroupMember> MembersNaming(SilexGisDbContext db, AccessAnchorArm arm, Guid[] ids)
    {
        var kind = arm.SubjectKind;
        return db.PermissionGroupMembers.Where(m => m.MemberKind == kind && ids.Contains(m.MemberId));
    }
}
