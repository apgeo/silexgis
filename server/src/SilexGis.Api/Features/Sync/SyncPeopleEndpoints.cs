// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>One person a phone may attribute its users to.</summary>
/// <param name="Id">The caver's identifier — what an upload's <c>caverId</c> names.</param>
/// <param name="FullName">
/// The name the caller is shown for this person anywhere else: an account holder's own label wins
/// over the roster's name, as it does on every list of people.
/// </param>
/// <param name="HasAccount">Whether the person signs in here.</param>
/// <param name="IsMe">Whether the person is the caller.</param>
public sealed record SyncPersonDto(Guid Id, string FullName, string? ShortName, bool HasAccount, bool IsMe);

/// <summary>Which caver one phone user is, as it stands on the server.</summary>
/// <param name="CaverId">Null while nobody has said who the phone user is.</param>
public sealed record SyncPersonLinkDto(Guid DeviceUserId, Guid? CaverId);

/// <summary>The people directory and the links the caller's phones have sent.</summary>
/// <param name="People">Names only — never an e-mail address, a telephone number or a roster note.</param>
/// <param name="Links">
/// The current link of every phone user this account has sent, whoever made it — so a phone learns
/// of a link made in the browser and stops offering the choice it had.
/// </param>
public sealed record SyncPeopleDto(IReadOnlyList<SyncPersonDto> People, IReadOnlyList<SyncPersonLinkDto> Links);

/// <summary>
/// Who a phone may say its users are: the directory it offers when a phone user is matched to a
/// caver, and the links that stand.
/// </summary>
/// <remarks>
/// The list is names and nothing else, behind the right that lets an account be told who people
/// are at all. It answers that the part is switched off while the installation has it off, rather
/// than an empty list a phone would read as "nobody".
/// </remarks>
public static class SyncPeopleEndpoints
{
    public static void MapSyncPeopleEndpoints(this RouteGroupBuilder sync)
    {
        sync.MapGet("/people", ListAsync)
            .WithName("syncPeople")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The people a phone may attribute its users to, and the links its users have.");
    }

    private static async Task<Results<Ok<SyncPeopleDto>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAppSettingsService settings,
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

        var sync = await settings.GetSyncAsync(ct);
        if (!sync.PeopleDirectoryEnabled)
        {
            return ApiProblems.Conflict(
                "sync.feature_disabled", "This installation does not offer the people directory.");
        }

        if (!CaverDirectory.MayReadPeople(ctx))
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var cavers = await SyncPeopleDirectory.Listed(db, ctx, sync.PeopleDirectoryScope)
            .OrderBy(c => c.FullName)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.FullName, c.ShortName, c.UserId })
            .ToListAsync(ct);
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, cavers.Select(c => c.Id), ct);

        var people = cavers
            .Select(c => new SyncPersonDto(
                c.Id,
                labels.GetValueOrDefault(c.Id) ?? c.FullName,
                c.ShortName,
                HasAccount: c.UserId is not null,
                IsMe: c.UserId == ctx.UserId))
            .ToList();

        var links = await SyncPeopleDirectory.SentBy(db, ctx.UserId)
            .OrderBy(p => p.DeviceUserId)
            .Select(p => new SyncPersonLinkDto(p.DeviceUserId, p.CaverId))
            .ToListAsync(ct);

        return TypedResults.Ok(new SyncPeopleDto(people, links));
    }
}

/// <summary>
/// Who the people directory lists for a caller, stated once: the directory's answer and the
/// check an upload's link has to pass are the same question, so a phone can never link a user to
/// somebody it could not have been shown.
/// </summary>
internal static class SyncPeopleDirectory
{
    /// <summary>
    /// The cavers listed for <paramref name="ctx"/>: everybody on the roster, or the members of any
    /// caving group the caller belongs to together with the caller's own caver.
    /// </summary>
    /// <remarks>
    /// Membership comes from the caller's access context, which names their groups through their
    /// own roster entry. An account without one belongs to no group and is listed nobody but
    /// themselves — and not even that, having no caver to be.
    /// </remarks>
    public static IQueryable<Caver> Listed(SilexGisDbContext db, AccessContext ctx, PeopleDirectoryScope scope)
    {
        var cavers = db.Cavers.AsNoTracking();
        if (scope == PeopleDirectoryScope.Everyone)
        {
            return cavers;
        }

        var groups = ctx.CavingGroupIds.ToList();
        var me = ctx.UserId;
        return cavers.Where(c => c.UserId == me
            || db.CavingGroupMemberships.Any(m => m.CaverId == c.Id && groups.Contains(m.CavingGroupId)));
    }

    /// <summary>Which of <paramref name="caverIds"/> the directory lists for the caller.</summary>
    public static async Task<HashSet<Guid>> ListedAmongAsync(
        SilexGisDbContext db, AccessContext ctx, SyncSettings settings, IReadOnlyCollection<Guid> caverIds, CancellationToken ct)
    {
        if (caverIds.Count == 0 || !settings.PeopleDirectoryEnabled || !CaverDirectory.MayReadPeople(ctx))
        {
            return [];
        }

        var ids = caverIds.ToList();
        return [.. await Listed(db, ctx, settings.PeopleDirectoryScope)
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct)];
    }

    /// <summary>
    /// The phone users this account has sent: the ones it last described, and the ones any of its
    /// uploads ever recorded — a phone user another account described since is still one this
    /// account's phone has.
    /// </summary>
    public static IQueryable<DevicePerson> SentBy(SilexGisDbContext db, Guid userId) =>
        db.DevicePersons.AsNoTracking().Where(p => p.UploadedByUserId == userId
            || db.ImportBatchItems.Any(i => i.DevicePersonId == p.DeviceUserId
                && db.ImportBatches.Any(b => b.Id == i.ImportBatchId && b.ConfirmedByUserId == userId)));
}
