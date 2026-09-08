// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.Statistics;

/// <summary>
/// What a person, a cave, a club or a camp adds up to across trips — read-only, derived on every
/// request, and counted over the trips the caller may read.
/// </summary>
/// <remarks>
/// The subjects below are read surfaces and nothing about them is askable: a total may be looked
/// at, never filtered or sorted by. That is the same rule the trip filter vocabulary states about
/// the two questions it refuses — which trips visited a cave, and which trips a named person was
/// on — and for the same reason: both are answers about a cave and about a person assembled out of
/// rows the asker may never see, and a figure that moves under a filter gives the answer just as
/// surely as the rows would have.
///
/// <b>That rule is about these subjects and not about the whole path they are served under.</b>
/// The registry-wide statistics answer beneath the same path segment and are deliberately
/// narrowable, by a closed set of four scope fields, over a set already cut to what the caller may
/// read and — wherever the answer touches a place — to what they may place exactly. The difference
/// is what the subject is: a total about one named person or one named cave is an assertion about
/// that person or that cave, and moving it under a filter interrogates them; a distribution over
/// the registry is an assertion about the set, and narrowing the set is what makes it readable at
/// all. Those disclosure controls are stated where that family is defined. Neither contract is
/// visible in the generated schema, so a reader who takes this paragraph for a rule about every
/// route under this path gets the wrong rule for half of them.
///
/// Each subject answers twice: once as figures on a screen, once as a file. Both go through the
/// same permission ladder and the same query, in the shape below — a file is read once and then
/// kept, forwarded and opened long after the rules that produced it changed, so a report built by
/// its own path is how a saved copy comes to state what the screen refuses to.
/// </remarks>
public static class TripStatisticsEndpoints
{
    public static RouteGroupBuilder MapTripStatisticsEndpoints(this RouteGroupBuilder api)
    {
        var stats = api.MapGroup("/stats").WithTags("Statistics");

        stats.MapGet("/cavers/{id:guid}", ForCaverAsync)
            .WithSummary("What one person has done across the trips the caller may read.");
        stats.MapGet("/cavers/{id:guid}/export", ExportCaverAsync)
            .WithSummary("The same figures for one person, as a spreadsheet.");
        stats.MapGet("/caves/{id:guid}", ForCaveAsync)
            .WithSummary("What has been done at one cave across the trips the caller may read.");
        stats.MapGet("/caves/{id:guid}/export", ExportCaveAsync)
            .WithSummary("The same figures for one cave, as a spreadsheet.");
        stats.MapGet("/caving-groups/{id:guid}", ForCavingGroupAsync)
            .WithSummary("What one club has organised across the trips the caller may read.");
        stats.MapGet("/caving-groups/{id:guid}/export", ExportCavingGroupAsync)
            .WithSummary("The same figures for one club, as a spreadsheet.");
        stats.MapGet("/expeditions/{id:guid}", ForExpeditionAsync)
            .WithSummary("What one camp adds up to across the trips in it the caller may read.");
        stats.MapGet("/expeditions/{id:guid}/export", ExportExpeditionAsync)
            .WithSummary("The same figures for one camp, as a spreadsheet.");

        return api;
    }

