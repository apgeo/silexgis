// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Which features a trip is about, read and written through the general link mechanism.
///
/// The pairing used to be a table of its own carrying no role at all — it meant "a cave this
/// trip is about" and nothing finer. Its replacement is a link typed with one of the trip
/// roles, which says what was done there as well as where. So every reader that wants the old
/// meaning asks over <em>all</em> the roles at once: a read narrowed to one of them answers a
/// smaller question, silently, and none of the callers wants the smaller one. Only writers
/// pick a role, and picking a precise one there is an improvement precisely because the
/// readers do not care.
///
/// The queries here compose into the caller's own query rather than running on their own, so
/// a filter, a count and the page it counts stay one statement and cannot drift apart.
/// </summary>
public static class TripRoleLinks
{
    /// <summary>The vocabulary rows for the trip roles. Composed as a subquery, so an
    /// installation whose seeding ran later needs no cache to be invalidated.</summary>
    public static IQueryable<long> RoleIds(SilexGisDbContext db) =>
        db.ResLinkRelationTypes.AsNoTracking()
            .Where(r => ResLinkRelationTypeSeeds.TripRoleCodes.Contains(r.Code))
            .Select(r => r.Id);

    /// <summary>
    /// The trips any trip role names this feature on, as a subquery a caller composes into its
    /// own visibility-filtered query. Ids repeat when two roles or two links name the same
    /// feature on one trip, so a caller counting trips reduces them — the retired pairing was
    /// unique per (trip, cave) and a naive count would now say two where it used to say one.
    /// </summary>
    public static IQueryable<Guid> TripIdsNaming(SilexGisDbContext db, Guid featureId)
    {
        var roleIds = RoleIds(db);
        return from featureMember in db.ResLinkMembers.AsNoTracking()
               where featureMember.FeatureId == featureId
               join link in db.ResLinks.AsNoTracking() on featureMember.ResLinkId equals link.Id
               where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
               join tripMember in db.ResLinkMembers.AsNoTracking()
                   on featureMember.ResLinkId equals tripMember.ResLinkId
               where tripMember.EntityType == AttachedEntityType.TripLog && tripMember.EntityId != null
               select tripMember.EntityId!.Value;
    }

    /// <summary>
    /// The trips any trip role names any of these features on, as a subquery a caller composes
    /// into its own visibility-filtered query. The set of features is itself a query, so a
    /// caller narrowing by an area and everything inside it keeps the whole question in one
    /// statement rather than reading a list of ids and asking again with it.
    ///
    /// Ids repeat, for the same reason the single-feature form's do: two roles or two links may
    /// name the same feature on one trip, and two named features may sit on one trip. A caller
    /// counting trips reduces them.
    /// </summary>
    public static IQueryable<Guid> TripIdsNamingAny(SilexGisDbContext db, IQueryable<Guid> featureIds)
    {
        var roleIds = RoleIds(db);
        return from featureMember in db.ResLinkMembers.AsNoTracking()
               where featureMember.FeatureId != null && featureIds.Contains(featureMember.FeatureId.Value)
               join link in db.ResLinks.AsNoTracking() on featureMember.ResLinkId equals link.Id
               where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
               join tripMember in db.ResLinkMembers.AsNoTracking()
                   on featureMember.ResLinkId equals tripMember.ResLinkId
               where tripMember.EntityType == AttachedEntityType.TripLog && tripMember.EntityId != null
               select tripMember.EntityId!.Value;
    }

    /// <summary>
    /// The trips any trip role names any of these features on, over a set of ids the caller has
    /// already read and gated. A sibling of the query-taking form rather than a replacement: a
    /// caller whose set of features is itself decided row by row — because the decision cannot be
    /// written as a predicate — has nothing to compose, and passing it back through a subquery
    /// would only hide that the ids were materialised.
    /// </summary>
    public static IQueryable<Guid> TripIdsNamingAny(
        SilexGisDbContext db, IReadOnlyCollection<Guid> featureIds)
    {
        var roleIds = RoleIds(db);
        return from featureMember in db.ResLinkMembers.AsNoTracking()
               where featureMember.FeatureId != null && featureIds.Contains(featureMember.FeatureId.Value)
               join link in db.ResLinks.AsNoTracking() on featureMember.ResLinkId equals link.Id
               where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
               join tripMember in db.ResLinkMembers.AsNoTracking()
                   on featureMember.ResLinkId equals tripMember.ResLinkId
               where tripMember.EntityType == AttachedEntityType.TripLog && tripMember.EntityId != null
               select tripMember.EntityId!.Value;
    }

