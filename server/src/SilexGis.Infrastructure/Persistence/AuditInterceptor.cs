// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Writes audit_log rows for creates/updates/deletes of <see cref="IAuditable"/> entities.
/// Update entries carry a { prop: { old, new } } diff as jsonb.
/// <para>
/// Feature aggregates audit as ONE row: the supertype row and its subtype row share the
/// id and are edited together, so their entries are merged under the kind-qualified type
/// name ("Feature:Cave", …, see <see cref="FeatureAudit"/>). Property names cannot
/// collide inside a merged row — one id has exactly one kind.
/// </para>
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed record PendingEntry(
        string Action,
        string EntityType,
        string EntityId,
        string? RootEntityType,
        string? RootEntityId,
        Dictionary<string, Dictionary<string, object?>> Changes,
        bool FeatureWorld);

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var pending = new List<PendingEntry>();
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditActions.Created,
                EntityState.Modified => AuditActions.Updated,
                EntityState.Deleted => AuditActions.Deleted,
                _ => null,
            };

            if (action is null)
            {
                continue;
            }

            var kind = FeatureKindOf(entry.Entity);
            pending.Add(new PendingEntry(
                action,
                kind is null ? entry.Metadata.ClrType.Name : FeatureAudit.TypeName(kind.Value),
                entry.Entity.AuditId,
                (entry.Entity as IAuditChild)?.RootEntityType,
                (entry.Entity as IAuditChild)?.RootEntityId,
                CollectChanges(entry, action),
                kind is not null));
        }

        var entries = new List<AuditEntry>();
        // Merge the feature world by entity id: a supertype row and its subtype row edited
        // in one save become one audit row. Created/Deleted wins over Updated (creating a
        // cave adds both rows; the event is one creation).
        foreach (var group in pending.Where(p => p.FeatureWorld).GroupBy(p => p.EntityId))
        {
            var parts = group.ToList();
            var head = parts.Find(p => p.Action != AuditActions.Updated) ?? parts[0];
            var changes = parts.SelectMany(p => p.Changes)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);
            entries.Add(ToRow(head with { Changes = changes }));
        }

        entries.AddRange(pending.Where(p => !p.FeatureWorld).Select(ToRow));

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }
    }

    private AuditEntry ToRow(PendingEntry p) => new()
    {
        UserId = currentUser.UserId,
        Action = p.Action,
        EntityType = p.EntityType,
        EntityId = p.EntityId,
        RootEntityType = p.RootEntityType,
        RootEntityId = p.RootEntityId,
        Changes = p.Changes.Count == 0 ? null : JsonSerializer.Serialize(p.Changes),
    };

    // The subtype tables' kind-qualified audit identity. The kind is static per CLR type —
    // the composite FK guarantees a subtype row can only sit on a feature of its kind.
    private static FeatureKind? FeatureKindOf(IAuditable entity) => entity switch
    {
        Feature f => f.Kind,
        Cave => FeatureKind.Cave,
        CaveEntrance => FeatureKind.CaveEntrance,
        Centerline => FeatureKind.Centerline,
        _ => null,
    };

    // Update rows carry the modified props; created/deleted rows snapshot the whole entity so
    // "attachment added" / "entrance deleted" events are informative and delete forensics
    // survive. Skipped: primary keys (the audit row's EntityId already) and shadow properties
    // (computed search vectors, ltree paths, the subtype kind FK — store bookkeeping, not
    // domain change).
    private static Dictionary<string, Dictionary<string, object?>> CollectChanges(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string action)
    {
        var scalars = entry.Properties.Where(p => !p.Metadata.IsPrimaryKey() && !p.Metadata.IsShadowProperty());
        return action switch
        {
            AuditActions.Updated => scalars.Where(p => p.IsModified)
                .ToDictionary(p => p.Metadata.Name, p => Pair(p.OriginalValue, p.CurrentValue)),
            AuditActions.Created => scalars.Where(p => p.CurrentValue is not null)
                .ToDictionary(p => p.Metadata.Name, p => Pair(null, p.CurrentValue)),
            AuditActions.Deleted => scalars.Where(p => p.OriginalValue is not null)
                .ToDictionary(p => p.Metadata.Name, p => Pair(p.OriginalValue, null)),
            _ => [],
        };
    }

    private static Dictionary<string, object?> Pair(object? oldValue, object? newValue) =>
        new() { ["old"] = Plain(oldValue), ["new"] = Plain(newValue) };

    // Geometry (and anything else STJ can't represent) is stored as text in the diff. Enums
    // are stored as their camelCase name — matching how the API serializes them everywhere
    // else — so the history UI shows "private → public", not "0 → 3".
    private static object? Plain(object? value) => value switch
    {
        null => null,
        NetTopologySuite.Geometries.Geometry geometry => geometry.AsText(),
        Enum e => JsonNamingPolicy.CamelCase.ConvertName(e.ToString()),
        Guid[] ids => string.Join(',', ids),
        string or bool or Guid or DateTimeOffset or DateOnly => value,
        _ when value.GetType().IsPrimitive || value is decimal => value,
        _ => value.ToString(),
    };
}

/// <summary>Placeholder until authentication provides an HttpContext-backed implementation.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
}
