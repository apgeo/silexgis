// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.Property(x => x.Action).HasMaxLength(50);
        builder.Property(x => x.EntityType).HasMaxLength(100);
        builder.Property(x => x.EntityId).HasMaxLength(50);
        builder.Property(x => x.RootEntityType).HasMaxLength(100);
        builder.Property(x => x.RootEntityId).HasMaxLength(50);
        builder.Property(x => x.Changes).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.At);

        // Parent-timeline lookups scan (root_entity_type, root_entity_id) newest-first; the
        // partial filter keeps the index to child rows only (the majority carry no root).
        builder.HasIndex(x => new { x.RootEntityType, x.RootEntityId, x.Id })
            .HasFilter("root_entity_type IS NOT NULL")
            .IsDescending(false, false, true);

        // Everything one logical action wrote, read back as a group (a timeline's "this upload",
        // an undo collecting what to reverse). Rows from before attribution are null and never
        // looked up by it, hence partial.
        builder.HasIndex(x => x.ChangeSetId)
            .HasDatabaseName("ix_audit_log_change_set_id")
            .HasFilter("change_set_id IS NOT NULL");

        // The rows an import batch wrote, which is how an undo of that batch finds them. Most rows
        // belong to no batch, hence partial.
        builder.HasIndex(x => x.ImportBatchId)
            .HasDatabaseName("ix_audit_log_import_batch_id")
            .HasFilter("import_batch_id IS NOT NULL");
    }
}
