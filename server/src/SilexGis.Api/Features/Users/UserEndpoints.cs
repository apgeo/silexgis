// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Users;

/// <summary>
/// A user as a picker shows them. <paramref name="Label"/> is always safe to display;
/// <paramref name="Email"/> is present only when the caller may see it.
/// </summary>
public sealed record UserSummaryDto(Guid Id, string Label, string? Email);

/// <summary>
/// One member of the installation, reduced to what the caller may see. Every optional field is
/// absent rather than masked when the subject has not shared it — and the subject's choices are
/// never included, since the list of fields someone hides is itself information about them.
/// </summary>
public sealed record MemberDto(
    Guid Id,
    string Label,
    string? DisplayName,
    string? AvatarUrl,
    string? AvatarPreset,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    string? CavingClub,
    string? Bio,
    IReadOnlyList<MemberAddressDto> Addresses);

public sealed record MemberAddressDto(
    Guid Id,
    string Label,
    string? Country,
    string? City,
    string? AddressText,
    double? Longitude,
    double? Latitude);

/// <summary>
/// The user directory: a picker search for granting access and naming participants, and a member
/// list honouring each person's per-field visibility choices.
/// </summary>
/// <remarks>
/// <para>
/// Lives at /members rather than /users because /users is reserved for administrative user
/// management, which is a different audience and a different rule.
/// </para>
/// <para>
/// Users are not content objects — there is no ACL over a person — so nothing here composes the
/// content visibility filter. The profile visibility settings are the whole gate.
/// </para>
/// </remarks>
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/users/search", SearchAsync)
            .WithTags("Users")
            .WithSummary("Searches users by name (and by address, where the caller may see it).");

        api.MapGet("/members", ListMembersAsync)
            .WithTags("Members")
            .WithSummary("Directory of members, each reduced to what the caller may see.");
        api.MapGet("/members/{id:guid}", GetMemberAsync)
            .WithTags("Members")
            .WithSummary("One member, reduced to what the caller may see.");

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

        // Two branches, because matching on an address the caller may not see would let them
        // confirm any address has an account here by probing patterns — a working oracle even
        // though the address is never rendered. The name branch is unrestricted: a display name
        // is shown everywhere already.
        var byName = db.Users.AsNoTracking()
            .Where(u => u.DisplayName != null
                && EF.Functions.ILike(EF.Functions.Unaccent(u.DisplayName), EF.Functions.Unaccent(pattern)));

        var byEmail = db.Users.AsNoTracking()
            .WhereFieldVisibleTo(ProfileField.Email, user, db.CavingGroupMembers.AsNoTracking())
            .Where(u => u.Email != null && EF.Functions.ILike(u.Email, pattern));

        var matches = await byName.Union(byEmail)
            // Never ordered by a field the caller may not see: the order alone would disclose
            // where a hidden address falls alphabetically.
            .OrderBy(u => u.DisplayName == null)
            .ThenBy(u => u.DisplayName)
            .ThenBy(u => u.Id)
            .Take(20)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var profiles = await ProfileDirectory.ResolveAsync(db, user, matches, ct);
        return TypedResults.Ok(matches
            .Where(profiles.ContainsKey)
            .Select(id => profiles[id])
            .Select(p => new UserSummaryDto(p.Id, p.Label, p.Email))
            .ToList());
    }

    private static async Task<Results<Ok<PagedResult<MemberDto>>, UnauthorizedHttpResult>> ListMembersAsync(
        string? search,
        int? page,
        int? pageSize,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Only the always-visible display name is searchable here. Offering a filter over a
        // governed field would rebuild the oracle the search endpoint above just closed.
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => u.DisplayName != null
                && EF.Functions.ILike(EF.Functions.Unaccent(u.DisplayName), EF.Functions.Unaccent(pattern)));
        }

        var (normalizedPage, normalizedPageSize) = Paging.Normalize(page, pageSize);
        var ordered = query
            .OrderBy(u => u.DisplayName == null)
            .ThenBy(u => u.DisplayName)
            .ThenBy(u => u.Id);

        var total = await ordered.CountAsync(ct);
        var ids = await ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var profiles = await ProfileDirectory.ResolveAsync(db, user, ids, ct);
        var items = ids.Where(profiles.ContainsKey).Select(id => ToDto(profiles[id], tokens)).ToList();

        return TypedResults.Ok(new PagedResult<MemberDto>(items, normalizedPage, normalizedPageSize, total));
    }

    private static async Task<Results<Ok<MemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetMemberAsync(
        Guid id,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var profile = await ProfileDirectory.ResolveOneAsync(db, user, id, ct);
        return profile is null
            ? ApiProblems.NotFound("member.not_found")
            : TypedResults.Ok(ToDto(profile, tokens));
    }

    private static MemberDto ToDto(PublicProfile profile, IFileAccessTokenService tokens) => new(
        profile.Id,
        profile.Label,
        profile.DisplayName,
        // Avatars carry no visibility setting: attribution rows and member lists must always have
        // something to show. Delivery still needs a short-lived token, so this is a URL the caller
        // can render rather than a bare file id.
        profile.AvatarFileId is { } fileId
            ? $"/api/v1/files/{fileId}/thumbnail?size=480&token={Uri.EscapeDataString(tokens.CreateToken(fileId))}"
            : null,
        profile.AvatarPreset,
        profile.FirstName,
        profile.LastName,
        profile.Email,
        profile.PhoneNumber,
        profile.CavingClub,
        profile.Bio,
        [.. profile.Addresses.Select(a => new MemberAddressDto(
            a.Id, a.Label, a.Country, a.City, a.AddressText, a.Longitude, a.Latitude))]);
}
