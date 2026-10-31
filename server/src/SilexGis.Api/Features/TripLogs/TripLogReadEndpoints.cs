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
    /// <summary>
    /// The narrowed listing as a file.
    /// </summary>
    /// <remarks>
    /// Taken from the same composition the page is, and mapped through the same mapping, so the
    /// file holds what the screen would have shown and nothing the screen would have withheld.
    /// A filter naming something this caller may not read exports an empty sheet rather than
    /// refusing, for the reason the page answers empty: an id that answers differently from one
    /// that does not exist is an id anybody can go looking for. A <c>filter</c> or saved filter of
    /// the trip world is a different thing — a document that cannot be read or run is refused with
    /// its code, because answering it as no filter would hand over every trip as though it were a
    /// selection.
    /// </remarks>
    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ISpreadsheetWriter sheets,
        IOptions<TripListOptions> listOptions,
        SurfaceFilters surfaceFilters,
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
        CancellationToken ct,
        string? filter = null,
        Guid? savedFilterId = null)
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
                out var listFilter, out var problem))
        {
            return problem!;
        }

        // The trip world's own filter, beside the list's grammar rather than instead of it: the
        // two are asked together and a trip must satisfy both. Read before anything is queried so
        // a filter that cannot be read is a refusal and never a file of everything.
        var narrowing = await surfaceFilters.ForAsync<TripLog>(ctx, TripLogFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        var listing = await TripLogListing.ResolveAsync(db, protection, ctx, listFilter, ct);

        var limit = listOptions.Value.MaxExportedTrips;
        var rows = listing.Blocked
            ? []
            : await listing.Ordered(narrowing.Narrowing!.Apply(listing.Narrowed())).Take(limit + 1).ToListAsync(ct);
        var truncated = rows.Count > limit;
        if (truncated)
        {
            rows = [.. rows.Take(limit)];
        }

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, rows, ct);
        var bytes = sheets.Write(
            TripLogExportWorkbook.SheetName,
            TripLogExportWorkbook.Rows(items, truncated, limit));

        // Named by what it is about and by when it was taken, never by anything in it: a file
        // name travels through mail clients and download folders that nothing here controls.
        var fileName = $"trips-{DateTime.UtcNow:yyyyMMdd}.{sheets.Extension}";
        return TypedResults.File(bytes, sheets.ContentType, fileName);
    }

    /// <summary>
    /// The narrowed listing, sliced.
    /// </summary>
    /// <remarks>
    /// Cut from the same composition the page and the option counts are, so a slice's count and
    /// the rows that slice produces cannot drift apart, and a caller who may not read everything
    /// gets slices of what they may read rather than of what exists.
    /// </remarks>
    private static async Task<Results<Ok<TripListGroupingDto>, UnauthorizedHttpResult, ProblemHttpResult>> GroupingAsync(
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
        string? groupBy,
        string? thenBy,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The order a page would be in changes no slice, so it is not a parameter here.
        if (!TripListQuery.TryParse(
                from, to, caveId, expeditionId, participantIds, areaIds,
                types, states, visibilities, hadIncident, search, null,
                out var filter, out var problem))
        {
            return problem!;
        }

        if (!TripLogGrouping.TryParseDimension(groupBy, out var primary))
        {
            return ApiProblems.BadRequest(GroupInvalidCode, $"Cannot group trips by '{groupBy}'.");
        }

        if (!TripLogGrouping.TryParseDimension(thenBy, out var secondary))
        {
            return ApiProblems.BadRequest(GroupInvalidCode, $"Cannot group trips by '{thenBy}'.");
        }

        if (primary == TripGroupDimension.None)
        {
            // Nothing to slice by is not a refusal: it is the panel's first choice, and it means
            // the reader wants the table and no shape above it.
            return TypedResults.Ok(TripListGroupingDto.Empty);
        }

        if (secondary == primary)
        {
            // Slicing a slice by the thing it was already cut on answers one child per parent,
            // which is a shape that says nothing. Refused rather than silently ignored, so a
            // panel that offers the pair learns it here rather than shipping the empty answer.
            return ApiProblems.BadRequest(GroupInvalidCode, "The two grouping levels must differ.");
        }

        var listing = await TripLogListing.ResolveAsync(db, protection, ctx, filter, ct);
        if (listing.Blocked)
        {
            return TypedResults.Ok(TripListGroupingDto.Empty);
        }

        return TypedResults.Ok(
            await TripLogGrouping.BuildAsync(
                db, protection, ctx, user, listing, primary, secondary, listOptions.Value.MaxGroupedTrips, ct));
    }

    /// <summary>
    /// The narrowed listing, totalled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own route rather than a shape on the listing, and taking exactly the listing's own
    /// narrowings so that "the current filter" means one thing on both screens. A figure worked
    /// out from a second, similar-looking filter would disagree with the list under the one
    /// condition nobody tests: a caller who may read only part of the archive.
    /// </para>
    /// <para>
    /// The order a page would be in changes no total, so it is not a parameter here — but a
    /// bad word in any of the shared ones is refused with the same code the listing uses, or a
    /// page would draw charts for a filter the listing rejects.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TripStatsDto>, UnauthorizedHttpResult, ProblemHttpResult>> StatsAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
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
            return TypedResults.Ok(TripStatsDto.Empty);
        }

        return TypedResults.Ok(await TripLogStats.BuildAsync(db, protection, ctx, user, listing, ct));
    }

    // An option's value is the word the listing's own parameter takes, which is the word every
    // other field of every other answer spells it with. Handing back the name the enum carries
    // in code would still filter — the listing reads a word without regard to case — but a panel
    // could not look the option's own translation up under it, and comparing what somebody has
    // chosen against what is offered would miss every time.
    private static string WireWord<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString()!);

    /// <summary>
    /// The caller's own trips, soonest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whose trips these are is resolved from the request's own identity and from nothing
    /// supplied. That is the whole shape of this route, not an implementation detail: a
    /// parameter naming a person would let somebody assemble where that person has been out of
    /// trips they may never open, and the size of the answer gives it away even when no row
    /// comes back — ask once, ask again with a different window, and the difference is when that
    /// person was underground. Filtering trips by participant is refused everywhere else in this
    /// application for exactly that reason, and a query string is the same request with
    /// different spelling. So there is no participant parameter here, under any name, and adding
    /// one would undo a refusal the rest of the code takes seriously.
    /// </para>
    /// <para>
    /// Being on a trip is not a right to read it. The caller's ordinary reading is applied first
    /// and this narrows what is left, so a trip somebody was asked about and then shut out of
    /// disappears from their own list — which is correct: the list is a view of records, and a
    /// record nobody may read is not shown by a different door.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<TripLogDto>>, UnauthorizedHttpResult, ProblemHttpResult>> MineAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        int? page,
        int? pageSize,
        DateOnly? from,
        DateOnly? to,
        string? state,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Lifecycle states arrive as the camelCase words the rest of the contract spells them
        // with, parsed here rather than by route binding: binding a bad word would answer with a
        // bare 400 carrying no code, and a client cannot tell that apart from any other refusal.
        // A word this application does not have, and one a trip cannot hold, are refused alike —
        // a caller who asked for something that cannot exist wants to be told, not handed the
        // whole list.
        ActivityState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!RouteEnums.TryParse<ActivityState>(state, out var stateValue)
                || !ActivityStates.IsTripLogState(stateValue))
            {
                return ApiProblems.BadRequest(StateInvalidCode, $"Unknown state '{state}'.");
            }

            stateFilter = stateValue;
        }

        var query = db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);

        // Being asked counts as being on it, and saying no counts as not being on it. Somebody
        // invited and not yet written onto the roster has the trip in their diary as much as
        // anybody already named; somebody who declined does not, and would otherwise fill a short
        // list with the weekends they turned down while the trip they are going on falls off the
        // end. Being written onto the party regardless is the organiser overruling the answer, and
        // puts the trip back.
        var mine = TripAudience.TripIdsTheAccountIsOn(db, user.UserId);
        query = query.Where(x => mine.Contains(x.Id));

        // Coming up means from today onwards unless the caller says otherwise, so the default is
        // a floor rather than a fixed window: a list of what somebody is going on is useless if
        // it opens on last winter. Dates here are the trip's own calendar days and today is read
        // in UTC, which is the only clock this application stores — a trip on the boundary can
        // therefore appear or drop a few hours early or late for a reader far from it, and that
        // is preferable to a per-reader answer nothing else in the application gives.
        //
        // An explicit window is honoured as asked, backwards included. Every row here is a trip
        // the caller is on and may already read, so widening it discloses nothing that was being
        // withheld; the same list then answers "what have I been on" without a second door.
        var windowStart = from ?? DateOnly.FromDateTime(DateTime.UtcNow);

        query = query.OverlappingDays(x => x.TripDate, x => x.TripDateEnd, windowStart, to);

        // No state is excluded by default, and that is a decision rather than an omission. A trip
        // the caller is on that has been called off is exactly the thing they most need to see on
        // a list of what is coming up, and hiding it would make the list quietly disagree with the
        // trip's own page. Narrowing is offered instead, so a surface that wants only what is
        // going ahead asks for it and says so.
        if (stateFilter is { } wantedState)
        {
            query = query.Where(x => x.State == wantedState);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        // Ascending, which is the opposite of every other trip listing and is the point of this
        // one: the next thing somebody is going on is the row they came for, so it is the first.
        // The tie-break is the primary key and has to be — two trips on the same day are
        // ordinary, and an order that does not separate them lets a row appear on two pages or on
        // none as the database chooses. A timestamp is the same bug one step further away, since
        // two rows written in the same tick tie again; the identifiers are time-ordered, so
        // ascending by id reads as "the one entered first" among trips that start together.
        var rows = await query.OrderBy(x => x.TripDate).ThenBy(x => x.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, rows, ct);
        return TypedResults.Ok(new PagedResult<TripLogDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        var items = await MapWithChildrenAsync(db, access, protection, ctx!, user!, [trip], ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Ok(items[0]);
    }
}
