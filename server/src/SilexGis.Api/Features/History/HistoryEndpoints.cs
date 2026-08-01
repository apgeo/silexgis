// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.History;

public sealed record HistoryEventDto(
    long Id,
    DateTimeOffset At,
    Guid? UserId,
    string? UserName,
    /// <summary>created | updated | deleted.</summary>
    string Action,
    /// <summary>Audit type name: "Feature:Cave" | "Feature:CaveEntrance" | "Attachment" | "TripLog" | …</summary>
    string EntityType,
    string EntityId,
    /// <summary>Per-property diff/snapshot with noise and protection-redacted props removed.</summary>
    JsonElement? Changes,
    /// <summary>Names removed by location protection ([] otherwise) — the UI renders "hidden".</summary>
    string[] RedactedProperties);

/// <summary>
/// Per-entity change history, derived from the audit trail. A feature's timeline covers its
/// whole containment subtree (an entrance's move shows in its cave's history, a cave's edit in
/// its karst area's) plus the satellites pointing at it; non-feature entities keep the stamped
/// parent pointer. Protection-of-history redaction mirrors the live DTO masking.
/// </summary>
public static class HistoryEndpoints
{
    /// <summary>
    /// Audit type names of the feature world. Matched as a closed set rather than by prefix:
    /// FeatureLink/FeatureShare/FeatureType share the leading word without being feature rows,
    /// and equality keeps the (entity_type, entity_id) index usable.
    /// </summary>
    private static readonly string[] FeatureTypeNames =
        [.. Enum.GetValues<FeatureKind>().Select(FeatureAudit.TypeName)];

    /// <summary>
    /// Properties whose value is a reference to another feature. A record carrying exact
    /// coordinates discloses a protected target by proximity, so these ids are redacted
    /// independently of the row's own protection.
    /// </summary>
    private static readonly string[] ReferenceProperties =
        [nameof(FeatureLink.FromId), nameof(FeatureLink.ToId), nameof(TripLogCave.CaveId)];

