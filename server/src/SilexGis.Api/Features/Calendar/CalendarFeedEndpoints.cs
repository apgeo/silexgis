// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Calendar;

/// <summary>
/// The subscription feed: one account's own calendar, read by a calendar application that holds
/// the account's feed address and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Anonymous, and the first anonymous route that resolves to a person.</b> Every other token in
/// an address here opens one named thing — a feature, an album, a trip's followed page — or drives
/// one narrow write. This one is turned into an account: the token is looked up, the account's
/// grant set is assembled <i>on this request</i> exactly as it would be for the account's own
/// sign-in, and the calendar is read through that context. Nothing about the grants is stored
/// with the token, so a grant withdrawn after the address was handed out stops its rows appearing
/// on the next poll, and a test holds that to be so.
/// </para>
/// <para>
/// <b>Scoped to what the account is on, not to everything it may see.</b> A phone calendar is for
/// the trips somebody is going on and the evenings they said yes to; the alternative turns one
/// address into an installation-wide extract living for years in third-party calendar
/// infrastructure. So each family is read through the account's visibility first and then
/// intersected with the rows the account is named on, asked about and not declined, or recorded
/// as staying at — the same three rules the calendar's own "mine" narrowing applies.
/// </para>
/// <para>
/// <b>What a row carries is the title, the days, a link back and whether it was called off, and
/// nothing else</b> — no place, no position, no participants, no description. A calendar service
/// stores whatever it is handed for the life of the subscription.
/// </para>
/// <para>
/// <b>Every refusal is the one 404.</b> A malformed token, an unknown one, a revoked one, an
/// account that has been locked, and an installation that has switched feeds off all answer
/// identically, so nothing in the answer says that an address was ever real. The gate is read on
/// every poll rather than at mint, which is what makes switching it off stop every address
/// already handed out.
/// </para>
/// <para>
/// Whole, not windowed: a subscriber keeps the file and shows whichever part of it the device is
/// looking at. The one bound is a backstop on rows, applied to the oldest first, because what a
/// subscription is for is what is coming.
/// </para>
/// </remarks>
public static class CalendarFeedEndpoints
{
    /// <summary>The one refusal, for every reason there is.</summary>
    public const string NotFoundCode = "calendar_feed.not_found";

    /// <summary>
    /// The most rows one feed carries, kept from the most recent backwards. A backstop against
    /// a decade of membership rather than a page size; a phone shows a month at a time and the
    /// newest rows are the ones it is for.
    /// </summary>
    private const int MaxRows = 2000;

    /// <summary>
    /// The lifecycle states a feed carries, the same answer the calendar page draws: a draft
    /// nobody has been told about is not on anybody's phone either.
    /// </summary>
    private static readonly ActivityState[] ShownStates = [.. CalendarMembership.ShownStates];

