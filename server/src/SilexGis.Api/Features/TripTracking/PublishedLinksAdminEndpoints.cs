// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Everything this installation has published, for the people who answer for the installation.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> A published link is managed from its own trip, by whoever may write
/// that trip. Nothing answered the question an installation's administrator actually gets asked —
/// "what do we have out on the internet right now?" — short of opening every trip in turn, and a
/// link handed out by somebody who has since left was known to nobody.
/// </para>
/// <para>
/// <b>Every status here is the routes' own answer.</b> What a link does for its holder is decided
/// by <see cref="TripPublicationStatus"/>, which is also what the anonymous routes refuse or serve
/// by, with the cave's refusal taken from the same <see cref="TrackingWithholding"/> call they
/// make. A status computed any other way would sooner or later describe a page that answers
/// differently, and a list an administrator cannot trust is worse than none.
/// </para>
/// <para>
/// <b>Full administrators only.</b> The list crosses every trip's own access rules on purpose —
/// it names trips, their caves and who published them, across the installation — and it is what a
/// withdrawal of everything is read against, so it has to be complete rather than narrowed to what
/// one member happens to see. That is a power, not a view, and it sits with the one group whose
/// reach no access entry can reduce.
/// </para>
/// <para>
/// It lives beside the publication routes rather than with the other administration pages because
/// it must ask the cave's refusal exactly as they do, and that is this slice's own business.
/// </para>
/// <para>
/// <b>And taking it back, a trip at a time or all at once.</b> The two withdrawals here do to many
/// links what the trip's own route does to one, and nothing else: each link is stamped as taken
/// back, through the same tracked entity, so each leaves the same trail. Neither can be undone.
/// A link taken back opens nothing again, and a finished trip whose last link is gone leaves its
/// cave's history and can be published again only by starting its watch again — which is why
/// there is no switch here that hides everything for a while and brings it back: nothing a link
/// stores could tell "hidden for now" from "withdrawn", and an address that might start answering
/// again is not one anybody can call withdrawn.
/// </para>
/// </remarks>
public static class PublishedLinksAdminEndpoints
{
    /// <summary>
    /// How many characters of the stored hash identify a link in this list. The request log writes
    /// the same number of the same hash in place of a token, which is the point: a log line and a
    /// row here name the same link. A test holds the two together.
    /// </summary>
    internal const int HandleLength = 8;

    public static RouteGroupBuilder MapPublishedLinksAdminEndpoints(this RouteGroupBuilder api)
    {
        var published = api.MapGroup("/admin/published-trips").WithTags("Admin");

        published.MapGet("/", ListAsync)
            .WithValidation<PublishedLinksQuery>()
            .WithSummary("Every published trip link of the installation, with what each does for its holder right now.")
            .WithDescription(
                "For full administrators. One row per link, revoked ones included; a link whose trip or "
                + "watch is gone is not listed, and opens nothing. status narrows to one status, spelled as "
                + "the answers spell it; sort is one of createdAt (the default, newest first), expiresAt, "
                + "tripDate, tripTitle, status; descending reverses a named order. Every status is decided "
                + "at the one instant the answer names, by the rules the published pages are served by. "
                + "No token is carried: a link is named by a short prefix of its stored hash, the same "
                + "handle the request log writes.");

        published.MapPost("/{tripLogId:guid}/revoke-all", RevokeTripAsync)
            .WithSummary("Take back every link of one trip in one act. Cannot be undone.")
            .WithDescription(
                "For full administrators. Every link of the trip that nobody had taken back is taken "
                + "back; the answer says how many. The trip's published page stops answering, and a "
                + "finished trip leaves its cave's public history — it is published again only by "
                + "starting its watch again. Asked twice, the second answer is zero.");

        // Its own path and its own word in the body, on purpose. A request built for one trip that
        // lost its trip id does not land here — the path is a different one, not the same one with
        // a segment missing — and a request that does land here without the word is refused before
        // anything is read. Withdrawing everything is done by somebody who typed that they meant to.
        published.MapPost("/revoke-everything", RevokeEverythingAsync)
            .WithValidation<RevokeEverythingRequest>()
            .WithSummary("Take back every link of the installation in one act. Cannot be undone.")
            .WithDescription(
                "For full administrators. The body must carry confirm: \"" + RevokeEverythingRequest.Word
                + "\". Every link nobody had taken back is taken back; the answer says how many, over "
                + "how many trips. Every published page stops answering and every cave's public history "
                + "of past trips empties; each trip is published again only by starting its watch again.");

        return api;
    }

    /// <summary>
    /// How many times a withdrawal looks again for links that appeared while it was writing.
    /// </summary>
    private const int WithdrawalPasses = 4;

