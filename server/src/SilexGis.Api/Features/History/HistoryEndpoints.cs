// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.History;

public sealed record HistoryEventDto(
    long Id,
    DateTimeOffset At,
    Guid? UserId,
    string? UserName,
    /// <summary>created | updated | deleted.</summary>
    string Action,
    /// <summary>CLR type name: "Cave" | "CaveEntrance" | "Attachment" | "Tagging" | …</summary>
    string EntityType,
    string EntityId,
    /// <summary>Per-property diff/snapshot with noise and protection-redacted props removed.</summary>
    JsonElement? Changes,
    /// <summary>Names removed by location protection ([] otherwise) — the UI renders "hidden".</summary>
    string[] RedactedProperties);

/// <summary>
/// Per-entity change history, derived from the audit trail. A parent's timeline includes its
/// children's events via the audit root columns (an entrance's move shows in its cave's
/// history). Protection-of-history redaction mirrors the live DTO masking.
/// </summary>
public static class HistoryEndpoints
{
    public static RouteGroupBuilder MapHistoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/history", ListAsync)
            .WithTags("History")
            .WithSummary("Change history for an entity (incl. its children); protection-redacted, visibility-gated.");
        return api;
    }

    private static async Task<Results<Ok<PagedResult<HistoryEventDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        string? entityType,
        Guid? entityId,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Existence of an entity the caller cannot read is never disclosed (404, not 403/400).
        if (entityId is null || !Enum.TryParse<AttachedEntityType>(entityType, ignoreCase: true, out var type)
            || !await FileAccessRules.CanReadEntityAsync(db, user, type, entityId.Value, ct))
        {
            return ApiProblems.NotFound("history.entity_not_found");
        }

        var clr = AttachedEntityTypes.ClrName(type);
        var id = entityId.Value.ToString();

        var query = db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.Created
                || a.Action == AuditActions.Updated
                || a.Action == AuditActions.Deleted)
            // Permission-grant history stays on the admin audit page, not the public timeline.
            .Where(a => a.EntityType != nameof(ObjectAcl))
            .Where(a => (a.EntityType == clr && a.EntityId == id)
                || (a.RootEntityType == clr && a.RootEntityId == id));

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        // Names are resolved after the page materialises rather than joined in: the label a user
        // may be shown under is a rule, not a column, and it lives in one place.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value), ct);

        var parsed = rows
            .Select(r => (
                Row: r,
                UserName: r.UserId is { } actor ? labels.GetValueOrDefault(actor) : null,
                Changes: ParseChanges(r.Changes)))
            .ToList();

        // Protection-of-history: resolve every cave the rows touch, then which are hidden from
        // this caller. One batched lookup drives both governing-cave and cave-link redaction.
        var caveIds = new HashSet<Guid>();
        foreach (var (row, _, changes) in parsed)
        {
            if (GoverningCaveId(row) is { } governing)
            {
                caveIds.Add(governing);
            }

            CollectLinkedCaveIds(changes, caveIds);
        }

        var hidden = await CaveLinkRedaction.RedactedCaveIdsAsync(db, user, caveIds, ct);

        var items = parsed.Select(r =>
        {
            var governingHidden = GoverningCaveId(r.Row) is { } gc && hidden.Contains(gc);
            var (changes, redacted) = HistoryProtection.Redact(r.Row.EntityType!, r.Changes, governingHidden, hidden.Contains);
            return new HistoryEventDto(
                r.Row.Id, r.Row.At, r.Row.UserId, r.UserName, r.Row.Action,
                r.Row.EntityType!, r.Row.EntityId!,
                changes is null ? null : JsonSerializer.SerializeToElement(changes),
                [.. redacted]);
        }).ToList();

        return TypedResults.Ok(new PagedResult<HistoryEventDto>(items, p, size, total));
    }

    private static JsonObject? ParseChanges(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json) as JsonObject;

    // The cave whose protection governs a row's own coordinate fields: the cave itself for a
    // Cave row, the parent cave for a rooted cave-child row. Null for rows that only reference
    // a cave (redacted via the cave-link predicate instead).
    private static Guid? GoverningCaveId(AuditEntry row) => row.EntityType switch
    {
        nameof(Cave) => ParseGuid(row.EntityId),
        nameof(CaveEntrance) or nameof(CaveCenterline) or nameof(SurveyModel) => ParseGuid(row.RootEntityId),
        _ => null,
    };

    // CaveId values referenced inside a row's change set (surface features, trip cave-links).
    private static void CollectLinkedCaveIds(JsonObject? changes, HashSet<Guid> caveIds)
    {
        if (changes?["CaveId"] is not JsonObject pair)
        {
            return;
        }

        foreach (var side in new[] { pair["old"], pair["new"] })
        {
            if (side is JsonValue v && v.TryGetValue<string>(out var text) && Guid.TryParse(text, out var caveId))
            {
                caveIds.Add(caveId);
            }
        }
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;
}