    /// <summary>The features any trip role names on this trip, as a composable subquery.</summary>
    public static IQueryable<Guid> FeatureIdsNamedBy(SilexGisDbContext db, Guid tripId)
    {
        var roleIds = RoleIds(db);
        return from tripMember in db.ResLinkMembers.AsNoTracking()
               where tripMember.EntityType == AttachedEntityType.TripLog && tripMember.EntityId == tripId
               join link in db.ResLinks.AsNoTracking() on tripMember.ResLinkId equals link.Id
               where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
               join featureMember in db.ResLinkMembers.AsNoTracking()
                   on tripMember.ResLinkId equals featureMember.ResLinkId
               where featureMember.FeatureId != null
               select featureMember.FeatureId!.Value;
    }

    /// <summary>
    /// Every (trip, feature) pair named by a trip role across a set of trips, as a subquery over
    /// whatever query produced those trips. The point of taking a query rather than a list of ids
    /// is that a caller's visibility walk stays inside the statement: a total over these pairs is
    /// then counted over the same rows the caller would be handed, in one pass, and cannot drift
    /// from them.
    ///
    /// Pairs repeat when two roles or two links name the same feature on one trip, so a caller
    /// counting anything reduces them first — the retired pairing was unique per (trip, cave) and
    /// a naive count would now say two where it used to say one.
    /// </summary>
    /// <remarks>
    /// The narrowing to particular features is a parameter rather than something a caller adds
    /// afterwards, and so is every other condition: the pair is built in the projection, and a
    /// database cannot be asked about a member of a value the projection is still constructing.
    /// What comes back is therefore read, not composed on further.
    /// </remarks>
    public static IQueryable<TripFeaturePair> PairsIn(
        SilexGisDbContext db, IQueryable<Guid> tripIds, IReadOnlyCollection<Guid>? featureIds = null)
    {
        var roleIds = RoleIds(db);
        var all = from tripMember in db.ResLinkMembers.AsNoTracking()
                  where tripMember.EntityType == AttachedEntityType.TripLog
                      && tripMember.EntityId != null
                      && tripIds.Contains(tripMember.EntityId.Value)
                  join link in db.ResLinks.AsNoTracking() on tripMember.ResLinkId equals link.Id
                  where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
                  join featureMember in db.ResLinkMembers.AsNoTracking()
                      on tripMember.ResLinkId equals featureMember.ResLinkId
                  where featureMember.FeatureId != null
                  select new { tripMember.EntityId, featureMember.FeatureId };

        // Two shapes rather than one predicate carrying a null check: an absent narrowing is the
        // absence of a condition, and writing it as a comparison against a captured null asks the
        // translator to fold something it has no reason to.
        if (featureIds is not null)
        {
            all = all.Where(pair => featureIds.Contains(pair.FeatureId!.Value));
        }

        return all.Select(pair => new TripFeaturePair(pair.EntityId!.Value, pair.FeatureId!.Value));
    }

    /// <summary>
    /// The features any trip role names across a set of trips, as a subquery over the query that
    /// produced them. Ids repeat and a caller reduces them.
    /// </summary>
    public static IQueryable<Guid> FeatureIdsNamedIn(SilexGisDbContext db, IQueryable<Guid> tripIds)
    {
        var roleIds = RoleIds(db);
        return from tripMember in db.ResLinkMembers.AsNoTracking()
               where tripMember.EntityType == AttachedEntityType.TripLog
                   && tripMember.EntityId != null
                   && tripIds.Contains(tripMember.EntityId.Value)
               join link in db.ResLinks.AsNoTracking() on tripMember.ResLinkId equals link.Id
               where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
               join featureMember in db.ResLinkMembers.AsNoTracking()
                   on tripMember.ResLinkId equals featureMember.ResLinkId
               where featureMember.FeatureId != null
               select featureMember.FeatureId!.Value;
    }

