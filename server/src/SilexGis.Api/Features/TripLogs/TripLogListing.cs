// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>Which of the listing's narrowings a query is being built without.</summary>
/// <remarks>
/// A facet's own choices are left out when its option counts are worked out, so each option says
/// how many trips it would leave rather than how many it leaves now — otherwise every unpicked
/// option in a facet somebody has already used reads as zero, and the panel stops being usable
/// the moment it is used. Every other facet still applies, which is what makes the numbers the
/// answer to "and this one too" rather than to a question nobody asked.
/// </remarks>
internal enum TripListFacet
{
    None = 0,
    Type,
    State,
    Visibility,
    Incident,
    Participant,
    Area,
}

/// <summary>How a trip listing is ordered. The value is the sort word a caller may ask for.</summary>
internal enum TripListSort
{
    /// <summary>Most recent first — what the list has always answered when nothing is asked.</summary>
    DateDescending = 0,
    DateAscending,
    TitleAscending,
    TitleDescending,
    CreatedDescending,
    CreatedAscending,
    UpdatedDescending,
    UpdatedAscending,
}

/// <summary>Everything a caller may narrow or order the trip listing by, already parsed.</summary>
internal sealed record TripLogListFilter(
    DateOnly? From,
    DateOnly? To,
    Guid? CaveId,
    Guid? ExpeditionId,
    IReadOnlyList<Guid> ParticipantIds,
    IReadOnlyList<Guid> AreaIds,
    IReadOnlyList<long> TypeIds,
    IReadOnlyList<ActivityState> States,
    IReadOnlyList<Visibility> Visibilities,
    bool? HadIncident,
    string? Search,
    TripListSort Sort);

/// <summary>
/// The one place a trip listing is composed: the audience walk, the narrowings a caller asked
/// for, and the order they are handed back in.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the page, the number printed above the page and the number printed beside
/// every facet option are three readings of one question, and three hand-written compositions of
/// the same predicates is how they come to disagree. A count that says twelve over a page that
/// shows four is worse than no count at all: it tells the reader they are being shown less than
/// they may see, which for a caller who genuinely may not see everything is exactly the sentence
/// this application must not say by accident.
/// </para>
/// <para>
/// So every narrowing is composed <em>into</em> the visibility-filtered query and nothing is ever
/// filtered afterwards. Two callers therefore get different numbers for the same option and both
/// are right — the facet counts are counts of what that caller may read, not of what exists.
/// </para>
/// <para>
/// Naming something the caller may not read does not refuse and does not explain: it answers as
/// though nothing were linked, empty page and every count zero. An id that answers differently
/// from one that does not exist is an id anybody can go looking for, and for a cave the answer
/// would be the trips that reached it, each carrying its own geometry, which places the cave the
/// listing would not name.
/// </para>
/// </remarks>
internal sealed class TripLogListing
{
    private readonly SilexGisDbContext db;
    private readonly IQueryable<TripLog> visible;
    private readonly TripLogListFilter filter;
    private readonly IQueryable<Guid>? caveTrips;
    private readonly IQueryable<Guid>? campTrips;
    private readonly IQueryable<Guid>? areaTrips;
    private readonly IQueryable<Guid>? participantTrips;

    private TripLogListing(
        SilexGisDbContext db,
        IQueryable<TripLog> visible,
        TripLogListFilter filter,
        bool blocked,
        IQueryable<Guid>? caveTrips,
        IQueryable<Guid>? campTrips,
        IQueryable<Guid>? areaTrips,
        IQueryable<Guid>? participantTrips)
    {
        this.db = db;
        this.visible = visible;
        this.filter = filter;
        Blocked = blocked;
        this.caveTrips = caveTrips;
        this.campTrips = campTrips;
        this.areaTrips = areaTrips;
        this.participantTrips = participantTrips;
    }

    /// <summary>
    /// A filter named something this caller may not read. The whole answer is empty — no page, no
    /// totals, no option counts — and the caller is told nothing about whether the thing exists.
    /// </summary>
    public bool Blocked { get; }