    /// <summary>
    /// How many times in all a withdrawal starts a pass over because somebody else took back one
    /// of the links it had read. Past this the act fails rather than answer with a count it
    /// cannot stand behind; what earlier passes took back stays taken back.
    /// </summary>
    private const int WithdrawalConflictRetries = 8;

    private static async Task<Results<Ok<PublishedLinksWithdrawnDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        RevokeTripAsync(
            Guid tripLogId, SilexGisDbContext db, IAccessContextAccessor accessAccessor, TimeProvider clock,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return TypedResults.Unauthorized();
        if (!ctx.IsFullAdmin) return ApiProblems.Forbidden("access.forbidden");

        // Asked of the trip rather than inferred from finding no links, so that a mistyped id is a
        // refusal and not a reassuring "none withdrawn". The caller reads every trip, so saying
        // that one does not exist tells them nothing they may not know.
        if (!await db.TripLogs.AsNoTracking().AnyAsync(t => t.Id == tripLogId, ct))
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        return TypedResults.Ok(await WithdrawAsync(db, clock, tripLogId, ct));
    }

    private static async Task<Results<Ok<PublishedLinksWithdrawnDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        RevokeEverythingAsync(
            RevokeEverythingRequest request, SilexGisDbContext db, IAccessContextAccessor accessAccessor,
            TimeProvider clock, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return TypedResults.Unauthorized();
        if (!ctx.IsFullAdmin) return ApiProblems.Forbidden("access.forbidden");

        // Asked again here although the validator has asked it: this is the one act of the
        // application that cannot be narrowed afterwards, and it must not come to depend on a
        // filter that somebody can take off the route without touching this method.
        if (!RevokeEverythingRequest.Confirms(request.Confirm))
        {
            return ApiProblems.BadRequest("validation.failed", RevokeEverythingRequest.Refusal);
        }

        return TypedResults.Ok(await WithdrawAsync(db, clock, onlyTrip: null, ct));
    }

    /// <summary>
    /// Takes back every standing link, of one trip or of all of them, and says how many.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each link is loaded and stamped as an entity rather than updated as a set, so every one of
    /// them leaves the trail a single revocation leaves, naming who did it. A link already taken
    /// back is not touched and keeps the instant it was taken back at.
    /// </para>
    /// <para>
    /// <b>That holds for a link somebody else takes back while this is running, too.</b> A link is
    /// written by its id alone, so a pass that read it as standing would otherwise stamp it over
    /// the instant its coordinator had just revoked or replaced it at, file that under the
    /// administrator's name and count it. Each pass therefore reads and writes on one snapshot:
    /// the database then refuses the write of a row that changed after the pass read it, the pass
    /// is thrown away whole — nothing of it was written — and started again, and the second read
    /// no longer finds that link. Only links this act stamped are counted and leave its trail.
    /// </para>
    /// <para>
    /// The links are read through the ordinary set, like every other read of them: what that set
    /// does not show — a link the application treats as gone with its trip — opens nothing either,
    /// and is not counted here.
    /// </para>
    /// <para>
    /// <b>It looks again after writing.</b> A link handed out, or exchanged for a fresh one, while
    /// the first pass was writing is not among the rows that pass read, and "everything" would
    /// then have left one standing with the answer saying otherwise. So the read is repeated until
    /// it finds nothing, a bounded number of times; a link published after the last look is a later
    /// act by somebody else, and the list is where it shows.
    /// </para>
    /// </remarks>
    private static async Task<PublishedLinksWithdrawnDto> WithdrawAsync(
        SilexGisDbContext db, TimeProvider clock, Guid? onlyTrip, CancellationToken ct)
    {
        // One instant for the whole act, so the links of one withdrawal are dated alike.
        var now = clock.GetUtcNow();
        var links = 0;
        var trips = new HashSet<Guid>();
        var conflicts = 0;

        for (var pass = 0; pass < WithdrawalPasses;)
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

            var standing = await db.TripTrackingShares
                .Where(s => s.RevokedAt == null && (onlyTrip == null || s.TripLogId == onlyTrip))
                .ToListAsync(ct);
            if (standing.Count == 0) break;

            foreach (var link in standing)
            {
                link.RevokedAt = now;
            }

            try
            {
                // One save in one transaction: a pass withdraws all it read or none of it.
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception e) when (
                TripTrackingPublicationEndpoints.IsConcurrentUpdate(e)
                && conflicts < WithdrawalConflictRetries)
            {
                // One of these links was taken back or replaced by somebody else after this pass
                // read it. Nothing of the pass was written; forget what it meant to write and
                // read again. It does not count as a look: no look was completed.
                conflicts++;
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                continue;
            }

            links += standing.Count;
            trips.UnionWith(standing.Select(link => link.TripLogId));
            // The links just stamped are done with; the next look reads the table afresh.
            db.ChangeTracker.Clear();
            pass++;
        }

