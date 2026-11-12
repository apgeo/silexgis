// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TestDataRunConfiguration : IEntityTypeConfiguration<TestDataRun>
{
    public void Configure(EntityTypeBuilder<TestDataRun> builder)
    {
        builder.ToTable("test_data_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Profile).HasColumnType("jsonb");
        builder.Property(x => x.Counts).HasColumnType("jsonb");
        builder.Property(x => x.Notes).HasColumnType("jsonb");
        builder.Property(x => x.Phase).HasMaxLength(64);

        // The page lists runs newest first, and "is a run already going?" reads the same index.
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_test_data_runs_created_at");
    }
}

public sealed class TestDataRowConfiguration : IEntityTypeConfiguration<TestDataRow>
{
    public void Configure(EntityTypeBuilder<TestDataRow> builder)
    {
        builder.ToTable("test_data_rows");

        // Run first: every read is "the rows of this run", by table or in written order.
        builder.HasKey(x => new { x.RunId, x.TableName, x.RowId });
        builder.Property(x => x.TableName).HasMaxLength(64);
        builder.Property(x => x.RowId).HasMaxLength(64);
        builder.HasIndex(x => new { x.RunId, x.Sequence }).HasDatabaseName("ix_test_data_rows_run_id_sequence");

        // "Was this row generated?" is asked by table and key, across runs.
        builder.HasIndex(x => new { x.TableName, x.RowId }).HasDatabaseName("ix_test_data_rows_table_name_row_id");

        builder.HasOne<TestDataRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}
