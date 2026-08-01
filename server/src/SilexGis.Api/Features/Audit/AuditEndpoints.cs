// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
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
    private static readonly string FeatureKindPrefix = $"{FeatureAudit.RootName}:";

    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", ListAsync)
            .WithTags("Audit")
            .WithSummary("Audit trail, filterable by entity (\"Feature\" selects every feature kind); admins only until per-object managers exist.");
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
            // Feature rows are typed by kind ("Feature:Cave", "Feature:Generic", …), so the bare
            // word selects the whole feature world and a qualified value one kind. The prefix
            // carries the separator: FeatureLink/FeatureShare/FeatureType are unrelated types
            // that would otherwise be swept in.
            query = entityType == FeatureAudit.RootName
                ? query.Where(x => x.EntityType != null && x.EntityType.StartsWith(FeatureKindPrefix))
                : query.Where(x => x.EntityType == entityType);
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
            .ToListAsync(ct);

        // Names are resolved after the page materialises rather than joined in: the label a user
        // may be shown under is a rule, not a column, and it lives in one place.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value), ct);

        var items = rows.Select(x => new AuditEntryDto(
            x.Id,
            x.At,
            x.UserId,
            x.UserId is { } actor ? labels.GetValueOrDefault(actor) : null,
            x.Action,
            x.EntityType,
            x.EntityId,
            x.Changes is null ? null : JsonSerializer.Deserialize<JsonElement>(x.Changes))).ToList();

        return TypedResults.Ok(new PagedResult<AuditEntryDto>(items, p, size, total));
    }
}
