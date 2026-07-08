// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Audit;

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset At,
    Guid? UserId,
    string? UserName,
    string Action,
    string? EntityType,
    string? EntityId,
    /// <summary>Per-property diff: { "prop": { "old": …, "new": … } }.</summary>
    JsonElement? Changes);

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", ListAsync)
            .WithTags("Audit")
            .WithSummary("Audit trail, filterable by entity; admins only until per-object managers exist.");
        return api;
    }

    private static async Task<Results<Ok<PagedResult<AuditEntryDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        string? entityType,
        string? entityId,
        string? action,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The trail spans every entity including ones the caller couldn't read —
        // admin-only until the ACL phase introduces per-object managers.
        if (!user.IsAdmin)
        {
            return ApiProblems.Forbidden("audit.requires_admin");
        }

        var query = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(x => x.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(x => x.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Action == action);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .GroupJoin(db.Users.AsNoTracking(), a => a.UserId, u => u.Id, (a, users) => new { a, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new
            {
                x.a,
                UserName = u == null ? null : (u.DisplayName ?? u.UserName),
            })
            .ToListAsync(ct);

        var items = rows.Select(x => new AuditEntryDto(
            x.a.Id,
            x.a.At,
            x.a.UserId,
            x.UserName,
            x.a.Action,
            x.a.EntityType,
            x.a.EntityId,
            x.a.Changes is null ? null : JsonSerializer.Deserialize<JsonElement>(x.a.Changes))).ToList();

        return TypedResults.Ok(new PagedResult<AuditEntryDto>(items, p, size, total));
    }
}
