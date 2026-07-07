// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Features.Me;

public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetAsync)
            .WithTags("Me")
            .WithSummary("Profile and global roles of the authenticated caller.");
        return api;
    }

    private static async Task<IResult> GetAsync(HttpContext context, UserManager<SilexGisUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return TypedResults.Ok(new MeDto(user.Id, user.Email!, user.DisplayName, user.Bio, user.Locale, [.. roles]));
    }
}

public sealed record MeDto(Guid Id, string Email, string? DisplayName, string? Bio, string Locale, string[] Roles);