    private static Task<Results<Ok<TripStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ForCaverAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct) =>
        SeenAsync(StatisticsSubject.Caver, id, db, accessAccessor, protection, ct);

    private static Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportCaverAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct) =>
        SavedAsync(StatisticsSubject.Caver, id, db, accessAccessor, protection, sheets, ct);

    private static Task<Results<Ok<TripStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ForCaveAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct) =>
        SeenAsync(StatisticsSubject.Cave, id, db, accessAccessor, protection, ct);

    private static Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportCaveAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct) =>
        SavedAsync(StatisticsSubject.Cave, id, db, accessAccessor, protection, sheets, ct);

    private static Task<Results<Ok<TripStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ForCavingGroupAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct) =>
        SeenAsync(StatisticsSubject.CavingGroup, id, db, accessAccessor, protection, ct);

    private static Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportCavingGroupAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct) =>
        SavedAsync(StatisticsSubject.CavingGroup, id, db, accessAccessor, protection, sheets, ct);

    private static Task<Results<Ok<TripStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ForExpeditionAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct) =>
        SeenAsync(StatisticsSubject.Expedition, id, db, accessAccessor, protection, ct);

    private static Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportExpeditionAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct) =>
        SavedAsync(StatisticsSubject.Expedition, id, db, accessAccessor, protection, sheets, ct);

    private static async Task<Results<Ok<TripStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SeenAsync(
            StatisticsSubject subject,
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var answer = await AnswerAsync(subject, id, db, accessAccessor, protection, ct);
        return answer.Unauthorized ? TypedResults.Unauthorized()
            : answer.Problem is { } problem ? problem
            : TypedResults.Ok(answer.Totals!);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        SavedAsync(
            StatisticsSubject subject,
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct)
    {
        // The same refusal the screen gets, letter for letter: a file that existed where a page
        // said nothing would be the answer the page declined to give.
        var answer = await AnswerAsync(subject, id, db, accessAccessor, protection, ct);
        if (answer.Unauthorized)
        {
            return TypedResults.Unauthorized();
        }

        if (answer.Problem is { } problem)
        {
            return problem;
        }

        var bytes = sheets.Write(
            TripStatisticsWorkbook.SheetName,
            TripStatisticsWorkbook.Rows(subject, answer.Totals!));

        // Named by what it is about rather than by who or which cave: the name a person or a
        // protected place may be shown under is decided in one place for the whole application,
        // and a download's file name is not somewhere to decide it a second time. The subject's
        // own identifier disambiguates two files saved side by side.
        var fileName =
            $"trip-statistics-{Slug(subject)}-{id.ToString("N")[..8]}-{DateTime.UtcNow:yyyyMMdd}."
            + sheets.Extension;
        return TypedResults.File(bytes, sheets.ContentType, fileName);
    }

    /// <summary>
    /// The permission ladder and the one query, for whichever subject is being asked about. Both
    /// surfaces come through here so neither can acquire a rule the other lacks.
    /// </summary>
    private static async Task<Answer> AnswerAsync(
        StatisticsSubject subject,
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return new Answer(true, null, null);
        }

        switch (subject)
        {
            case StatisticsSubject.Caver:
                if (!Holds(ctx, AccessDomain.Cavers, id))
                {
                    return new Answer(false, ApiProblems.Forbidden("access.forbidden"), null);
                }

                if (!await db.Cavers.AsNoTracking().AnyAsync(c => c.Id == id, ct))
                {
                    return new Answer(false, ApiProblems.NotFound("caver.not_found"), null);
                }

                return new Answer(false, null, await TripStatisticsQuery.ComputeAsync(
                    db,
                    protection,
                    ctx,
                    trip => db.TripLogParticipants.Any(p => p.TripLogId == trip.Id && p.CaverId == id),
                    id,
                    null,
                    ct));

            case StatisticsSubject.Cave:
                var readable = await db.Features.AsNoTracking()
                    .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                    .AnyAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);
                if (!readable)
                {
                    return new Answer(false, ApiProblems.NotFound("feature.not_found"), null);
                }

                // A caller who may read the cave but not know where it is gets the same answer the
                // list of its trips gives them: nothing. The trips carry their own positions, so
                // counting them beside a name places the cave — and a count is the more dangerous
                // half, because it can be asked repeatedly and compared.
                if (await protection.ShouldRedactLinkAsync(ctx, id, ct))
                {
                    return new Answer(false, null, TripStatisticsDto.Empty);
                }

                // The cave itself is handed down as the subject, so its places and its first
                // visits are about this cave. A trip normally names more than one place — the
                // cave it entered, the area it fell in, sometimes a second cave the same day —
                // and counting all of them here would print another cave's figures under this
                // cave's name.
                var naming = TripRoleLinks.TripIdsNaming(db, id);
                return new Answer(false, null, await TripStatisticsQuery.ComputeAsync(
                    db, protection, ctx, trip => naming.Contains(trip.Id), null, id, ct));

            case StatisticsSubject.CavingGroup:
                if (!Holds(ctx, AccessDomain.CavingGroups, id))
                {
                    return new Answer(false, ApiProblems.Forbidden("access.forbidden"), null);
                }

                if (!await db.CavingGroups.AsNoTracking().AnyAsync(g => g.Id == id, ct))
                {
                    return new Answer(false, ApiProblems.NotFound("caving_group.not_found"), null);
                }

                // The club that ran the trip, not the club a trip is shared with: the first says
                // whose activity this was, the second is a visibility setting and counting by it
                // would report a club's totals as whatever happened to be shared with it.
                return new Answer(false, null, await TripStatisticsQuery.ComputeAsync(
                    db, protection, ctx, trip => trip.OrganizingCavingGroupId == id, null, null, ct));

            case StatisticsSubject.Expedition:
                // A camp is governed in its own right, so the question is whether this caller may
                // read this camp rather than whether they hold camps generally — and a camp they
                // may not read is missing rather than refused, exactly as the camp's own page and
                // its trip listing answer. An id that answered differently from one that does not
                // exist would be an id anybody could go looking for.
                var camp = await db.Expeditions.AsNoTracking()
                    .VisibleTo(ctx, AccessDomain.Expeditions)
                    .AnyAsync(x => x.Id == id, ct);
                if (!camp)
                {
                    return new Answer(false, ApiProblems.NotFound("expedition.not_found"), null);
                }

                // The trips gathered into the camp, out of the trips this caller may read — which
                // is why the same camp legitimately shows two people two sets of totals, and why
                // the surfaces showing them carry the sentence saying so.
                //
                // No subject person and no subject cave: a camp is about neither, so its places
                // are every place its trips reached and its first visits are anybody's.
                return new Answer(false, null, await TripStatisticsQuery.ComputeAsync(
                    db,
                    protection,
                    ctx,
                    trip => db.ExpeditionTrips.Any(m => m.ExpeditionId == id && m.TripLogId == trip.Id),
                    null,
                    null,
                    ct));

            default:
                throw new ArgumentOutOfRangeException(nameof(subject));
        }
    }

    /// <summary>
    /// The domain check for the subject being asked about. A statistic is a reading of the subject,
    /// so it is refused wherever reading the subject is; there is deliberately no right of its own,
    /// because the figures are already filtered to what the caller may read and a right over them
    /// would be a right to a number nobody is otherwise entitled to.
    /// </summary>
    private static bool Holds(AccessContext ctx, AccessDomain domain, Guid subjectId) =>
        AccessEvaluator.Decide(ctx, domain, AccessAction.Read, new AccessTargetFacts { ObjectId = subjectId })
            .Allowed;

    private static string Slug(StatisticsSubject subject) => subject switch
    {
        StatisticsSubject.Caver => "caver",
        StatisticsSubject.Cave => "cave",
        StatisticsSubject.CavingGroup => "caving-group",
        StatisticsSubject.Expedition => "expedition",
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    /// <summary>
    /// Either a refusal or the figures — never both, and never neither.
    /// </summary>
    private readonly record struct Answer(
        bool Unauthorized,
        ProblemHttpResult? Problem,
        TripStatisticsDto? Totals);
}
