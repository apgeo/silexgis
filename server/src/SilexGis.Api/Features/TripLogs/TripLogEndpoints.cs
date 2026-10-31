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
    // A lifecycle state a listing was asked to narrow by that a trip does not have. Its own code
    // rather than the transition refusal's: nothing is being moved, a word was simply not
    // recognised, and a client that cannot tell those apart cannot say anything useful about
    // either. One spelling for both listings, so the two cannot drift.
    private const string StateInvalidCode = TripListQuery.StateInvalidCode;

    // A dimension a listing cannot be sliced by, or a pair of levels that says nothing. One code
    // for both: what a client does about either is the same — put the grouping panel back to
    // something it can ask for — and a second code would only be a second thing to handle.
    private const string GroupInvalidCode = "trip_log.group_invalid";

    public static RouteGroupBuilder MapTripLogEndpoints(this RouteGroupBuilder api)
    {
        var trips = api.MapGroup("/trip-logs").WithTags("TripLogs");

        trips.MapGet("/", ListAsync)
            .WithSummary(
                "Paged trip logs, visibility-filtered. Narrowable by an overlapping date window, "
                + "by a cave, a camp, and by comma-separated lists of area, person on the roster, "
                + "trip type, lifecycle state and audience, plus whether something went wrong; "
                + "orderable by date, title, creation or last change with a leading minus for "
                + "descending. Values inside one list are alternatives and the narrowings are "
                + "combined, so an empty list means no opinion rather than nothing. An area "
                + "reaches everything the containment hierarchy puts inside it. Naming a cave, "
                + "area or person this caller may not read answers with an empty page rather than "
                + "a refusal.");
        trips.MapGet("/facets", FacetsAsync)
            .WithSummary(
                "How many trips each filter option would leave, counted over the same "
                + "visibility-filtered query the page is taken from, with the facet's own choices "
                + "left out so each option answers \u201cand this one too\u201d. Two callers get "
                + "different numbers for the same option and both are right \u2014 these are "
                + "counts of what that caller may read.");
        trips.MapGet("/export", ExportAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "The narrowed listing as a spreadsheet \u2014 every trip the filter leaves and "
                + "not only the page being looked at, visibility-filtered exactly as the page is. "
                + "Besides the list's own narrowings it takes a `filter` (a filter document as a link "
                + "token) or a `savedFilterId` about the trip world, applied together with them; one "
                + "that cannot be read is refused, never answered as everything. "
                + "Bounded, and the file says so in its own first lines when the bound was "
                + "reached.");
        trips.MapGet("/grouping", GroupingAsync)
            .WithSummary(
                "The same narrowed listing broken into slices, one or two levels deep, each "
                + "slice carrying its trip count, the days it spans, the purposes it was for and "
                + "who was on it. Grouped by year, type, state, visibility, incident, area or "
                + "person. A trip counts into every area and every person it holds, so those "
                + "slice counts add up to more than the trips \u2014 the answer says so rather "
                + "than leaving a reader to notice.");
        trips.MapGet("/stats", StatsAsync)
            .WithSummary(
                "What the same narrowed listing adds up to: trips per year, what they were for, "
                + "where they went, who was on them, and how many distinct areas had been reached "
                + "by the end of each year. Counted over the trips this caller may read, so the "
                + "totals are the reader\u2019s and not the archive\u2019s. A trip counts into "
                + "every area and every person it holds, so those breakdowns add up to more than "
                + "the trips \u2014 the answer says so rather than leaving a reader to notice.");
        trips.MapGet("/mine", MineAsync)
            .WithSummary(
                "The trips the calling account is on \u2014 named on the roster or asked about it "
                + "\u2014 soonest first, from today unless a window says otherwise, and narrowable "
                + "by lifecycle state. Whose trips these are is worked out from the caller and "
                + "cannot be asked for: there is no parameter naming a person, because one would "
                + "answer where a named person has been out of trips the asker may not read.");
        trips.MapGet("/deleted", TripLogDeletionEndpoints.ListAsync)
            .WithSummary(
                "The deleted trips this caller may put back — the ones they could read and "
                + "could delete — most recently deleted first, each with when it went and "
                + "when it will be removed for good. A trip past that moment is not listed.");
        trips.MapGet("/config", TripLogDeletionEndpoints.ConfigAsync)
            .WithSummary(
                "What this installation does with a deleted trip: how many days it stays "
                + "restorable, or nothing when deleted trips are kept until somebody says "
                + "otherwise. Asked before a delete so the confirmation can say what will happen.");
        trips.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single trip log with caves and participants.");
        trips.MapPost("/", CreateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Creates a trip log written up after the event (Create permission on trip logs); "
                + "the caller becomes owner, and an audience the request does not name is private.");
        trips.MapPost("/plans", CreatePlanAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Creates a trip that has not happened yet (Create permission on trip logs). The "
                + "same trip in every respect but one: an audience the request does not name is "
                + "the author's caving group rather than private, because a proposal only its "
                + "author can read is a proposal to nobody. The state it starts in is the same.");
        trips.MapGet("/plan-default", PlanDefaultAsync)
            .WithSummary(
                "The audience a trip being planned would get for this caller if they name none, "
                + "answered before the trip exists so a form can say who will see it.");
        trips.MapPut("/{id:guid}", UpdateAsync).WithValidation<TripLogWriteRequest>()
            .WithSummary(
                "Full update (Write permission). The whole roster is replaced, in every role, so a "
                + "person left out of both lists is taken off the trip; the caves are replaced only "
                + "when a list is supplied, and left as they are when the field is omitted.");
        trips.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary(
                "Deletes a trip log (Delete permission). The trip leaves every listing, map, "
                + "calendar and published address at once and answers as not found; nothing on "
                + "it is removed, so it can be put back until the installation's restore window "
                + "runs out.");
        trips.MapPost("/{id:guid}/restore", TripLogDeletionEndpoints.RestoreAsync)
            .WithSummary(
                "Puts a deleted trip back with everything it had (the right to read it and to "
                + "delete it), and answers the trip with its version. Refused once the restore "
                + "window has passed. Nobody is notified.");
        trips.MapPost("/{id:guid}/state", TransitionAsync)
            .WithValidation<TripLogTransitionRequest>()
            .WithSummary(
                "Moves a trip log to another lifecycle state (Write permission). One endpoint "
                + "rather than a verb per state: the moves a trip may make are a table, and a "
                + "verb per move can only ever offer the handful somebody thought to name.");
        trips.MapPost("/{id:guid}/callout", ArrangeCalloutAsync)
            .WithValidation<TripCalloutRequest>()
            .WithSummary(
                "Arranges, changes or calls off the check that notices if the party does not come "
                + "back (Write permission). Its own door rather than two fields on the trip, so "
                + "that saving the trip from a surface which never drew them cannot quietly leave "
                + "a party unwatched.");
        trips.MapPost("/{id:guid}/callout/stand-down", StandDownCalloutAsync)
            .WithSummary(
                "Says the party is out, which stops the overdue check. Open to anyone the trip "
                + "names or has asked, and deliberately not to whoever may edit the trip: it is a "
                + "statement about where people are, not a change to the record of the trip. It "
                + "stands the check down rather than erasing it, so what was arranged stays "
                + "readable afterwards.");
        trips.MapGet("/{id:guid}/report", TripReportEndpoints.DownloadAsync)
            .WithSummary(
                "The trip written up as a document, built from this caller's own reading of the "
                + "trip — the same one the page shows. 'format=pdf' asks for it as a PDF, which "
                + "an installation without the document converter refuses.");
        TripReportEndpoints.MapDownloadWithMap(trips);
        trips.MapPost("/{id:guid}/report", TripReportEndpoints.KeepAsync)
            .WithSummary(
                "Writes the trip up and files the document against the trip, replacing any report "
                + "kept there before (Write permission).");

        return api;
    }
}
