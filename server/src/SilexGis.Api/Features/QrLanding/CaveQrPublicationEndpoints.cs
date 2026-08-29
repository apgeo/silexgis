// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.QrLanding;

/// <summary>
/// Deciding that a cave's printed codes may be resolved by somebody who is not signed in, and
/// taking that decision back.
/// </summary>
/// <remarks>
/// <para>
/// The decision is gated on the right to share the cave, because that is what it is: a caller
/// who may hand this cave to somebody outside the installation may also let a label on its wall
/// be scanned. It is not gated on a right of its own — a cave is a feature and is governed as
/// one, so nothing new needs a place in the access model for this to be decidable by exactly
/// the people who could already decide the neighbouring question.
/// </para>
/// <para>
/// The unit is the cave. Anything inside one inherits the decision through containment, so there
/// is no route here for publishing a single place, and no way for one label to be live under a
/// cave nobody published.
/// </para>
/// </remarks>
public static class CaveQrPublicationEndpoints
{
    /// <summary>
    /// The answer for a cave that is not there and for a cave the caller may not read — the same
    /// name the cave routes give the same fact, because it is the same fact.
    /// </summary>
    private const string CaveNotFoundCode = "cave.not_found";

    public static RouteGroupBuilder MapCaveQrPublicationEndpoints(this RouteGroupBuilder api)
    {
        var publication = api.MapGroup("/caves/{id:guid}/qr-publication").WithTags("QrLanding");

        publication.MapGet("/", GetAsync)
            .WithSummary("Whether this cave's codes resolve for a visitor who is not signed in (Share permission).");
        publication.MapPost("/", PublishAsync)
            .WithSummary("Lets this cave's codes resolve for visitors who are not signed in (Share permission).");
        publication.MapDelete("/", RevokeAsync)
            .WithSummary("Stops this cave's codes resolving for visitors who are not signed in (Share permission).");

        return api;
    }

    private static async Task<Results<Ok<CaveQrPublicationDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (cave, problem) = await ResolveCaveAsync(id, ctx, db, access, ct);
        if (problem is not null)
        {
            return problem;
        }

        return TypedResults.Ok(ToDto(await LatestAsync(db.CaveQrPublications.AsNoTracking(), cave!.Id, ct)));
    }

    private static async Task<Results<Ok<CaveQrPublicationDto>, UnauthorizedHttpResult, ProblemHttpResult>> PublishAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (cave, problem) = await ResolveCaveAsync(id, ctx, db, access, ct);
        if (problem is not null)
        {
            return problem;
        }

        var live = await LiveAsync(db.CaveQrPublications, cave!.Id, ct);
        if (live is null)
        {
            var fresh = new CaveQrPublication { FeatureId = cave.Id, PublishedBy = ctx.UserId };
            db.CaveQrPublications.Add(fresh);
            try
            {
                await db.SaveChangesAsync(ct);
                live = fresh;
            }
            catch (DbUpdateException)
            {
                // Two people deciding at the same moment. Only one row can be live and the
                // database is what settles which, so the one that lost reports the decision
                // that stands rather than an error about a race neither of them could see.
                // Anything else that could fail this write has no standing decision to find
                // and is rethrown.
                db.Entry(fresh).State = EntityState.Detached;
                live = await LiveAsync(db.CaveQrPublications.AsNoTracking(), cave.Id, ct);
                if (live is null)
                {
                    throw;
                }
            }
        }

        // Publishing a cave that is already published is the state the caller asked for, so the
        // standing decision is answered rather than replaced. Writing a second row would rewrite
        // both who decided and when, over a button somebody pressed twice.
        return TypedResults.Ok(ToDto(live));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RevokeAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (cave, problem) = await ResolveCaveAsync(id, ctx, db, access, ct);
        if (problem is not null)
        {
            return problem;
        }

        var live = await LiveAsync(db.CaveQrPublications, cave!.Id, ct);
        if (live is not null)
        {
            // Stamped here rather than by the timestamp interceptor, which owns only the created
            // and updated columns. A second withdrawal finds no live row and changes nothing, so
            // the moment the codes stopped resolving keeps standing however many times it is
            // asked for — and so does withdrawing a cave nobody ever published, which is already
            // the state being asked for and is not an error.
            live.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The cave this route is about, or the answer to give instead of it. A cave the caller may
    /// not read answers exactly as a cave that is not there: whether this installation holds a
    /// particular cave is not something an unauthorised caller learns by asking about its codes.
    /// A feature that is not a cave is not there either — publication is per cave, and a place
    /// inside one is reached through the cave that contains it.
    /// </summary>
    private static async Task<(Feature? Cave, ProblemHttpResult? Problem)> ResolveCaveAsync(
        Guid id,
        AccessContext ctx,
        SilexGisDbContext db,
        IAccessService access,
        CancellationToken ct)
    {
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);
        if (cave is null)
        {
            return (null, ApiProblems.NotFound(CaveNotFoundCode));
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Share, cave, ct)).Allowed)
        {
            // Readable but not shareable is a refusal the caller can do something about;
            // unreadable is not disclosed at all.
            return (null, (await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(CaveNotFoundCode));
        }

        return (cave, null);
    }

    /// <summary>The decision that stands, if one does.</summary>
    private static Task<CaveQrPublication?> LiveAsync(
        IQueryable<CaveQrPublication> publications, Guid caveId, CancellationToken ct) =>
        publications.FirstOrDefaultAsync(p => p.FeatureId == caveId && p.RevokedAt == null, ct);

    /// <summary>
    /// The most recent decision, live or withdrawn. Ordered by the identifier as well as the
    /// timestamp so two decisions inside one clock tick still have an order — the identifiers are
    /// time-ordered, so this agrees with the timestamps rather than competing with them.
    /// </summary>
    private static Task<CaveQrPublication?> LatestAsync(
        IQueryable<CaveQrPublication> publications, Guid caveId, CancellationToken ct) =>
        publications
            .Where(p => p.FeatureId == caveId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

    private static CaveQrPublicationDto ToDto(CaveQrPublication? publication) =>
        publication is null
            ? new CaveQrPublicationDto(false, null, null, null, null)
            : new CaveQrPublicationDto(
                publication.RevokedAt is null,
                publication.Id,
                publication.PublishedBy,
                publication.CreatedAt,
                publication.RevokedAt);
}
