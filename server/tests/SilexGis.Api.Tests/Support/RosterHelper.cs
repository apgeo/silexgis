// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Roster seeding for tests. Membership belongs to people, so putting an account in a caving
/// group means giving it a caver row first — the same two steps the application takes.
/// </summary>
public static class RosterHelper
{
    /// <summary>The account's roster entry, created if this test never made one.</summary>
    public static async Task<Caver> CaverForAsync(SilexGisDbContext db, Guid userId, string? fullName = null)
    {
        var existing = await db.Cavers.FirstOrDefaultAsync(c => c.UserId == userId);
        if (existing is not null)
        {
            return existing;
        }

        var caver = new Caver { FullName = fullName ?? $"Caver {userId.ToString("N")[..8]}", UserId = userId };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver;
    }

    /// <summary>The roster id of an account, for request bodies that name a person.</summary>
    public static async Task<Guid> CaverIdForAsync(SilexGisApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await CaverForAsync(db, userId)).Id;
    }

    /// <summary>Puts an account in a caving group, creating its roster entry on the way.</summary>
    public static async Task<Caver> AddMemberAsync(
        SilexGisDbContext db, Guid cavingGroupId, Guid userId, CavingGroupRole role = CavingGroupRole.Member)
    {
        var caver = await CaverForAsync(db, userId);
        db.CavingGroupMemberships.Add(new CavingGroupMembership
        {
            CavingGroupId = cavingGroupId,
            CaverId = caver.Id,
            Role = role,
        });
        await db.SaveChangesAsync();
        return caver;
    }

    /// <summary>A request context for an account, with its real caving-group membership.</summary>
    public static async Task<UserContext> ContextOfAsync(SilexGisDbContext db, Guid userId) =>
        new(userId, await (
            from membership in db.CavingGroupMemberships
            join caver in db.Cavers on membership.CaverId equals caver.Id
            where caver.UserId == userId
            select membership.CavingGroupId)
            .ToListAsync());

    /// <summary>The account's access context, resolved exactly as a request resolves it.</summary>
    public static Task<SilexGis.Domain.Access.AccessContext> AccessContextOfAsync(
        SilexGisDbContext db, Guid userId) =>
        SilexGis.Infrastructure.Permissions.AccessContextResolver.ResolveAsync(db, userId);
}
