// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class StorageReportSnapshotConfiguration : IEntityTypeConfiguration<StorageReportSnapshot>
{
    public void Configure(EntityTypeBuilder<StorageReportSnapshot> builder)
    {
        builder.ToTable("storage_report_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Report).HasColumnType("jsonb");

        // Every read is "the latest" or "the latest at or before a day": both walk this index
        // from its top.
        builder.HasIndex(x => x.TakenAt).HasDatabaseName("ix_storage_report_snapshots_taken_at");
    }
}
