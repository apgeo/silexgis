// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>One user of the SpeleoLoc application on some phone, as the roster's keeper sees it.</summary>
/// <param name="DeviceUserId">The phone's own identifier for the user.</param>
/// <param name="Username">The phone's user name; null for a user only ever met in an imported archive.</param>
/// <param name="CaverId">The caver the phone user is linked to; null while nobody has said.</param>
/// <param name="CaverName">That caver's name as the caller is shown it anywhere else; null while unlinked.</param>
/// <param name="LinkSource">Who made the link; null while unlinked.</param>
/// <param name="LastSeenAt">When an upload or an import last described this phone user.</param>
public sealed record PhoneUserDto(
    Guid DeviceUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    Guid? CaverId,
    string? CaverName,
    DevicePersonLinkSource? LinkSource,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>Which caver a phone user is, as somebody keeping the roster says it.</summary>
public sealed record PhoneUserLinkRequest(Guid CaverId);

public sealed class PhoneUserLinkRequestValidator : AbstractValidator<PhoneUserLinkRequest>
{
    public PhoneUserLinkRequestValidator() => RuleFor(x => x.CaverId).NotEmpty();
}

/// <summary>
/// The phone users this installation has met and the cavers they are, for whoever keeps the
/// roster: the list, and linking or unlinking one.
/// </summary>
/// <remarks>
/// <para>
/// Listing is Read over the caver domain — the right that lets an account be told who people are
/// at all — and changing a link is Write over it, the roster keeper's right, because a link says
/// who a person is in everything a phone sends from then on.
/// </para>
/// <para>
/// A link made here carries the roster keeper's authority, the highest there is: a phone that goes
/// on sending a different choice does not replace it, and learns of it instead from the directory
/// it reads. These routes work whether or not the installation offers the people directory to
/// phones — the links decide archive imports too, and someone has to be able to correct them.
/// </para>
/// </remarks>
public static class SyncPhoneUserEndpoints
{
    public static void MapSyncPhoneUserEndpoints(this RouteGroupBuilder api)
    {
        var phoneUsers = api.MapGroup("/phone-users").WithTags("Sync");

        phoneUsers.MapGet("/", ListAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("The phone users this installation has met, the unlinked first, with the caver each is.");
        phoneUsers.MapPut("/{deviceUserId:guid}/link", LinkAsync)
            .WithValidation<PhoneUserLinkRequest>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Says which caver a phone user is; a phone's later different choice does not replace it.");
        phoneUsers.MapDelete("/{deviceUserId:guid}/link", UnlinkAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Takes a phone user's link to a caver away, keeping the phone user.");
    }

    private static async Task<Results<Ok<PagedResult<PhoneUserDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CaverDirectory.MayReadPeople(ctx))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.DevicePersons.AsNoTracking()
            .OrderBy(x => x.CaverId != null)
            .ThenByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.DeviceUserId);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var labels = await LabelsAsync(db, user, rows, ct);

        return TypedResults.Ok(new PagedResult<PhoneUserDto>([.. rows.Select(r => ToDto(r, labels))], p, size, total));
    }

    private static async Task<Results<Ok<PhoneUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> LinkAsync(
        Guid deviceUserId,
        PhoneUserLinkRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CaverDirectory.CanKeepRoster(ctx))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var person = await db.DevicePersons.FirstOrDefaultAsync(x => x.DeviceUserId == deviceUserId, ct);
        if (person is null)
        {
            return ApiProblems.NotFound("sync.phone_user_not_found");
        }

        if (!await db.Cavers.AnyAsync(c => c.Id == request.CaverId, ct))
        {
            return ApiProblems.NotFound("sync.caver_not_found");
        }

        // Written whatever source the current link has: the roster keeper's word is the highest,
        // and replacing their own earlier word is the one replacement only they may make.
        person.CaverId = request.CaverId;
        person.LinkSource = DevicePersonLinkSource.Administrator;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDto(person, await LabelsAsync(db, user, [person], ct)));
    }

    private static async Task<Results<Ok<PhoneUserDto>, UnauthorizedHttpResult, ProblemHttpResult>> UnlinkAsync(
        Guid deviceUserId,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CaverDirectory.CanKeepRoster(ctx))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var person = await db.DevicePersons.FirstOrDefaultAsync(x => x.DeviceUserId == deviceUserId, ct);
        if (person is null)
        {
            return ApiProblems.NotFound("sync.phone_user_not_found");
        }

        // The phone user stays: the phone still has them, and the row is what every later upload
        // and import resolves them through. What goes is the statement of who they are.
        person.CaverId = null;
        person.LinkSource = DevicePersonLinkSource.Administrator;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDto(person, new Dictionary<Guid, string>()));
    }

    private static Task<Dictionary<Guid, string>> LabelsAsync(
        SilexGisDbContext db, UserContext user, IReadOnlyCollection<DevicePerson> rows, CancellationToken ct) =>
        CaverDirectory.ResolveLabelsAsync(
            db, user, rows.Where(r => r.CaverId is not null).Select(r => r.CaverId!.Value), ct);

    private static PhoneUserDto ToDto(DevicePerson p, IReadOnlyDictionary<Guid, string> labels) => new(
        p.DeviceUserId,
        p.Username,
        p.FirstName,
        p.LastName,
        p.CaverId,
        p.CaverId is { } caverId ? labels.GetValueOrDefault(caverId) : null,
        p.CaverId is null ? null : p.LinkSource,
        p.FirstSeenAt,
        p.LastSeenAt);
}
