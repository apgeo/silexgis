// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// The flag on a trip made automatically from a phone recording that a person has to look at.
/// </summary>
/// <remarks>
/// Only clearing is offered. Fixing what the flag names — choosing a station for a scan, mapping
/// a phone user — is done with the tools that already exist for it, and the flag is how somebody
/// says they have.
/// </remarks>
public static class TripAttentionEndpoints
{
    /// <summary>The flag changed between the caller reading it and clearing it.</summary>
    public const string ChangedCode = "trip_attention.changed";

    public static RouteGroupBuilder MapTripAttentionEndpoints(this RouteGroupBuilder api)
    {
        var attention = api.MapGroup("/trip-logs/{id:guid}/attention").WithTags("TripLogs");

        attention.MapPost("/clear", ClearAsync)
            .WithName("clearTripAttention")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary(
                "Says that a trip made from a phone recording no longer needs looking at (Write "
                + "permission on the trip). The reasons stay on the record; clearing an already "
                + "cleared flag changes nothing. A trip with no flag answers trip_attention.not_found; a flag "
                + "raised again while it was being cleared answers trip_attention.changed.");

        return api;
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> ClearAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        // A trip the caller may not read is not here; one they may read but not change is theirs
        // to look at, not to sign off.
        if (!(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        var attention = await db.TripAttentions.FirstOrDefaultAsync(a => a.TripLogId == id, ct);
        if (attention is null)
        {
            return ApiProblems.NotFound("trip_attention.not_found");
        }

        // The first clearance is the record of who signed off and when; a second is not a new one.
        if (attention.ClearedAt is null)
        {
            attention.ClearedAt = clock.GetUtcNow();
            attention.ClearedByUserId = ctx.UserId;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // An upload raised the flag again between the reading and the clearance, so what is
                // being signed off is no longer what the caller saw.
                return ApiProblems.Conflict(
                    ChangedCode,
                    "The flag gained a reason while it was being cleared. Look at it again before clearing it.");
            }
        }

        return TypedResults.NoContent();
    }
}
