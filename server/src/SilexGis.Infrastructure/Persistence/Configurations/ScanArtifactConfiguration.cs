// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ScanArtifactConfiguration : IEntityTypeConfiguration<ScanArtifact>
{
    public const int FormatLength = 40;
    public const int StepIdLength = 100;
    public const int ToolLength = 100;
    public const int ToolVersionLength = 100;
    public const int HashLength = 64;

    public void Configure(EntityTypeBuilder<ScanArtifact> builder)
    {
        builder.ToTable("scan_artifacts");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Format).HasMaxLength(FormatLength);
        builder.Property(x => x.Sha256).HasMaxLength(HashLength);
        builder.Property(x => x.StepId).HasMaxLength(StepIdLength);
        builder.Property(x => x.Tool).HasMaxLength(ToolLength);
        builder.Property(x => x.ToolVersion).HasMaxLength(ToolVersionLength);
        builder.Property(x => x.ParamsHash).HasMaxLength(HashLength);
        builder.Property(x => x.InputArtifactIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");

        // An artefact is a product of its upload's run and goes with the upload; it outlives its
        // model, as the upload's other files do, since the model is what a run makes last and a
        // removed model leaves the uploaded original in place. The stored file holding the bytes
        // is never removed from under a row that names it.
        builder.HasOne<SurveyUpload>().WithMany().HasForeignKey(x => x.SurveyUploadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);

        // Named at declaration, every one: a second unnamed index over a column edits the first.
        // The upload's index leads with the three columns a rerun matches an artefact by, so one
        // index serves both the foreign key and the reuse lookup.
        builder.HasIndex(x => new { x.SurveyUploadId, x.StepId, x.ParamsHash }, "ix_scan_artifacts_upload_step_params")
            .HasDatabaseName("ix_scan_artifacts_upload_step_params");
        builder.HasIndex(x => x.SurveyModelId, "ix_scan_artifacts_survey_model_id")
            .HasDatabaseName("ix_scan_artifacts_survey_model_id");
        builder.HasIndex(x => x.FileId, "ix_scan_artifacts_file_id")
            .HasDatabaseName("ix_scan_artifacts_file_id");
    }
}
