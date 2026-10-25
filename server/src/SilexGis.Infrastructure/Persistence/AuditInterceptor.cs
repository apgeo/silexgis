// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

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
/// <para>
/// Every audit row added in a save — the ones written here and the ones a feature adds itself (an
/// export, a permission change) — carries the client address of the request that caused it, when
/// there is one.
/// </para>
/// <para>
/// Every one of them also names the logical action that wrote it — its change set, its source and
/// the import batch it belongs to — from the unit of work's <see cref="IChangeContext"/>. Stamped
/// here, over every added row, rather than by each writer, so that no write path can forget it:
/// the soft-delete, restore and purge rows a feature adds by hand get it the same way.
/// </para>
/// <para>
/// An edit of a feature's own shape that is too large to record as text (the history keeps a
/// summary of it instead) also keeps both full shapes beside the row, in
/// <see cref="AuditGeometryValue"/>, so that a restore can put the shape back exactly.
/// Centerlines are left out: they are rebuilt from their survey files, and they are the large ones.
/// </para>
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, IRequestOrigin origin) : SaveChangesInterceptor
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
        Dictionary<string, GeometryPair> Geometries,
        bool FeatureWorld);

    /// <summary>Both sides of a geometry whose diff could only record a summary.</summary>
    private sealed record GeometryPair(Geometry? Old, Geometry? New);

    private static readonly string CenterlineType = FeatureAudit.TypeName(FeatureKind.Centerline);

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var change = ChangeContextOf(context);
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
            var geometries = new Dictionary<string, GeometryPair>();
            pending.Add(new PendingEntry(
                action,
                kind is null ? entry.Metadata.ClrType.Name : FeatureAudit.TypeName(kind.Value),
                entry.Entity.AuditId,
                (entry.Entity as IAuditChild)?.RootEntityType,
                (entry.Entity as IAuditChild)?.RootEntityId,
                CollectChanges(entry, action, geometries),
                geometries,
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
            var geometries = parts.SelectMany(p => p.Geometries)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);
            var row = ToRow(head with { Changes = changes }, change);
            entries.Add(row);
            AddPreImages(context, row, head with { Geometries = geometries });
        }

        entries.AddRange(pending.Where(p => !p.FeatureWorld).Select(p => ToRow(p, change)));

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }

        var address = origin.ClientAddress;
        foreach (var added in context.ChangeTracker.Entries<AuditEntry>())
        {
            if (added.State != EntityState.Added)
            {
                continue;
            }

            var row = added.Entity;
            row.Ip ??= address;

            // A writer that already named the action (or the user) keeps its answer; the context
            // fills in what it left empty.
            if (change is not null)
            {
                row.ChangeSetId ??= change.ChangeSetId;
                row.Source ??= change.Source;
                row.ImportBatchId ??= change.ImportBatchId;
                row.UserId ??= change.ActingUserId;
            }
        }
    }

    // The interceptor is one instance shared by every context, while the change context belongs to
    // one unit of work, so it is read from the services the context itself was created in rather
    // than taken in the constructor. Null for a context created by hand outside dependency
    // injection, whose rows are then written without attribution rather than with a wrong one.
    private static IChangeContext? ChangeContextOf(DbContext context) =>
        context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider
            ?.GetService<IChangeContext>();

    // Only an edit's pre-image is ever put back (a creation has no "before", and a hard delete's
    // snapshot is not restored from), so only update rows keep shapes; the new side is kept too,
    // because undoing an upload compares the current shape with what the upload wrote.
    private static void AddPreImages(DbContext context, AuditEntry row, PendingEntry p)
    {
        if (p.Action != AuditActions.Updated || p.EntityType == CenterlineType || p.Geometries.Count == 0)
        {
            return;
        }

        context.Set<AuditGeometryValue>().AddRange(p.Geometries.Select(g => new AuditGeometryValue
        {
            AuditEntry = row,
            Property = g.Key,
            OldWkb = g.Value.Old is null ? null : AuditGeometry.PreImage(g.Value.Old),
            NewWkb = g.Value.New is null ? null : AuditGeometry.PreImage(g.Value.New),
        }));
    }

    private AuditEntry ToRow(PendingEntry p, IChangeContext? change) => new()
    {
        UserId = currentUser.UserId ?? change?.ActingUserId,
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
    //
    // An updated geometry whose diff records a summary on either side is also handed back in
    // geometries, with both full shapes, for the pre-image kept beside the row.
    private static Dictionary<string, Dictionary<string, object?>> CollectChanges(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        string action,
        Dictionary<string, GeometryPair> geometries)
    {
        var scalars = entry.Properties.Where(p => !p.Metadata.IsPrimaryKey() && !p.Metadata.IsShadowProperty());
        return action switch
        {
            AuditActions.Updated => scalars.Where(p => p.IsModified)
                .ToDictionary(p => p.Metadata.Name, p =>
                {
                    var pair = Pair(p.OriginalValue, p.CurrentValue);
                    if (pair["old"] is Dictionary<string, object?> || pair["new"] is Dictionary<string, object?>)
                    {
                        geometries[p.Metadata.Name] = new GeometryPair(
                            p.OriginalValue as Geometry, p.CurrentValue as Geometry);
                    }

                    return pair;
                }),
            AuditActions.Created => scalars.Where(p => p.CurrentValue is not null)
                .ToDictionary(p => p.Metadata.Name, p => Pair(null, p.CurrentValue)),
            AuditActions.Deleted => scalars.Where(p => p.OriginalValue is not null)
                .ToDictionary(p => p.Metadata.Name, p => Pair(p.OriginalValue, null)),
            _ => [],
        };
    }

    private static Dictionary<string, object?> Pair(object? oldValue, object? newValue) =>
        new() { ["old"] = Plain(oldValue), ["new"] = Plain(newValue) };

    // Geometry (and anything else STJ can't represent) is stored as text in the diff — or, past
    // a few kilobytes of text, as a position-free summary (AuditGeometry says why). Enums
    // are stored as their camelCase name — matching how the API serializes them everywhere
    // else — so the history UI shows "private → public", not "0 → 3".
    private static object? Plain(object? value) => value switch
    {
        null => null,
        Geometry geometry => AuditGeometry.Record(geometry),
        Enum e => JsonNamingPolicy.CamelCase.ConvertName(e.ToString()),
        Guid[] ids => string.Join(',', ids),
        string[] values => string.Join(',', values),
        string or bool or Guid or DateTimeOffset or DateOnly => value,
        _ when value.GetType().IsPrimitive || value is decimal => value,
        _ => value.ToString(),
    };
}

/// <summary>
/// No signed-in caller: the default for work with no request behind it, such as seeding. The web
/// host replaces it with the request's caller.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
}

/// <summary>No request behind the work, so no address: the default outside the web host.</summary>
public sealed class NoRequestOrigin : IRequestOrigin
{
    public System.Net.IPAddress? ClientAddress => null;
}
