// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Which trips to read, for the read of several trips' replays at once.
/// </summary>
/// <remarks>
/// The ids arrive as text and are judged by the validator: bound straight to identifiers, one
/// that is not an identifier would be refused by the framework before any rule of this route ran,
/// in a shape of its own.
/// </remarks>
/// <param name="TripLogIds">
/// The trips, one <c>tripLogIds</c> member of the query string each. An id given twice is read once.
/// </param>
public sealed record TrackedTripReplayQuery(
    [property: FromQuery(Name = "tripLogIds")] string[]? TripLogIds);

public sealed class TrackedTripReplayQueryValidator : AbstractValidator<TrackedTripReplayQuery>
{
    /// <summary>
    /// The most trips one read answers. Each trip costs what its two own reads cost, so the bound
    /// is what keeps one request from being asked to fold an installation's whole history.
    /// </summary>
    public const int MaxTrips = 50;

    public TrackedTripReplayQueryValidator()
    {
        RuleFor(x => x.TripLogIds)
            .Must(ids => ids is { Length: > 0 })
            .OverridePropertyName("tripLogIds")
            .WithMessage("tripLogIds names at least one trip.");

        RuleFor(x => x.TripLogIds)
            .Must(ids => ids!.Length <= MaxTrips)
            .When(x => x.TripLogIds is { Length: > 0 })
            .OverridePropertyName("tripLogIds")
            .WithMessage($"tripLogIds names at most {MaxTrips} trips.");

        RuleFor(x => x.TripLogIds)
            .Must(ids => ids!.All(id => Guid.TryParse(id, out _)))
            .When(x => x.TripLogIds is { Length: > 0 })
            .OverridePropertyName("tripLogIds")
            .WithMessage("Every member of tripLogIds is a trip's id.");
    }
}

/// <summary>Somebody on a trip's roster, under the name this caller is shown them by.</summary>
/// <param name="Name">
/// The label every signed-in surface shows this person under; empty when there is none to show.
/// </param>
public sealed record TrackedTripPersonDto(Guid CaverId, string Name);

/// <summary>
/// One trip as a replay of several trips needs it: what it is called, who is on it, its tracking
/// state and its log.
/// </summary>
/// <param name="Participants">The trip's roster, each person once whatever their jobs on it.</param>
/// <param name="Tracking">
/// Exactly what the trip's own state read answers this caller, positions withheld as it withholds
/// them.
/// </param>
/// <param name="Events">
/// The trip's reports, newest first, each exactly as the trip's own log read answers it to this
/// caller. A report taken off the log is not among them.
/// </param>
/// <param name="EventsComplete">
/// False when the log is longer than one answer carries, and <paramref name="Events"/> is then
/// only its newest part. A replay is drawn from the whole log or not at all: a reader must treat
/// such a trip as one whose log could not be read.
/// </param>
public sealed record TrackedTripReplayDto(
    Guid TripLogId,
    string Title,
    IReadOnlyList<TrackedTripPersonDto> Participants,
    TrackingStateDto Tracking,
    IReadOnlyList<TrackingEventDto> Events,
    bool EventsComplete);

/// <summary>
/// Several trips' tracking read in one request, for a surface that replays them together on one
/// survey model.
/// </summary>
/// <remarks>
/// <para>
/// A replay of several trips needs each trip's title, roster, tracking state and whole log. Asked
/// trip by trip that is three reads per trip, the log in pages, from a browser that allows a
/// handful of requests at a time. This answers the same things in one.
/// </para>
/// <para>
/// <b>It decides nothing about what a caller may be told.</b> Each trip is passed through the
/// guard its own reads apply, and its state and its log are produced by the routines those reads
/// run — called, not restated — so a position withheld from a caller on a trip's own page is
/// withheld here, a report taken off a log is absent here, and a deleted trip is as absent here as
/// anywhere. A trip the caller may not read is left out of the answer without a word: an entry
/// saying "refused" would tell them the trip exists.
/// </para>
/// <para>
/// The survey model in the address is gated as the list of its tracked trips gates it. It does
/// not narrow the trips: which trips belong on the model is that list's answer, and a trip named
/// here that has nothing to do with the model is answered as its own reads would answer it, with
/// nothing more.
/// </para>
/// </remarks>
public static class SurveyModelTrackedTripReplayEndpoints
{
    /// <summary>
    /// The longest log one answer carries, in reports. A trip's log runs to tens of reports and a
    /// long expedition day to hundreds; this is far past anything a party can radio out, and a log
    /// past it is flagged incomplete rather than quietly cut.
    /// </summary>
    internal const int MaxEventsPerTrip = 10_000;

