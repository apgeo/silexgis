// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// The caller's own addresses.
/// </summary>
/// <remarks>
/// <para>
/// Ownership is the whole authorization rule, so there is no permission service and no
/// visibility filter here: an address is not a content object — it has no owner/team/visibility
/// trio and no ACL. Who else may see it is governed by the address visibility setting on the
/// profile, applied wherever profiles are emitted.
/// </para>
/// <para>
/// Someone else's address id answers 404, never 403: a distinguishable refusal would confirm
/// the id exists.
/// </para>
/// </remarks>
public static class MeAddressEndpoints
{
    /// <summary>A person has a few addresses; the cap stops the list becoming free storage.</summary>
    private const int MaxAddresses = 10;

    public static RouteGroupBuilder MapMeAddressEndpoints(this RouteGroupBuilder api)
    {
        var addresses = api.MapGroup("/me/addresses").WithTags("Me");

        addresses.MapPost("/", CreateAsync)
            .WithValidation<UserAddressWriteRequest>()
            .WithSummary("Adds an address to the caller's profile.");
        addresses.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<UserAddressWriteRequest>()
            .WithSummary("Replaces one of the caller's addresses.");
        addresses.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Removes one of the caller's addresses.");

        return api;
    }

    private static async Task<Results<Created<UserAddressDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        UserAddressWriteRequest request,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var count = await db.UserAddresses.CountAsync(a => a.UserId == user.UserId, ct);
        if (count >= MaxAddresses)
        {
            return ApiProblems.BadRequest("me.address_limit", $"At most {MaxAddresses} addresses.");
        }

        var address = new UserAddress
        {
            UserId = user.UserId,
            Label = request.Label.Trim(),
            Country = Trimmed(request.Country),
            City = Trimmed(request.City),
            AddressText = Trimmed(request.AddressText),
            Geom = request.Geom?.ToPoint(),
            SortOrder = request.SortOrder,
        };
        db.UserAddresses.Add(address);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/me/addresses/{address.Id}", MeMapping.ToDto(address));
    }

    private static async Task<Results<Ok<UserAddressDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UserAddressWriteRequest request,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var address = await db.UserAddresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.UserId, ct);
        if (address is null)
        {
            return ApiProblems.NotFound("me.address_not_found");
        }

        address.Label = request.Label.Trim();
        address.Country = Trimmed(request.Country);
        address.City = Trimmed(request.City);
        address.AddressText = Trimmed(request.AddressText);
        address.Geom = request.Geom?.ToPoint();
        address.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(MeMapping.ToDto(address));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var address = await db.UserAddresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.UserId, ct);
        if (address is null)
        {
            return ApiProblems.NotFound("me.address_not_found");
        }

        db.UserAddresses.Remove(address);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
