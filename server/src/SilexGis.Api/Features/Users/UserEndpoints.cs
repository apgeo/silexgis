// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Users;

public sealed record UserSummaryDto(Guid Id, string? DisplayName, string? Email);

/// <summary>
/// Minimal user directory for pickers (ACL grants, team members, participants).
/// Installations are single-community, so authenticated users may search the
/// directory; full user administration is a separate admin surface.
/// </summary>
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/users/search", SearchAsync)
            .WithTags("Users")
            .WithSummary("Searches users by name/email for pickers (authenticated).");
        return api;
    }

    private static async Task<Results<Ok<List<UserSummaryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> SearchAsync(
        string q,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return ApiProblems.BadRequest("user_search.query_too_short", "Provide at least 2 characters.");
        }

        var pattern = $"%{q.Trim()}%";
        var users = await db.Users.AsNoTracking()
            .Where(u => (u.DisplayName != null && EF.Functions.ILike(EF.Functions.Unaccent(u.DisplayName), EF.Functions.Unaccent(pattern)))
                || (u.Email != null && EF.Functions.ILike(u.Email, pattern)))
            .OrderBy(u => u.DisplayName ?? u.Email)
            .Take(20)
            .Select(u => new UserSummaryDto(u.Id, u.DisplayName ?? u.UserName, u.Email))
            .ToListAsync(ct);

        return TypedResults.Ok(users);
    }
}