    public static RouteGroupBuilder MapSurveyModelTrackedTripReplayEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/survey-models/{surveyModelId:guid}/tracked-trips/replay", ReadAsync)
            .WithValidation<TrackedTripReplayQuery>()
            .WithTags("TripTracking")
            .WithSummary("Up to fifty trips' titles, rosters, tracking states and whole logs in one answer, in the order asked — each exactly as the trip's own tracking reads answer the caller; a trip the caller may not read is left out.");
        return api;
    }

    private static async Task<Results<Ok<List<TrackedTripReplayDto>>, ProblemHttpResult>> ReadAsync(
        Guid surveyModelId, [AsParameters] TrackedTripReplayQuery query, SilexGisDbContext db,
        IAccessService access, FeatureProtection protection, IAccessContextAccessor accessAccessor,
        IOptions<TripTrackingOptions> options, TimeProvider clock, IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null) return ApiProblems.NotFound("survey_model.not_found");
        if (await TripTrackingEndpoints.UsableModelAsync(db, access, protection, ctx, surveyModelId, ct) is null)
        {
            return ApiProblems.NotFound("survey_model.not_found");
        }

        // The validator has already refused an empty list, a list too long and anything that is
        // not an id; what is left parses. In the order asked, each trip once.
        var asked = (query.TripLogIds ?? []).Select(id => Guid.Parse(id)).Distinct().ToList();

        // The guard of the per-trip reads, trip by trip: missing, deleted and unreadable all come
        // back null there, and all three are simply not in the answer here.
        var readable = new List<(Guid Id, string Title)>();
        foreach (var tripLogId in asked)
        {
            if (await TripTrackingEndpoints.ReadableTripAsync(db, access, ctx, tripLogId, ct) is { } trip)
            {
                readable.Add((trip.Id, trip.Title));
            }
        }
        if (readable.Count == 0) return TypedResults.Ok(new List<TrackedTripReplayDto>());

        // The rosters of all of them in one read, and their names by the one rule that decides
        // what a person is shown under — never their address, and an account's own label over the
        // roster's spelling, as on the trip's own page.
        var readableIds = readable.Select(trip => trip.Id).ToList();
        var rosterRows = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where readableIds.Contains(participant.TripLogId)
            select new { participant.TripLogId, participant.CaverId })
            .Distinct()
            .ToListAsync(ct);
        var labels = await CaverDirectory.ResolveLabelsAsync(
            db, await userAccessor.GetAsync(ct), rosterRows.Select(row => row.CaverId), ct);
        var rosters = rosterRows.ToLookup(row => row.TripLogId, row => row.CaverId);

        var dtos = new List<TrackedTripReplayDto>();
        foreach (var (tripLogId, title) in readable)
        {
            var tracking = await TripTrackingEndpoints.StateShownToAsync(
                db, access, protection, ctx, tripLogId, options, clock, userAccessor, ct);

            // The rows and the order of the trip's own log read, by its routines, one row past the
            // bound so that a log longer than the bound is known to be longer.
            var rows = await TripTrackingEndpoints.NewestFirst(TripTrackingEndpoints.LogOf(db, tripLogId))
                .Take(MaxEventsPerTrip + 1)
                .ToListAsync(ct);
            var complete = rows.Count <= MaxEventsPerTrip;
            if (!complete) rows.RemoveAt(rows.Count - 1);
            var events = await TripTrackingEndpoints.ShownToAsync(db, access, protection, ctx, tripLogId, rows, ct);

            dtos.Add(new TrackedTripReplayDto(
                tripLogId,
                title,
                [.. rosters[tripLogId]
                    .Select(caverId => new TrackedTripPersonDto(
                        caverId, labels.GetValueOrDefault(caverId) ?? string.Empty))
                    .OrderBy(person => person.Name, StringComparer.Ordinal)
                    .ThenBy(person => person.CaverId)],
                tracking,
                events,
                complete));
        }
        return TypedResults.Ok(dtos);
    }
}