    public static RouteGroupBuilder MapCalendarFeedEndpoints(this RouteGroupBuilder api)
    {
        // The token in the address is the whole credential, so the route is on the anonymous
        // allow-list; a calendar application can attach no header. The `.ics` suffix is for the
        // calendar applications that decide what an address is by how it ends.
        api.MapGet("/calendar/feed/{token}.ics", GetAsync)
            .WithTags("Calendar")
            .AllowAnonymous()
            .RequireRateLimiting(CalendarFeedRateLimits.PolicyName)
            .WithSummary(
                "One account's own calendar as an iCalendar feed: the trips, camps and club dates "
                + "that account is on, each as a title, its days and a link back. The address is "
                + "the credential; every refusal is the same 404.");
        return api;
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> GetAsync(
        string token,
        SilexGisDbContext db,
        IAppSettingsService settings,
        IConfiguration configuration,
        IOptions<AboutOptions> about,
        TimeProvider clock,
        CancellationToken ct)
    {
        // Refused before the hash: a kilobyte of path is not an address anybody was handed.
        if (!CalendarFeedTokens.IsPlausible(token))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var hash = CalendarFeedTokens.Hash(token);
        var feed = await db.CalendarFeedTokens.AsNoTracking()
            .Where(x => x.TokenHash == hash && x.RevokedAt == null)
            .Select(x => new { x.UserId })
            .FirstOrDefaultAsync(ct);
        if (feed is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Read on every poll and not at mint: switching the installation's feeds off has to stop
        // every address already out there, and it is the only lever an operator has over them.
        if (!(await settings.GetProtectionAsync(ct)).CalendarFeedEnabled)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // A locked account cannot sign in, so its feed does not answer either — the feed is a
        // reading of the account and must stop where the account stops.
        if (!await AccountStanding.IsOpenAsync(db, feed.UserId, clock.GetUtcNow(), ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // The account's grants, assembled now. Nothing was stored with the token, so this is the
        // same context the account's own sign-in would get at this moment, and a grant withdrawn
        // since the last poll is already gone from it.
        var ctx = await AccessContextResolver.ResolveAsync(db, feed.UserId, ct);

        var site = NotificationLinks.SiteUrl(configuration);
        var uidHost = UidHost(site);
        var entries = new List<CalendarFeedEntry>();

        // Each family: the account's visibility first and unconditionally, then the display rule,
        // then the intersection with what the account is on. The order is the point — nothing
        // below the visibility walk can widen it, so a feed can never say a row the account's own
        // calendar page would not draw.
        var onTrips = TripAudience.TripIdsTheAccountIsOn(db, feed.UserId);
        var trips = await db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(x => ShownStates.Contains(x.State))
            .Where(x => onTrips.Contains(x.Id))
            .OrderByDescending(x => x.TripDate).ThenBy(x => x.Id)
            .Take(MaxRows)
            .Select(x => new
            {
                x.Id,
                x.Title,
                Start = x.TripDate,
                End = x.TripDateEnd,
                StartTime = x.EntryTime,
                EndTime = x.ExitTime,
                x.State,
                x.UpdatedAt,
            })
            .ToListAsync(ct);
        entries.AddRange(trips.Select(x => new CalendarFeedEntry(
            $"trip-{x.Id}@{uidHost}",
            x.Title,
            x.Start,
            x.End,
            x.StartTime,
            x.EndTime,
            $"{site}/trip-logs/{x.Id}",
            x.UpdatedAt,
            x.State == ActivityState.Cancelled)));

        var onCamps = ExpeditionAudience.ExpeditionIdsTheAccountIsOn(db, feed.UserId);
        var camps = await db.Expeditions.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Expeditions)
            .Where(x => ShownStates.Contains(x.State))
            .Where(x => onCamps.Contains(x.Id))
            .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id)
            .Take(MaxRows)
            .Select(x => new
            {
                x.Id,
                Title = x.Name,
                Start = x.StartDate,
                End = x.EndDate,
                x.State,
                x.UpdatedAt,
            })
            .ToListAsync(ct);
        entries.AddRange(camps.Select(x => new CalendarFeedEntry(
            $"camp-{x.Id}@{uidHost}",
            x.Title,
            x.Start,
            x.End,
            null,
            null,
            $"{site}/expeditions/{x.Id}",
            x.UpdatedAt,
            x.State == ActivityState.Cancelled)));

        // Occurrences of a repeating event are rows, so each is its own entry and no recurrence
        // rule is written: the feed says exactly the rows the calendar page shows.
        var onEvents = EventAudience.EventIdsTheAccountIsOn(db, feed.UserId);
        var events = await db.Events.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Events)
            .Where(x => ShownStates.Contains(x.State))
            .Where(x => onEvents.Contains(x.Id))
            .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id)
            .Take(MaxRows)
            .Select(x => new
            {
                x.Id,
                x.Title,
                Start = x.StartDate,
                End = x.EndDate,
                x.StartTime,
                x.EndTime,
                x.State,
                x.UpdatedAt,
            })
            .ToListAsync(ct);
        entries.AddRange(events.Select(x => new CalendarFeedEntry(
            $"event-{x.Id}@{uidHost}",
            x.Title,
            x.Start,
            x.End,
            x.StartTime,
            x.EndTime,
            $"{site}/events/{x.Id}",
            x.UpdatedAt,
            x.State == ActivityState.Cancelled)));

        // Merged in memory and bounded once more over the union, newest kept, so three families
        // each under the cap cannot together hand a subscriber three times it.
        var ordered = entries
            .OrderByDescending(e => e.StartDate).ThenBy(e => e.Uid, StringComparer.Ordinal)
            .Take(MaxRows)
            .OrderBy(e => e.StartDate).ThenBy(e => e.Uid, StringComparer.Ordinal)
            .ToList();

        var text = CalendarFeedWriter.Write(about.Value.InstanceName, ordered);
        return TypedResults.Text(text, "text/calendar", Encoding.UTF8);
    }

    /// <summary>
    /// What follows the <c>@</c> in every identifier, so two installations' feeds subscribed to
    /// from one phone never collide on a row: the installation's own host, or its whole address
    /// when that cannot be read as one.
    /// </summary>
    private static string UidHost(string site) =>
        Uri.TryCreate(site, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : "silexgis";
}