    /// <summary>
    /// Every (trip, feature) pair named by a trip role across a set of trips, deduplicated, in
    /// one read. Optionally narrowed to one kind of feature, for a caller whose surface promises
    /// a particular kind; left open otherwise, because a role names any linkable target and
    /// which of them matters is the caller's question rather than this one's.
    /// </summary>
    public static async Task<List<TripFeaturePair>> PairsForAsync(
        SilexGisDbContext db,
        IReadOnlyCollection<Guid> tripIds,
        FeatureKind? kind,
        CancellationToken ct)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        var roleIds = RoleIds(db);
        var rows = from tripMember in db.ResLinkMembers.AsNoTracking()
                   where tripMember.EntityType == AttachedEntityType.TripLog
                       && tripMember.EntityId != null
                       && tripIds.Contains(tripMember.EntityId.Value)
                   join link in db.ResLinks.AsNoTracking() on tripMember.ResLinkId equals link.Id
                   where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
                   join featureMember in db.ResLinkMembers.AsNoTracking()
                       on tripMember.ResLinkId equals featureMember.ResLinkId
                   where featureMember.FeatureId != null
                       && (kind == null
                           || db.Features.Any(f => f.Id == featureMember.FeatureId && f.Kind == kind))
                   select new { TripId = tripMember.EntityId!.Value, FeatureId = featureMember.FeatureId!.Value };