    public static RouteGroupBuilder MapHistoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/history", ListAsync)
            .WithTags("History")
            .WithSummary("Change history for an entity (features incl. their subtree); protection-redacted, visibility-gated.");
        return api;
    }

    private static async Task<Results<Ok<PagedResult<HistoryEventDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
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
        var query = entityType is null || entityId is null
            ? null
            : await TimelineAsync(db, user, entityType, entityId.Value, ct);
        if (query is null)
        {
            return ApiProblems.NotFound("history.entity_not_found");
        }

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

        // Protection-of-history: one batched lookup drives both redaction inputs. A row's own
        // coordinate fields are governed by the feature the row belongs to, and the feature
        // references inside its diff by their targets — both are hidden under exactly the same
        // rule (the caller may not see that feature's exact location), so one set answers both.
        var involved = new HashSet<Guid>();
        foreach (var (row, _, changes) in parsed)
        {
            if (GoverningFeatureId(row) is { } governing)
            {
                involved.Add(governing);
            }

            CollectReferencedFeatureIds(changes, involved);
        }

        var hidden = await protection.RedactedLinkTargetIdsAsync(user, involved, ct);

        var items = parsed.Select(r =>
        {
            var governingHidden = GoverningFeatureId(r.Row) is { } governing && hidden.Contains(governing);
            var (changes, redacted) = HistoryProtection.Redact(
                r.Row.EntityType!, r.Changes, governingHidden, hidden.Contains);
            return new HistoryEventDto(
                r.Row.Id, r.Row.At, r.Row.UserId, r.UserName, r.Row.Action,
                r.Row.EntityType!, r.Row.EntityId!,
                changes is null ? null : JsonSerializer.SerializeToElement(changes),
                [.. redacted]);
        }).ToList();

        return TypedResults.Ok(new PagedResult<HistoryEventDto>(items, p, size, total));
    }

    /// <summary>
    /// The audit rows making up one entity's timeline, or null when the caller may not read
    /// the entity (indistinguishable from "does not exist" by design).
    /// </summary>
    private static async Task<IQueryable<AuditEntry>?> TimelineAsync(
        SilexGisDbContext db, UserContext user, string entityType, Guid entityId, CancellationToken ct)
    {
        var rows = db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.Created
                || a.Action == AuditActions.Updated
                || a.Action == AuditActions.Deleted)
            // Permission-grant history stays on the admin audit page, not the public timeline.
            .Where(a => a.EntityType != nameof(ObjectAcl));

        // Every physical feature is addressed uniformly: the kind lives in the rows, not in the
        // query parameter ("Feature:Cave", "Feature:Generic", …).
        if (string.Equals(entityType, FeatureAudit.RootName, StringComparison.OrdinalIgnoreCase))
        {
            if (!await db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls).AnyAsync(f => f.Id == entityId, ct))
            {
                return null;
            }

            // Subtree scope resolved from the containment closure at read time instead of from a
            // pointer stamped when the row was written: it covers any depth, and it follows
            // re-parenting immediately (a cave moved under another area takes its history along).
            // Soft-deleted descendants stay in scope — their deletion is precisely the event a
            // timeline exists to show — while descendants the caller may not read contribute
            // nothing, so a private child cannot leak through its parent's timeline.
            var scope = await db.Features.AsNoTracking().IgnoreQueryFilters()
                .VisibleTo(user, db.ObjectAcls)
                .Where(f => db.FeatureAncestors.Any(a => a.AncestorId == entityId && a.FeatureId == f.Id))
                .Select(f => f.Id)
                .ToListAsync(ct);

            // The trail keys entities as text (it spans mixed key types), so the id set crosses
            // the boundary as strings — same format the audit interceptor writes.
            var keys = scope.Select(id => id.ToString()).ToList();
            return rows.Where(a =>
                (a.EntityType != null && FeatureTypeNames.Contains(a.EntityType)
                    && a.EntityId != null && keys.Contains(a.EntityId))
                || (a.RootEntityType == FeatureAudit.RootName
                    && a.RootEntityId != null && keys.Contains(a.RootEntityId)));
        }

        if (!Enum.TryParse<AttachedEntityType>(entityType, ignoreCase: true, out var type)
            || !await FileAccessRules.CanReadEntityAsync(db, user, type, entityId, ct))
        {
            return null;
        }

        var clr = AttachedEntityTypes.ClrName(type);
        var key = entityId.ToString();
        return rows.Where(a => (a.EntityType == clr && a.EntityId == key)
            || (a.RootEntityType == clr && a.RootEntityId == key));
    }

    private static JsonObject? ParseChanges(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json) as JsonObject;

    // The feature whose protected ancestry governs a row's own coordinate fields: the feature
    // itself for a feature row, the pointed-at feature for a satellite of one (survey models,
    // attachments, taggings). Null for rows that merely reference a feature — those go through
    // the link-target predicate instead. Unresolvable ids stay null and the row is treated as
    // ungoverned only for its own fields; a missing feature never yields exact view.
    private static Guid? GoverningFeatureId(AuditEntry row) =>
        row.EntityType is { } type && FeatureAudit.IsFeatureType(type) ? ParseGuid(row.EntityId)
        : row.RootEntityType == FeatureAudit.RootName ? ParseGuid(row.RootEntityId)
        : null;

    private static void CollectReferencedFeatureIds(JsonObject? changes, HashSet<Guid> ids)
    {
        if (changes is null)
        {
            return;
        }

        foreach (var property in ReferenceProperties)
        {
            if (changes[property] is not JsonObject pair)
            {
                continue;
            }

            foreach (var side in new[] { pair["old"], pair["new"] })
            {
                if (side is JsonValue value && value.TryGetValue<string>(out var text)
                    && Guid.TryParse(text, out var id))
                {
                    ids.Add(id);
                }
            }
        }
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;
}
