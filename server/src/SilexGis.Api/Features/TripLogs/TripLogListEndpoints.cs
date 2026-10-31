// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

public static partial class TripLogEndpoints
{
    private static async Task<Results<Ok<PagedResult<TripLogDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        Guid? caveId,
        Guid? expeditionId,
        string? areaIds,
        string? participantIds,
        string? types,
        string? states,
        string? visibilities,
        bool? hadIncident,
        string? search,
        string? sort,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TripListQuery.TryParse(
                from, to, caveId, expeditionId, participantIds, areaIds,
                types, states, visibilities, hadIncident, search, sort,
                out var filter, out var problem))
        {
            return problem!;
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var listing = await TripLogListing.ResolveAsync(db, protection, ctx, filter, ct);
        if (listing.Blocked)
        {
            return TypedResults.Ok(new PagedResult<TripLogDto>([], p, size, 0));
        }

        var query = listing.Narrowed();
        var total = await query.CountAsync(ct);
        var rows = await listing.Ordered(query).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, rows, ct);
        return TypedResults.Ok(new PagedResult<TripLogDto>(items, p, size, total));
    }

    /// <summary>
    /// How many trips each option in the filter panel would leave.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own route rather than a second shape on the listing, because it is a different
    /// question about the same query: the page is a page of trips, and this is a set of counts
    /// over every trip the filter reaches. Bolting it onto the listing would make every caller
    /// who only wants rows pay for the counts.
    /// </para>
    /// <para>
    /// Every count is composed into the same visibility-filtered query the page is taken from,
    /// through the one composition both use — so a number beside an option is a promise the
    /// listing keeps. A count taken any other way would state how many rows the caller was not
    /// shown, which is the one thing a listing that hides rows must never say.
    /// </para>
    /// <para>
    /// The two open-ended facets are capped, longest count first, plus whatever the caller has
    /// already chosen — which is what keeps a shared link naming somebody far down the roster from
    /// opening onto a control showing a bare identifier. Somebody past the cap who has not been
    /// chosen is not offered: the panel filters the values it was sent and asks no roster of its
    /// own, so reaching them means narrowing the rest of the filter until they surface.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripListFacetsDto>, UnauthorizedHttpResult, ProblemHttpResult>> FacetsAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        IOptions<TripListOptions> listOptions,
        DateOnly? from,
        DateOnly? to,
        Guid? caveId,
        Guid? expeditionId,
        string? areaIds,
        string? participantIds,
        string? types,
        string? states,
        string? visibilities,
        bool? hadIncident,
        string? search,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The order a page would be in changes no count, so it is not a parameter here — but a
        // bad word in any of the shared ones is refused with the same code the listing uses, or
        // a panel would show numbers for a filter the listing rejects.
        if (!TripListQuery.TryParse(
                from, to, caveId, expeditionId, participantIds, areaIds,
                types, states, visibilities, hadIncident, search, null,
                out var filter, out var problem))
        {
            return problem!;
        }

        var listing = await TripLogListing.ResolveAsync(db, protection, ctx, filter, ct);
        if (listing.Blocked)
        {
            return TypedResults.Ok(TripListFacetsDto.Empty);
        }

        var matching = await listing.Narrowed().CountAsync(ct);
        var overall = await listing.Visible.CountAsync(ct);

        var typeCounts = await listing.Narrowed(TripListFacet.Type)
            .Where(x => x.TripTypeId != null)
            .GroupBy(x => x.TripTypeId!.Value)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var stateCounts = await listing.Narrowed(TripListFacet.State)
            .GroupBy(x => x.State)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var visibilityCounts = await listing.Narrowed(TripListFacet.Visibility)
            .GroupBy(x => x.Visibility)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var incidentCounts = await listing.Narrowed(TripListFacet.Incident)
            .GroupBy(x => x.HadIncident)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Distinct by trip, not by roster row: the roster holds one row per person per job, so
        // somebody who led and surveyed the same trip is one person who went once. Counting rows
        // would say two, and the listing would then hand back one.
        var participantTripIds = listing.Narrowed(TripListFacet.Participant).Select(x => x.Id);
        var participantCounts = await db.TripLogParticipants.AsNoTracking()
            .Where(participant => participantTripIds.Contains(participant.TripLogId))
            .Select(participant => new { participant.CaverId, participant.TripLogId })
            .Distinct()
            .GroupBy(x => x.CaverId)
            .Select(g => new { CaverId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);
        var (cappedParticipants, moreParticipants) = Capped(
            [.. participantCounts.Select(x => (Value: x.CaverId, x.Count))],
            filter.ParticipantIds,
            listOptions.Value.MaxOpenFacetValues);

        var caverLabels = await CaverDirectory.ResolveLabelsAsync(
            db, user, cappedParticipants.Select(x => x.Value), ct);

        // Areas are counted through the same walk the area narrowing makes — the containment
        // hierarchy, with the audience and position gates on every feature it passes through — so
        // the number beside an area and the page selecting it produces are two readings of one
        // question. Counted any other way a massif was offered the trips that named it directly
        // and then handed back every trip that named anything inside it.
        var areaReach = await TripAreaReach.BuildAsync(
            db, protection, ctx, listing.Narrowed(TripListFacet.Area).Select(x => x.Id), ct);
        var (areaCounts, moreAreas) = Capped(
            [.. areaReach.Counts().Select(x => (Value: x.AreaId, x.Count))],
            filter.AreaIds,
            listOptions.Value.MaxOpenFacetValues);
        var chosenAreaNames = await AreaLabelsAsync(
            db, protection, ctx, areaReach.Names, [.. areaCounts.Select(x => x.Value)], ct);

        return TypedResults.Ok(new TripListFacetsDto(
            matching,
            overall,
            [.. typeCounts.OrderByDescending(x => x.Count)
                .Select(x => new TripFacetValueDto(x.Value.ToString(), null, x.Count))],
            [.. stateCounts.OrderByDescending(x => x.Count)
                .Select(x => new TripFacetValueDto(WireWord(x.Value), null, x.Count))],
            [.. visibilityCounts.OrderByDescending(x => x.Count)
                .Select(x => new TripFacetValueDto(WireWord(x.Value), null, x.Count))],
            [.. incidentCounts.OrderByDescending(x => x.Count)
                .Select(x => new TripFacetValueDto(x.Value ? "true" : "false", null, x.Count))],
            [.. cappedParticipants.Select(x => new TripFacetValueDto(
                x.Value.ToString(), caverLabels.GetValueOrDefault(x.Value), x.Count))],
            [.. areaCounts.Select(x => new TripFacetValueDto(
                x.Value.ToString(), chosenAreaNames.GetValueOrDefault(x.Value), x.Count))],
            moreParticipants,
            moreAreas));
    }

    /// <summary>
    /// The values one open-ended facet offers: the most-reached first, plus whatever the caller
    /// has already chosen — and whether a counted value was left out.
    /// </summary>
    /// <remarks>
    /// A chosen value is kept whatever the cap says, and with the count it really has. Dropping it
    /// would leave a shared link opening onto a control displaying a bare identifier with nothing
    /// able to translate it, and no way to let go of a choice the reader can no longer see. That
    /// more exist is said rather than left to be noticed, because a list that stopped short reads
    /// exactly like the whole roster; a chosen value past the cap is offered, so it is not what
    /// the flag is about.
    /// </remarks>
    private static (IReadOnlyList<(Guid Value, int Count)> Offered, bool More) Capped(
        IReadOnlyList<(Guid Value, int Count)> counted, IReadOnlyCollection<Guid> chosen, int cap)
    {
        var kept = counted.Take(cap).ToList();
        var offered = kept.Select(x => x.Value).ToHashSet();
        foreach (var id in chosen.Where(id => !offered.Contains(id)))
        {
            var known = counted.FirstOrDefault(x => x.Value == id);
            kept.Add(known.Value == id ? known : (id, 0));
            offered.Add(id);
        }

        return (kept, counted.Any(x => !offered.Contains(x.Value)));
    }

    /// <summary>
    /// Names for the areas being offered. The walk already named every area it reached; this adds
    /// the one the caller chose when the filter has narrowed the listing away from it, through the
    /// same two gates, so an area nobody may be told about stays unnamed.
    /// </summary>
    private static async Task<Dictionary<Guid, string?>> AreaLabelsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyDictionary<Guid, string?> known,
        IReadOnlyCollection<Guid> offered,
        CancellationToken ct)
    {
        var labels = known.ToDictionary(entry => entry.Key, entry => entry.Value);
        var missing = offered.Where(id => !labels.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return labels;
        }

        var rows = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => missing.Contains(f.Id) && f.Kind == FeatureKind.Generic && f.DeletedAt == null)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);
        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, [.. rows.Select(f => f.Id)], ct);
        foreach (var row in rows.Where(f => !redacted.Contains(f.Id)))
        {
            labels[row.Id] = row.Name;
        }

        return labels;
    }
}