        return
        [
            .. (await rows.Distinct().ToListAsync(ct))
                .Select(r => new TripFeaturePair(r.TripId, r.FeatureId)),
        ];
    }

    /// <summary>
    /// Every (trip, feature) pair named by a trip role across a set of trips, deduplicated, in one
    /// read, narrowed to the features of one data-level type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sibling of the kind-narrowed read above rather than an extra parameter on it, because the
    /// two narrow on different columns and neither can be asked the other's question. The schema
    /// discriminator holds one member per subtype table plus a single member for everything
    /// data-driven, so a continuation, a sinkhole and a spring all carry the same value there and
    /// narrowing to it is no narrowing at all. What tells them apart is the type row the feature
    /// points at, and that is what this one asks about.
    /// </para>
    /// <para>
    /// Role-agnostic like every other reader here: a continuation somebody recorded as merely
    /// visited is the same open way on as one recorded as a lead, and a read narrowed to the
    /// obvious role would drop it silently.
    /// </para>
    /// <para>
    /// The type is a database identity, so a caller resolves it from its code and never carries a
    /// number — the same row has a different id on a fresh installation than on an upgraded one.
    /// </para>
    /// </remarks>
    public static async Task<List<TripFeaturePair>> PairsOfTypeForAsync(
        SilexGisDbContext db,
        IReadOnlyCollection<Guid> tripIds,
        long featureTypeId,
        CancellationToken ct)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        var roleIds = RoleIds(db);
        var rows = from tripMember in db.ResLinkMembers.AsNoTracking()
                   where tripMember.EntityType == AttachedEntityType.TripLog
                       && tripMember.EntityId != null
                       && tripIds.Contains(tripMember.EntityId.Value)
                   join link in db.ResLinks.AsNoTracking() on tripMember.ResLinkId equals link.Id
                   where link.RelationTypeId != null && roleIds.Contains(link.RelationTypeId.Value)
                   join featureMember in db.ResLinkMembers.AsNoTracking()
                       on tripMember.ResLinkId equals featureMember.ResLinkId
                   where featureMember.FeatureId != null
                       && db.Features.Any(f =>
                           f.Id == featureMember.FeatureId && f.FeatureTypeId == featureTypeId)
                   select new { TripId = tripMember.EntityId!.Value, FeatureId = featureMember.FeatureId!.Value };

        return
        [
            .. (await rows.Distinct().ToListAsync(ct))
                .Select(r => new TripFeaturePair(r.TripId, r.FeatureId)),
        ];
    }

    /// <summary>
    /// Records that a trip did something to a feature, by extending the trip's existing link of
    /// that role or opening one when there is none. Extending is what a person doing it by hand
    /// would do — a second link of the same role between the same two ends says nothing the
    /// first did not — and it also keeps the pair count from growing on every repetition. A link
    /// that has reached the shared member bound is not extended past it: a role is the union of
    /// its links, so the next naming opens another one rather than building an object no other
    /// surface would accept or could later edit.
    ///
    /// Returns false when the role is not a seeded one, so a caller cannot silently create an
    /// untyped link by mistyping a code. Nothing is saved here: the members join the caller's
    /// own unit of work so that a failure later takes the link with it.
    /// </summary>
    public static async Task<bool> NameFeatureAsync(
        SilexGisDbContext db,
        Guid tripId,
        Guid featureId,
        string roleCode,
        Guid? addedBy,
        CancellationToken ct)
    {
        var roleId = await db.ResLinkRelationTypes
            .Where(r => r.Code == roleCode)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (roleId is null)
        {
            return false;
        }

        // Asked over every link of the role rather than over the one about to be joined: with a
        // role spread across more than one link, the feature may already sit on another of them,
        // and naming it again would say nothing the first naming did not.
        if (await NamesFeatureAsync(db, tripId, roleId.Value, featureId, ct))
        {
            return true;
        }

        var host = await HostLinkIdAsync(db, tripId, roleId.Value, ct);
        if (host is null)
        {
            var link = new ResLink
            {
                ShortCode = ResLinkRules.NewShortCode(),
                RelationTypeId = roleId,
                CreatedBy = addedBy,
            };
            db.ResLinks.Add(link);
            // Directed with the trip as the distinguished member, so the relation reads out of
            // the trip ("Visited …") rather than being ambiguous about which end acted.
            db.ResLinkMembers.Add(new ResLinkMember
            {
                ResLinkId = link.Id,
                EntityType = AttachedEntityType.TripLog,
                EntityId = tripId,
                IsMain = true,
                SortOrder = 0,
                AddedBy = addedBy,
            });
            host = link.Id;
        }

        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = host.Value,
            FeatureId = featureId,
            SortOrder = 1,
            AddedBy = addedBy,
        });
        return true;
    }

    /// <summary>
    /// Takes a feature off the trip's links of one role, and takes a link with it once nothing
    /// is left to relate: a link holding only the trip is a one-sided association no surface
    /// offers and no delete path reaches. Members already tracked as added in this unit of work
    /// are dropped from it rather than deleted, so an add and a remove in one save cancel.
    /// </summary>
    public static async Task UnnameFeatureAsync(
        SilexGisDbContext db, Guid tripId, Guid featureId, string roleCode, CancellationToken ct)
    {
        var linkIds = await LinkIdsAsync(db, tripId, roleCode, ct);
        if (linkIds.Count == 0)
        {
            return;
        }

        // A membership queued earlier in this same unit of work has no row to delete, so it is
        // dropped from the unit of work instead — that is how an add and a removal cancel.
        var pending = db.ChangeTracker.Entries<ResLinkMember>()
            .Where(e => e.State == EntityState.Added
                && e.Entity.FeatureId == featureId && linkIds.Contains(e.Entity.ResLinkId))
            .ToList();
        foreach (var entry in pending)
        {
            entry.State = EntityState.Detached;
        }

        var members = await db.ResLinkMembers
            .Where(m => linkIds.Contains(m.ResLinkId) && m.FeatureId == featureId)
            .ToListAsync(ct);
        foreach (var member in members)
        {
            db.ResLinkMembers.Remove(member);
        }

        var touched = members.Select(m => m.ResLinkId)
            .Concat(pending.Select(e => e.Entity.ResLinkId))
            .Distinct();
        foreach (var linkId in touched)
        {
            // Counted against the unit of work, not against the stored rows alone: removing two
            // targets of one link takes two calls with no save between them, and a count that saw
            // only the database would find both of them still there and keep an emptied link.
            if (await MemberCountAsync(db, linkId, ct) > 1)
            {
                continue;
            }

            await DropLinkAsync(db, linkId, ct);
        }
    }

    /// <summary>How many members a link will have once this unit of work is saved.</summary>
    private static async Task<int> MemberCountAsync(SilexGisDbContext db, Guid linkId, CancellationToken ct)
    {
        var stored = await db.ResLinkMembers.AsNoTracking().CountAsync(m => m.ResLinkId == linkId, ct);
        var queued = db.ChangeTracker.Entries<ResLinkMember>()
            .Where(e => e.Entity.ResLinkId == linkId)
            .Sum(e => e.State switch
            {
                EntityState.Added => 1,
                EntityState.Deleted => -1,
                _ => 0,
            });
        return stored + queued;
    }

    /// <summary>
    /// Takes a link out of the unit of work along with whatever memberships it still carries.
    /// A link opened in this same save is withdrawn rather than deleted — there is no row yet,
    /// and leaving its members queued would insert them against a link that never exists.
    /// </summary>
    private static async Task DropLinkAsync(SilexGisDbContext db, Guid linkId, CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries<ResLinkMember>()
                     .Where(e => e.State == EntityState.Added && e.Entity.ResLinkId == linkId)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        var tracked = db.ChangeTracker.Entries<ResLink>().FirstOrDefault(e => e.Entity.Id == linkId);
        if (tracked is not null)
        {
            db.ResLinks.Remove(tracked.Entity);
            return;
        }

        var link = await db.ResLinks.FirstOrDefaultAsync(l => l.Id == linkId, ct);
        if (link is not null)
        {
            db.ResLinks.Remove(link);
        }
    }

    /// <summary>
    /// The trip's link of a role that the next naming should join: the first one that still has
    /// room. A link this unit of work has already decided to delete is never one of them — the
    /// row is still readable until the save, and joining it would take the new membership down
    /// with it.
    /// </summary>
    private static async Task<Guid?> HostLinkIdAsync(
        SilexGisDbContext db, Guid tripId, long roleId, CancellationToken ct)
    {
        var saved = await db.ResLinkMembers.AsNoTracking()
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId)
            .Join(db.ResLinks.AsNoTracking().Where(l => l.RelationTypeId == roleId),
                m => m.ResLinkId, l => l.Id, (m, l) => l.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);

        // A link opened earlier in this same unit of work has no row to find yet.
        var pendingMembers = db.ChangeTracker.Entries<ResLinkMember>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId)
            .Select(m => m.ResLinkId)
            .ToHashSet();
        var pending = db.ChangeTracker.Entries<ResLink>()
            .Where(e => e.State == EntityState.Added && e.Entity.RelationTypeId == roleId
                && pendingMembers.Contains(e.Entity.Id))
            .Select(e => e.Entity.Id)
            .ToList();

        var doomed = db.ChangeTracker.Entries<ResLink>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.Id)
            .ToHashSet();

        foreach (var candidate in saved.Concat(pending))
        {
            if (doomed.Contains(candidate))
            {
                continue;
            }

            if (ResLinkRules.MayAddMember(await MemberCountAsync(db, candidate, ct)))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Whether any link of one role already names this feature on this trip.</summary>
    private static async Task<bool> NamesFeatureAsync(
        SilexGisDbContext db, Guid tripId, long roleId, Guid featureId, CancellationToken ct)
    {
        var linkIds = await db.ResLinkMembers.AsNoTracking()
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId)
            .Join(db.ResLinks.AsNoTracking().Where(l => l.RelationTypeId == roleId),
                m => m.ResLinkId, l => l.Id, (m, l) => l.Id)
            .Distinct()
            .ToListAsync(ct);

        var doomed = db.ChangeTracker.Entries<ResLink>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.Id)
            .ToHashSet();
        var live = linkIds.Where(id => !doomed.Contains(id)).ToList();

        // A membership this unit of work is dropping does not count as a naming: the caller is
        // putting the feature back, which is exactly the add that has to happen.
        var removed = db.ChangeTracker.Entries<ResLinkMember>()
            .Where(e => e.State == EntityState.Deleted && e.Entity.FeatureId == featureId)
            .Select(e => e.Entity.ResLinkId)
            .ToHashSet();

        if (live.Count > 0)
        {
            var stored = await db.ResLinkMembers.AsNoTracking()
                .Where(m => live.Contains(m.ResLinkId) && m.FeatureId == featureId)
                .Select(m => m.ResLinkId)
                .ToListAsync(ct);
            if (stored.Exists(id => !removed.Contains(id)))
            {
                return true;
            }
        }

        // A link opened earlier in this same unit of work has no row to find, so it is looked for
        // in the tracker — but only the ones this trip is a member of. A link of the same role
        // opened for a different trip is not this trip's naming, and counting it would make two
        // trips naming the same cave in one save leave the second trip naming nothing at all.
        var pendingTripLinks = db.ChangeTracker.Entries<ResLinkMember>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId)
            .Select(m => m.ResLinkId)
            .ToHashSet();
        var pendingLinks = db.ChangeTracker.Entries<ResLink>()
            .Where(e => e.State == EntityState.Added && e.Entity.RelationTypeId == roleId
                && pendingTripLinks.Contains(e.Entity.Id))
            .Select(e => e.Entity.Id)
            .ToHashSet();
        return db.ChangeTracker.Entries<ResLinkMember>().Any(e =>
            e.State == EntityState.Added && e.Entity.FeatureId == featureId
            && (live.Contains(e.Entity.ResLinkId) || pendingLinks.Contains(e.Entity.ResLinkId)));
    }

    private static async Task<List<Guid>> LinkIdsAsync(
        SilexGisDbContext db, Guid tripId, string roleCode, CancellationToken ct) =>
        await db.ResLinkMembers.AsNoTracking()
            .Where(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId)
            .Join(
                db.ResLinks.AsNoTracking().Where(l => l.RelationTypeId != null
                    && db.ResLinkRelationTypes.Any(r => r.Id == l.RelationTypeId && r.Code == roleCode)),
                m => m.ResLinkId, l => l.Id, (m, l) => l.Id)
            .Distinct()
            .ToListAsync(ct);
}

/// <summary>One naming of a feature by a trip, without the role that did the naming.</summary>
public readonly record struct TripFeaturePair(Guid TripId, Guid FeatureId);