        return new PublishedLinksWithdrawnDto(links, trips.Count);
    }

    /// <summary>One link with the facts of its trip and watch that its status is read from.</summary>
    private sealed record LinkRow(
        Guid Id,
        string TokenHash,
        Guid CreatedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? RevokedAt,
        Guid TripLogId,
        string TripTitle,
        DateOnly TripDate,
        DateOnly? TripDateEnd,
        TripTrackingState WatchState,
        DateTimeOffset? WatchClosedAt,
        Guid? CaveFeatureId,
        Guid? SurveyModelId);

    private static async Task<Results<Ok<PublishedLinksDto>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        [AsParameters] PublishedLinksQuery query,
        SilexGisDbContext db,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IOptions<TripTrackingOptions> live,
        IOptions<TripPastTrackOptions> past,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        // A word that names no member is refused here, once the caller is known to be allowed to
        // ask at all, under the one code a closed vocabulary on a query string is refused by.
        PublishedLinkStatus? wanted = null;
        if (query.Status is not null)
        {
            if (!RouteEnums.TryParse<PublishedLinkStatus>(query.Status, out var status))
            {
                return RouteEnums.Invalid<PublishedLinkStatus>("status");
            }

            wanted = status;
        }

        var sort = PublishedLinkSort.CreatedAt;
        if (query.Sort is not null)
        {
            if (!RouteEnums.TryParse<PublishedLinkSort>(query.Sort, out var named))
            {
                return RouteEnums.Invalid<PublishedLinkSort>("sort");
            }

            sort = named;
        }
        // Newest first when nothing was asked; a named order ascends unless told otherwise.
        var descending = query.Descending ?? query.Sort is null;
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);

        // One reading of the clock for the whole answer. A status is a comparison against now, and
        // two links compared against two different instants could be shown in a combination that
        // never existed — a trip both in grace by one row and in the archive by the next.
        var now = clock.GetUtcNow();

        // Every link, with its trip and its watch, through the same join the anonymous routes
        // resolve a token by — so a link whose trip or watch is gone is absent here for the reason
        // it opens nothing there. Read whole rather than paged in the database, deliberately: the
        // status is decided by the Domain rules in memory, and filtering, ordering and counting by
        // it in SQL would need those rules written out a second time in another language, which is
        // the one thing this list must not do. The table grows by a row each time somebody
        // publishes a trip, so "all of it" is hundreds of narrow rows on a busy installation.
        var rows = await (
            from link in db.TripTrackingShares.AsNoTracking()
            join log in db.TripLogs.AsNoTracking() on link.TripLogId equals log.Id
            join watch in db.TripTrackings.AsNoTracking() on log.Id equals watch.TripLogId
            select new LinkRow(
                link.Id,
                link.TokenHash,
                link.CreatedBy,
                link.CreatedAt,
                link.ExpiresAt,
                link.RevokedAt,
                log.Id,
                log.Title,
                log.TripDate,
                log.TripDateEnd,
                watch.State,
                watch.ClosedAt,
                watch.CaveFeatureId,
                watch.SurveyModelId)).ToListAsync(ct);

        // "Is this trip published at all" is read off the trip's remaining links, exactly as the
        // routes read it: the latest expiry among those nobody revoked.
        var latestUnrevokedExpiry = rows
            .Where(row => row.RevokedAt is null)
            .GroupBy(row => row.TripLogId)
            .ToDictionary(g => g.Key, g => g.Max(row => row.ExpiresAt));

        // The cave's refusal, asked once for every cave a standing link is anchored to — the same
        // call, about the same caves, that each published page makes about its own.
        var anchoredCaves = rows
            .Where(row => row.RevokedAt is null && row.CaveFeatureId is not null)
            .Select(row => row.CaveFeatureId!.Value)
            .Distinct()
            .ToList();
        var publishable = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, anchoredCaves, ct);

        var graceAfterClose = live.Value.ShareGraceAfterClose;
        var archiveEnabled = past.Value.Enabled;
        var retention = past.Value.Retention;

        var judged = rows
            .Select(row => (
                Row: row,
                Status: TripPublicationStatus.OfLink(
                    now,
                    row.RevokedAt,
                    row.ExpiresAt,
                    row.WatchState,
                    row.WatchClosedAt,
                    latestUnrevokedExpiry.TryGetValue(row.TripLogId, out var expiry) ? expiry : (DateTimeOffset?)null,
                    row.TripDate,
                    row.TripDateEnd,
                    graceAfterClose,
                    archiveEnabled,
                    retention,
                    cavePublishable: row.CaveFeatureId is { } cave && publishable.Contains(cave))))
            .ToList();

        // Counted before the filter, so the figures describe the installation and stay put while a
        // reader moves between statuses.
        var tally = judged.GroupBy(j => j.Status).ToDictionary(g => g.Key, g => g.Count());
        var counts = Enum.GetValues<PublishedLinkStatus>()
            .Select(each => new PublishedLinkStatusCountDto(each, tally.GetValueOrDefault(each)))
            .ToList();

        var matching = wanted is { } only ? judged.Where(j => j.Status == only) : judged;

        // The link's id breaks every tie, in the direction asked: ids are time-ordered, and a page
        // boundary that fell between two links equal on the sort key would otherwise be free to
        // show one of them twice and the other never.
        var keyed = sort switch
        {
            PublishedLinkSort.ExpiresAt => Ordered(matching, j => j.Row.ExpiresAt, descending),
            PublishedLinkSort.TripDate => Ordered(matching, j => j.Row.TripDate, descending),
            PublishedLinkSort.TripTitle => Ordered(
                matching, j => j.Row.TripTitle, descending, StringComparer.OrdinalIgnoreCase),
            PublishedLinkSort.Status => Ordered(matching, j => j.Status, descending),
            _ => Ordered(matching, j => j.Row.CreatedAt, descending),
        };
        var ordered = (descending ? keyed.ThenByDescending(j => j.Row.Id) : keyed.ThenBy(j => j.Row.Id)).ToList();

        var listed = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Names for the page only. The caves are read under the ordinary filters, so one that has
        // been deleted is named by nothing; no visibility narrowing, because the caller is a full
        // administrator and may read every cave. A name and an id — never a position.
        var caveIds = listed
            .Where(j => j.Row.CaveFeatureId is not null)
            .Select(j => j.Row.CaveFeatureId!.Value)
            .Distinct()
            .ToList();
        Dictionary<Guid, string?> caveNames = caveIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .Where(f => caveIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Name })
                .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        // The warning about a protected neighbour, for the links of this page that open something
        // — a link that opens nothing hands out no survey to warn about. Asked as the caller, who
        // may place every cave, so here the answer is the complete one.
        var surveys = listed
            .Where(j => OpensSomething(j.Status))
            .Select(j => SurveyOf(j.Row))
            .OfType<PublishedSurveyBounds.Survey>()
            .Distinct()
            .ToList();
        var reaching = await PublishedSurveyBounds.ReachingProtectedCavesAsync(db, protection, ctx, surveys, ct);

        // Resolved after the page materialises rather than joined in: what an account may be shown
        // as is a rule with one home, and this slice may not read a user row itself.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, listed.Select(j => j.Row.CreatedBy), ct);

        var items = listed
            .Select(j => new PublishedLinkDto(
                j.Row.Id,
                j.Row.TokenHash[..Math.Min(HandleLength, j.Row.TokenHash.Length)],
                j.Row.TripLogId,
                j.Row.TripTitle,
                j.Row.TripDate,
                j.Row.TripDateEnd,
                j.Row.CaveFeatureId is { } cave
                    ? new PublishedLinkCaveDto(cave, caveNames.GetValueOrDefault(cave))
                    : null,
                j.Row.WatchState,
                j.Row.WatchClosedAt,
                j.Row.CreatedBy,
                labels.GetValueOrDefault(j.Row.CreatedBy),
                j.Row.CreatedAt,
                j.Row.ExpiresAt,
                j.Row.RevokedAt,
                j.Status,
                OpensSomething(j.Status)
                    && SurveyOf(j.Row) is { } survey
                    && reaching.Contains(survey)))
            .ToList();

        return TypedResults.Ok(new PublishedLinksDto(
            items,
            page,
            pageSize,
            ordered.Count,
            now,
            counts,
            live.Value.PublishRealNames,
            archiveEnabled));
    }

    /// <summary>Whether a link in this status hands anything to its holder, the survey included.</summary>
    private static bool OpensSomething(PublishedLinkStatus status) =>
        status is PublishedLinkStatus.Followable or PublishedLinkStatus.InGrace or PublishedLinkStatus.InArchive;

    /// <summary>The survey a link's watch is drawn on, or null when the watch has none.</summary>
    private static PublishedSurveyBounds.Survey? SurveyOf(LinkRow row) =>
        row is { SurveyModelId: { } model, CaveFeatureId: { } cave }
            ? new PublishedSurveyBounds.Survey(model, cave)
            : null;

    private static IOrderedEnumerable<T> Ordered<T, TKey>(
        IEnumerable<T> source, Func<T, TKey> key, bool descending, IComparer<TKey>? comparer = null) =>
        descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
}
