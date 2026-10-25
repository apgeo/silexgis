// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurveyModelConfiguration : IEntityTypeConfiguration<SurveyModel>
{
    public void Configure(EntityTypeBuilder<SurveyModel> builder)
    {
        builder.ToTable("survey_models");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Format).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.ProcessingError).HasMaxLength(1000);
        // One survey name, held to the same length every other survey name column is.
        builder.Property(x => x.RootSurveyName).HasMaxLength(400);
        builder.Property(x => x.Anchor).HasColumnType("geometry(Point, 4326)");

        // Models die with their cave (purge path); the stored file survives.
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany()
            .HasForeignKey(x => x.ConvertedFileId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CaveFeatureId);

        // At most one current model per cave and kind. Two partial indexes rather than one over a
        // kind column, because the kind is a function of the format and a second column saying the
        // same thing could disagree with the first. PostgreSQL checks these per statement, so the
        // code that moves the mark demotes in one statement and promotes in the next.
        // Named at declaration, because an index over the same column declared without a name is
        // the plain index above, and a second unnamed declaration edits it rather than adding one.
        builder.HasIndex(x => x.CaveFeatureId, "ix_survey_models_current_line_plot")
            .HasDatabaseName("ix_survey_models_current_line_plot")
            .HasFilter($"is_current AND format <> {(short)SurveyModelFormat.Stl}")
            .IsUnique();
        builder.HasIndex(x => x.CaveFeatureId, "ix_survey_models_current_wall_mesh")
            .HasDatabaseName("ix_survey_models_current_wall_mesh")
            .HasFilter($"is_current AND format = {(short)SurveyModelFormat.Stl}")
            .IsUnique();

        // The survey and the upload a model was compiled from, when a run produced it. Released
        // rather than held when they go, as the cave's other survey material is.
        builder.HasOne<CaveSurvey>().WithMany().HasForeignKey(x => x.CaveSurveyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SurveyUpload>().WithMany().HasForeignKey(x => x.SurveyUploadId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.CaveSurveyId, "ix_survey_models_cave_survey_id")
            .HasDatabaseName("ix_survey_models_cave_survey_id");
        builder.HasIndex(x => x.SurveyUploadId, "ix_survey_models_survey_upload_id")
            .HasDatabaseName("ix_survey_models_survey_upload_id");
    }
}
