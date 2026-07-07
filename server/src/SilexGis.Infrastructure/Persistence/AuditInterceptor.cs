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

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entries = new List<AuditEntry>();
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

            entries.Add(new AuditEntry
            {
                UserId = currentUser.UserId,
                Action = action,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.AuditId,
                Changes = action == AuditActions.Updated ? SerializeDiff(entry) : null,
            });
        }

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }
    }

    private static string? SerializeDiff(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var diff = entry.Properties
            .Where(p => p.IsModified)
            .ToDictionary(
                p => p.Metadata.Name,
                p => new { old = Plain(p.OriginalValue), @new = Plain(p.CurrentValue) });

        return diff.Count == 0 ? null : JsonSerializer.Serialize(diff);
    }

    // Geometry (and anything else STJ can't represent) is stored as text in the diff.
    private static object? Plain(object? value) => value switch
    {
        null => null,
        NetTopologySuite.Geometries.Geometry geometry => geometry.AsText(),
        string or bool or Guid or DateTimeOffset or DateOnly or Enum => value,
        _ when value.GetType().IsPrimitive || value is decimal => value,
        _ => value.ToString(),
    };
}

/// <summary>Placeholder until authentication provides an HttpContext-backed implementation.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
}