    public TripLogListFilter Filter => filter;

    /// <summary>
    /// Every trip this caller may read, before any narrowing. The denominator of the
    /// filtered-of-total figure, so that figure is about this caller and not about the database.
    /// </summary>
    public IQueryable<TripLog> Visible => visible;

    public static async Task<TripLogListing> ResolveAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        TripLogListFilter filter,
        CancellationToken ct)
    {
        var visible = db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);

        IQueryable<Guid>? caveTrips = null;
        if (filter.CaveId is { } caveId)
        {
            // Two gates, neither implying the other: a cave the listing would not name may not be
            // used to narrow it, and a cave whose position is guarded is placed by the geometries
            // of the trips that reached it even when the cave itself is readable.
            if ((await TripCaveDisclosure.DisclosableCaveIdsAsync(db, protection, ctx, [caveId], ct)).Count == 0)
            {
                return Blocked_(db, visible, filter);
            }

            // Role-agnostic: the question is which trips this cave is named on, not what they did
            // there. Narrowing it to one role would quietly answer a smaller question.
            caveTrips = TripRoleLinks.TripIdsNaming(db, caveId);
        }

        IQueryable<Guid>? campTrips = null;
        if (filter.ExpeditionId is { } expeditionId)
        {
            var readableCamp = await db.Expeditions.AsNoTracking()
                .VisibleTo(ctx, AccessDomain.Expeditions)
                .AnyAsync(x => x.Id == expeditionId, ct);
            if (!readableCamp)
            {
                return Blocked_(db, visible, filter);
            }

            campTrips = db.ExpeditionTrips.AsNoTracking()
                .Where(m => m.ExpeditionId == expeditionId)
                .Select(m => m.TripLogId);
        }

        IQueryable<Guid>? areaTrips = null;
        if (filter.AreaIds.Count > 0)
        {
            // Each area with everything the containment hierarchy puts inside it — because a
            // massif that answered only for trips naming the massif itself would leave out every
            // trip that named one of its sub-areas, which is most of them — with the same two
            // gates the cave arm above applies, on the area and on every feature inside it.
            // Descending into an area without them would be a second way to ask the cave question
            // with the position check missing: the caves an area contains are inside it.
            //
            // The chosen areas are alternatives, so their sets are unioned. One of them this
            // caller may not read stops the whole answer rather than being dropped: a filter that
            // quietly widened when one of its terms was refused would show a reader more than
            // they asked for and tell them nothing about it.
            var within = new HashSet<Guid>();
            foreach (var areaId in filter.AreaIds)
            {
                var reached = await TripAreaReach.WithinAsync(db, protection, ctx, areaId, ct);
                if (reached is null)
                {
                    return Blocked_(db, visible, filter);
                }

                within.UnionWith(reached);
            }

            areaTrips = TripRoleLinks.TripIdsNamingAny(db, within);
        }

        IQueryable<Guid>? participantTrips = null;
        if (filter.ParticipantIds.Count > 0)
        {
            // Narrowing by a person answers only over trips this caller may already open, and
            // every one of those already carries its roster — so this asks nothing the caller
            // could not assemble by reading the pages. What it must not become is a way to ask
            // about somebody whose roster entry the caller may not read at all, so the roster's
            // own rule is asked first, at the object level, and a person it refuses answers as
            // though they had been on nothing — for the whole filter, because one refused term
            // silently dropped would widen the answer without saying so.
            foreach (var participantId in filter.ParticipantIds)
            {
                var mayRead = AccessEvaluator.Decide(
                    ctx,
                    AccessDomain.Cavers,
                    AccessAction.Read,
                    new AccessTargetFacts { ObjectId = participantId }).Allowed;
                if (!mayRead || !await db.Cavers.AsNoTracking().AnyAsync(c => c.Id == participantId, ct))
                {
                    return Blocked_(db, visible, filter);
                }
            }

            // The chosen people are alternatives: a trip either of them was on is kept, which is
            // the reading the panel's own rule promises and the one a reader predicts.
            var people = filter.ParticipantIds;
            participantTrips = db.TripLogParticipants.AsNoTracking()
                .Where(p => people.Contains(p.CaverId))
                .Select(p => p.TripLogId);
        }

        return new TripLogListing(db, visible, filter, false, caveTrips, campTrips, areaTrips, participantTrips);
    }

    private static TripLogListing Blocked_(SilexGisDbContext db, IQueryable<TripLog> visible, TripLogListFilter filter) =>
        new(db, visible, filter, true, null, null, null, null);

    /// <summary>
    /// The caller's trips with every narrowing composed in, optionally leaving one facet's own
    /// choices out so that facet's options can be counted.
    /// </summary>
    /// <remarks>
    /// Within a facet the chosen values are an OR and across facets they are an AND, and a facet
    /// nobody has chosen anything in is composed at all — an empty choice is "no opinion", not
    /// "nothing matches". That is the only reading a reader predicts, and it is what makes
    /// clearing a facet the same thing as never having touched it.
    /// </remarks>
    public IQueryable<TripLog> Narrowed(TripListFacet except = TripListFacet.None)
    {
        var query = visible.OverlappingDays(x => x.TripDate, x => x.TripDateEnd, filter.From, filter.To);

        if (caveTrips is not null)
        {
            var trips = caveTrips;
            query = query.Where(x => trips.Contains(x.Id));
        }

        if (campTrips is not null)
        {
            var trips = campTrips;
            query = query.Where(x => trips.Contains(x.Id));
        }

        if (areaTrips is not null && except != TripListFacet.Area)
        {
            var trips = areaTrips;
            query = query.Where(x => trips.Contains(x.Id));
        }

        if (participantTrips is not null && except != TripListFacet.Participant)
        {
            var trips = participantTrips;
            query = query.Where(x => trips.Contains(x.Id));
        }

        if (filter.TypeIds.Count > 0 && except != TripListFacet.Type)
        {
            var ids = filter.TypeIds;
            query = query.Where(x => x.TripTypeId != null && ids.Contains(x.TripTypeId.Value));
        }

        if (filter.States.Count > 0 && except != TripListFacet.State)
        {
            var states = filter.States;
            query = query.Where(x => states.Contains(x.State));
        }

        if (filter.Visibilities.Count > 0 && except != TripListFacet.Visibility)
        {
            var visibilities = filter.Visibilities;
            query = query.Where(x => visibilities.Contains(x.Visibility));
        }

        if (filter.HadIncident is { } incident && except != TripListFacet.Incident)
        {
            query = query.Where(x => x.HadIncident == incident);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search}%";
            query = query.Where(x => EF.Functions.ILike(EF.Functions.Unaccent(x.Title), EF.Functions.Unaccent(pattern)));
        }

        return query;
    }

    /// <summary>
    /// The order a page is handed back in. Every arm settles on the identifier, which is
    /// time-ordered, rather than on a timestamp: two rows written in the same tick tie again, and
    /// a tie in the order is a row that appears on two pages or on none.
    /// </summary>
    public IOrderedQueryable<TripLog> Ordered(IQueryable<TripLog> query) => filter.Sort switch
    {
        TripListSort.DateAscending => query.OrderBy(x => x.TripDate).ThenBy(x => x.Id),
        TripListSort.TitleAscending => query.OrderBy(x => x.Title).ThenBy(x => x.Id),
        TripListSort.TitleDescending => query.OrderByDescending(x => x.Title).ThenBy(x => x.Id),
        TripListSort.CreatedAscending => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
        TripListSort.CreatedDescending => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
        TripListSort.UpdatedAscending => query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
        TripListSort.UpdatedDescending => query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id),
        _ => query.OrderByDescending(x => x.TripDate).ThenByDescending(x => x.Id),
    };
}
