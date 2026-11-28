// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// The part of a run's removal that is not features: the people it invented, the grants and
/// permission groups that came with them, the rows it hung on features, and extra vocabulary.
/// A generated row that data the run did not write refers to is kept — an account somebody has
/// used, a caver a real trip names, a club a real cave is bound to — and so is everything that
/// row needs to stay what it was: a kept account keeps its roster entry, memberships and grants;
/// a kept club keeps its roster and its permission groups.
/// </summary>
public static class TestDataPeopleRemoval
{
    /// <summary>Rows a run hangs on features, removed before the features so they never count as somebody's use of one.</summary>
    public static IReadOnlyList<string> HangOnTables { get; } =
        ["feature_links", "feature_external_ids", "feature_shares", "cave_qr_publications"];

    /// <summary>What a removal keeps, decided once before anything is deleted.</summary>
    public sealed class Kept
    {
        public HashSet<string> Users { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Cavers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Clubs { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Memberships { get; } = new(StringComparer.Ordinal);

        public HashSet<string> PermissionGroups { get; } = new(StringComparer.Ordinal);

        public HashSet<string> PermissionGroupMembers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> AccessEntries { get; } = new(StringComparer.Ordinal);

        public HashSet<string> AccountRows { get; } = new(StringComparer.Ordinal);

        public int Count => Users.Count + Cavers.Count + Clubs.Count;
    }

    public static async Task<Kept> DecideAsync(
        SilexGisDbContext db, Guid runId, ILookup<string, string> listed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(listed);
        var kept = new Kept();
        var users = listed["users"].ToList();
        var cavers = listed["cavers"].ToList();
        var clubs = listed["caving_groups"].ToList();
        var listedEntries = listed["access_entries"].Select(Long).ToHashSet();
        var listedMembers = listed["permission_group_members"].Select(Long).ToHashSet();

        // Accounts: referred to by a key from data the run did not write, or named by a grant or
        // a group membership somebody else made (those name an account by id, not by a key).
        kept.Users.UnionWith(await TestDataReferencesSql.ReferencedByOthersAsync(db, runId, "users", users, ct));
        var userIds = users.Select(Guid.Parse).ToList();
        kept.Users.UnionWith((await db.AccessEntries
                .Where(e => e.SubjectKind == AccessSubjectKind.User && e.SubjectId != null && userIds.Contains(e.SubjectId.Value))
                .Select(e => new { e.Id, e.SubjectId })
                .ToListAsync(ct))
            .Where(e => !listedEntries.Contains(e.Id)).Select(e => e.SubjectId!.Value.ToString()));
        kept.Users.UnionWith((await db.PermissionGroupMembers
                .Where(m => m.MemberKind == AccessSubjectKind.User && userIds.Contains(m.MemberId))
                .Select(m => new { m.Id, m.MemberId })
                .ToListAsync(ct))
            .Where(m => !listedMembers.Contains(m.Id)).Select(m => m.MemberId.ToString()));

        // Roster entries: referred to by somebody's data, or the entry of an account that stays.
        kept.Cavers.UnionWith(await TestDataReferencesSql.ReferencedByOthersAsync(db, runId, "cavers", cavers, ct));
        var caverIds = cavers.Select(Guid.Parse).ToList();
        var keptUserIds = kept.Users.Select(Guid.Parse).ToList();
        kept.Cavers.UnionWith((await db.Cavers
                .Where(c => caverIds.Contains(c.Id) && c.UserId != null && keptUserIds.Contains(c.UserId.Value))
                .Select(c => c.Id)
                .ToListAsync(ct)).Select(id => id.ToString()));

        // Memberships of a kept caver stay, and keep their club with them.
        var keptCaverIds = kept.Cavers.Select(Guid.Parse).ToList();
        var keptMemberships = await db.CavingGroupMemberships
            .Where(m => keptCaverIds.Contains(m.CaverId))
            .Select(m => new { m.Id, m.CavingGroupId })
            .ToListAsync(ct);
        kept.Memberships.UnionWith(keptMemberships.Select(m => m.Id.ToString(CultureInfo.InvariantCulture)));

        // Clubs: referred to by somebody's data (a cave bound to one, a member of their own), named
        // by somebody's grant or group, or the club of a membership that stays.
        kept.Clubs.UnionWith(await TestDataReferencesSql.ReferencedByOthersAsync(db, runId, "caving_groups", clubs, ct));
        var clubIds = clubs.Select(Guid.Parse).ToList();
        kept.Clubs.UnionWith((await db.AccessEntries
                .Where(e => (e.SubjectKind == AccessSubjectKind.CavingGroup && e.SubjectId != null && clubIds.Contains(e.SubjectId.Value))
                    || (e.ScopeKind == AccessScopeKind.CavingGroup && e.ScopeId != null && clubIds.Contains(e.ScopeId.Value)))
                .Select(e => new { e.Id, e.SubjectKind, e.SubjectId, e.ScopeKind, e.ScopeId })
                .ToListAsync(ct))
            .Where(e => !listedEntries.Contains(e.Id))
            .Select(e => (e.SubjectKind == AccessSubjectKind.CavingGroup && e.SubjectId is { } s && clubIds.Contains(s) ? s : e.ScopeId!.Value).ToString()));
        kept.Clubs.UnionWith((await db.PermissionGroupMembers
                .Where(m => m.MemberKind == AccessSubjectKind.CavingGroup && clubIds.Contains(m.MemberId))
                .Select(m => new { m.Id, m.MemberId })
                .ToListAsync(ct))
            .Where(m => !listedMembers.Contains(m.Id)).Select(m => m.MemberId.ToString()));
        kept.Clubs.UnionWith(keptMemberships.Select(m => m.CavingGroupId.ToString()).Where(id => clubs.Contains(id)));

        // Permission groups: used by somebody's data, or holding a club or account that stays.
        var groups = listed["permission_groups"].ToList();
        kept.PermissionGroups.UnionWith(await TestDataReferencesSql.ReferencedByOthersAsync(db, runId, "permission_groups", groups, ct));
        var keptClubIds = kept.Clubs.Select(Guid.Parse).ToHashSet();
        var keptUserSet = keptUserIds.ToHashSet();
        var memberRows = await db.PermissionGroupMembers
            .Where(m => listedMembers.Contains(m.Id))
            .Select(m => new { m.Id, m.PermissionGroupId, m.MemberKind, m.MemberId })
            .ToListAsync(ct);
        kept.PermissionGroups.UnionWith(memberRows
            .Where(m => (m.MemberKind == AccessSubjectKind.CavingGroup && keptClubIds.Contains(m.MemberId))
                || (m.MemberKind == AccessSubjectKind.User && keptUserSet.Contains(m.MemberId)))
            .Select(m => m.PermissionGroupId.ToString())
            .Where(id => groups.Contains(id)));
        var keptGroupIds = kept.PermissionGroups.Select(Guid.Parse).ToHashSet();
        kept.PermissionGroupMembers.UnionWith(memberRows
            .Where(m => keptGroupIds.Contains(m.PermissionGroupId)
                || (m.MemberKind == AccessSubjectKind.CavingGroup && keptClubIds.Contains(m.MemberId))
                || (m.MemberKind == AccessSubjectKind.User && keptUserSet.Contains(m.MemberId)))
            .Select(m => m.Id.ToString(CultureInfo.InvariantCulture)));

        var entryRows = await db.AccessEntries
            .Where(e => listedEntries.Contains(e.Id))
            .Select(e => new { e.Id, e.PermissionGroupId, e.SubjectKind, e.SubjectId })
            .ToListAsync(ct);
        kept.AccessEntries.UnionWith(entryRows
            .Where(e => (e.PermissionGroupId is { } g && keptGroupIds.Contains(g))
                || (e.SubjectKind == AccessSubjectKind.CavingGroup && e.SubjectId is { } c && keptClubIds.Contains(c))
                || (e.SubjectKind == AccessSubjectKind.User && e.SubjectId is { } u && keptUserSet.Contains(u)))
            .Select(e => e.Id.ToString(CultureInfo.InvariantCulture)));

        // A kept account keeps what is its own: addresses, notification choices, feeds, keys.
        kept.AccountRows.UnionWith((await db.UserAddresses.Where(a => keptUserIds.Contains(a.UserId)).Select(a => a.Id).ToListAsync(ct)).Select(id => "user_addresses:" + id));
        kept.AccountRows.UnionWith((await db.UserNotificationPreferences.Where(a => keptUserIds.Contains(a.UserId)).Select(a => a.Id).ToListAsync(ct)).Select(id => "user_notification_preferences:" + id.ToString(CultureInfo.InvariantCulture)));
        kept.AccountRows.UnionWith((await db.CalendarFeedTokens.Where(a => keptUserIds.Contains(a.UserId)).Select(a => a.Id).ToListAsync(ct)).Select(id => "calendar_feed_tokens:" + id));
        kept.AccountRows.UnionWith((await db.ApiAccessKeys.Where(a => keptUserIds.Contains(a.UserId)).Select(a => a.Id).ToListAsync(ct)).Select(id => "api_access_keys:" + id));
        return kept;
    }

    /// <summary>
    /// The rows the run hung on features, and its grants but those a kept account or club keeps.
    /// They go before the features, so a grant or a link of the run's own never counts as
    /// somebody's use of a generated cave.
    /// </summary>
    public static async Task RemoveHangOnsAsync(SilexGisDbContext db, ILookup<string, string> listed, Kept kept, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(kept);
        var entries = Free(listed["access_entries"], kept.AccessEntries).Select(Long).ToList();
        await db.AccessEntries.Where(x => entries.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var links = listed["feature_links"].Select(Long).ToList();
        await db.FeatureLinks.Where(x => links.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var externalIds = Guids(listed["feature_external_ids"]);
        await db.FeatureExternalIds.Where(x => externalIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var shares = Guids(listed["feature_shares"]);
        await db.FeatureShares.Where(x => shares.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var qr = Guids(listed["cave_qr_publications"]);
        await db.CaveQrPublications.Where(x => qr.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }

    /// <summary>Extra vocabulary the run made, unless something the run did not write uses it.</summary>
    public static async Task<int> RemoveVocabularyAsync(SilexGisDbContext db, Guid runId, ILookup<string, string> listed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(listed);
        var kept = 0;
        async Task<List<T>> Free<T>(string table, Func<string, T> parse)
        {
            var ids = listed[table].ToList();
            var used = await TestDataReferencesSql.ReferencedByOthersAsync(db, runId, table, ids, ct);
            kept += used.Count;
            return [.. ids.Where(id => !used.Contains(id)).Select(parse)];
        }

        var linkKinds = await Free("link_kinds", Long);
        await db.LinkKinds.Where(x => linkKinds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var caveTypes = await Free("cave_types", Long);
        await db.CaveTypes.Where(x => caveTypes.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var entranceTypes = await Free("entrance_types", Long);
        await db.EntranceTypes.Where(x => entranceTypes.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var rockTypes = await Free("rock_types", Long);
        await db.RockTypes.Where(x => rockTypes.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var featureTypes = await Free("feature_types", Long);
        await db.FeatureTypes.Where(x => featureTypes.Contains(x.Id)).ExecuteDeleteAsync(ct);
        return kept;
    }

    /// <summary>The people, in the order nothing deleted is still pointed at by something to come.</summary>
    public static async Task RemovePeopleAsync(SilexGisDbContext db, ILookup<string, string> listed, Kept kept, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(kept);

        var members = Free(listed["permission_group_members"], kept.PermissionGroupMembers).Select(Long).ToList();
        await db.PermissionGroupMembers.Where(x => members.Contains(x.Id)).ExecuteDeleteAsync(ct);
        // Permission groups, accounts and clubs are rows an access rule can name, so they are
        // removed through the change tracker, where the rules naming them go with them.
        var groups = Guids(Free(listed["permission_groups"], kept.PermissionGroups));
        db.PermissionGroups.RemoveRange(await db.PermissionGroups.Where(x => groups.Contains(x.Id)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        var addresses = Guids(FreeAccountRows(listed, "user_addresses", kept));
        await db.UserAddresses.Where(x => addresses.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var preferences = FreeAccountRows(listed, "user_notification_preferences", kept).Select(Long).ToList();
        await db.UserNotificationPreferences.Where(x => preferences.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var feeds = Guids(FreeAccountRows(listed, "calendar_feed_tokens", kept));
        await db.CalendarFeedTokens.Where(x => feeds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var keys = Guids(FreeAccountRows(listed, "api_access_keys", kept));
        await db.ApiAccessKeys.Where(x => keys.Contains(x.Id)).ExecuteDeleteAsync(ct);

        var memberships = Free(listed["caving_group_memberships"], kept.Memberships).Select(Long).ToList();
        await db.CavingGroupMemberships.Where(x => memberships.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var cavers = Guids(Free(listed["cavers"], kept.Cavers));
        await db.Cavers.Where(x => cavers.Contains(x.Id)).ExecuteDeleteAsync(ct);

        // An account's roles and sign-in records go with it, by the keys Identity declares.
        var users = Guids(Free(listed["users"], kept.Users));
        db.Users.RemoveRange(await db.Users.Where(x => users.Contains(x.Id)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        // A generated feature kept because somebody used it is still bound to its club, which
        // stays so the feature keeps the audience it had.
        var listedClubs = Guids(listed["caving_groups"]);
        kept.Clubs.UnionWith((await db.Features.IgnoreQueryFilters()
                .Where(f => f.CavingGroupId != null && listedClubs.Contains(f.CavingGroupId.Value))
                .Select(f => f.CavingGroupId!.Value)
                .Distinct()
                .ToListAsync(ct)).Select(id => id.ToString()));
        var clubs = Guids(Free(listed["caving_groups"], kept.Clubs));
        db.CavingGroups.RemoveRange(await db.CavingGroups.Where(x => clubs.Contains(x.Id)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<string> Free(IEnumerable<string> ids, HashSet<string> kept) => ids.Where(id => !kept.Contains(id));

    private static IEnumerable<string> FreeAccountRows(ILookup<string, string> listed, string table, Kept kept) =>
        listed[table].Where(id => !kept.AccountRows.Contains(table + ":" + id));

    private static List<Guid> Guids(IEnumerable<string> ids) => [.. ids.Select(Guid.Parse)];

    private static long Long(string id) => long.Parse(id, CultureInfo.InvariantCulture);
}
