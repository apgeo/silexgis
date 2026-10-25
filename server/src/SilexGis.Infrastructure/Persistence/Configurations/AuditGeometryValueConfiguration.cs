// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class AuditGeometryValueConfiguration : IEntityTypeConfiguration<AuditGeometryValue>
{
    public void Configure(EntityTypeBuilder<AuditGeometryValue> builder)
    {
        builder.ToTable("audit_geometry_values");
        builder.HasKey(x => new { x.AuditEntryId, x.Property });
        builder.Property(x => x.Property).HasMaxLength(100);

        // The shapes belong to their history row and go with it; nothing else points here.
        builder.HasOne(x => x.AuditEntry)
            .WithMany()
            .HasForeignKey(x => x.AuditEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
