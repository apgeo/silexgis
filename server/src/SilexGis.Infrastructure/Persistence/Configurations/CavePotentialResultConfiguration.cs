// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Potential;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CavePotentialResultConfiguration : IEntityTypeConfiguration<CavePotentialResult>
{
    public void Configure(EntityTypeBuilder<CavePotentialResult> builder)
    {
        builder.ToTable("cave_potential_results", t => t.HasCheckConstraint(
            "ck_cave_potential_results_cell_meters", "cell_meters > 0"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ModelName).HasMaxLength(CavePotentialModelRules.MaxNameLength);
        builder.Property(x => x.Method).HasConversion<short>();
        builder.Property(x => x.Definition).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Summary).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.EvidenceFeatureIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.Message).HasMaxLength(500);
        builder.Property(x => x.Path).HasMaxLength(1024);

        // A result outlives the model it was run from: it holds the definition as run, and a map
        // that vanished because somebody tidied their models would take its date with it.
        builder.HasOne<CavePotentialModel>().WithMany().HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.SetNull);

        // It does not outlive the area: who may read it is asked of the area every time, and a
        // result over an area that is gone could be shown to nobody.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.AreaFeatureId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(x => x.ProcessingJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ModelId).HasDatabaseName("ix_cave_potential_results_model_id");
        builder.HasIndex(x => x.AreaFeatureId).HasDatabaseName("ix_cave_potential_results_area_feature_id");
        builder.HasIndex(x => x.RequestedByUserId).HasDatabaseName("ix_cave_potential_results_requested_by_user_id");
        builder.HasIndex(x => x.ProcessingJobId).HasDatabaseName("ix_cave_potential_results_processing_job_id");
    }
}
