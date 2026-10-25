// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CaveSurveyConfiguration : IEntityTypeConfiguration<CaveSurvey>
{
    public void Configure(EntityTypeBuilder<CaveSurvey> builder)
    {
        builder.ToTable("cave_surveys");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(SurveyCaveNaming.MaxLength);
        builder.Property(x => x.ExternalKey).HasMaxLength(CaveSurveyMatching.MaxExternalKeyLength);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Source).HasConversion<short>();

        // A survey dies with its cave (purge path), as the cave's sources and models do.
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CaveFeatureId, "ix_cave_surveys_cave_feature_id")
            .HasDatabaseName("ix_cave_surveys_cave_feature_id");

        // What a re-upload without the survey's id is matched by. Held by the database so that two
        // uploads of one new survey arriving together cannot each start a survey of their own: the
        // second is refused and finds the first. A survey with no tool-given name is never matched,
        // so it is left out.
        builder.HasIndex(x => new { x.CaveFeatureId, x.Source, x.ExternalKey }, "ux_cave_surveys_external_key")
            .HasDatabaseName("ux_cave_surveys_external_key")
            .HasFilter("external_key IS NOT NULL")
            .IsUnique();
    }
}

public sealed class SurveyUploadConfiguration : IEntityTypeConfiguration<SurveyUpload>
{
    /// <summary>Column widths, shared with the writes that cut text down to them.</summary>
    public const int MessageLength = 500;

    public const int ErrorCodeLength = 100;

    public const int ErrorMessageLength = 2000;

    public const int LogTailLength = 8000;

    public void Configure(EntityTypeBuilder<SurveyUpload> builder)
    {
        // Progress is written by a worker rather than accepted from a request, so the database is
        // the only place that can hold it to its meaning.
        builder.ToTable("survey_uploads", t => t.HasCheckConstraint(
            "ck_survey_uploads_progress_range", "progress >= 0 and progress <= 100"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Source).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Stage).HasConversion<short>();
        builder.Property(x => x.DocumentsVisibility).HasConversion<short>();

        builder.Property(x => x.ClientName).HasMaxLength(100);
        builder.Property(x => x.ClientVersion).HasMaxLength(50);
        builder.Property(x => x.SurveyTitle).HasMaxLength(SurveyCaveNaming.MaxLength);
        builder.Property(x => x.SurveyName).HasMaxLength(CaveSurveyMatching.MaxExternalKeyLength);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Team).HasMaxLength(1000);
        builder.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Message).HasMaxLength(MessageLength);
        builder.Property(x => x.ErrorCode).HasMaxLength(ErrorCodeLength);
        builder.Property(x => x.ErrorMessage).HasMaxLength(ErrorMessageLength);
        builder.Property(x => x.LogTail).HasMaxLength(LogTailLength);
        builder.Property(x => x.CompilerVersion).HasMaxLength(100);

        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CaveSurvey>().WithMany().HasForeignKey(x => x.CaveSurveyId).OnDelete(DeleteBehavior.Cascade);

        // The batch is the record of what the upload archived and is never deleted while anything
        // names it.
        builder.HasOne<UploadBatch>().WithMany().HasForeignKey(x => x.UploadBatchId).OnDelete(DeleteBehavior.Restrict);

        // An upload outlives the account that sent it, as the upload batch it archived into does:
        // where a survey came from is asked about long after its uploader has left.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CaveFeatureId, "ix_survey_uploads_cave_feature_id")
            .HasDatabaseName("ix_survey_uploads_cave_feature_id");
        builder.HasIndex(x => x.UploadBatchId, "ix_survey_uploads_upload_batch_id")
            .HasDatabaseName("ix_survey_uploads_upload_batch_id");

        // A survey's versions, newest first — and two versions of one survey never share a stamp,
        // because every artefact name carries it.
        builder.HasIndex(x => new { x.CaveSurveyId, x.VersionAt }, "ux_survey_uploads_version")
            .HasDatabaseName("ux_survey_uploads_version")
            .IsUnique();

        // "My uploads", newest first.
        builder.HasIndex(x => new { x.RequestedBy, x.CreatedAt }, "ix_survey_uploads_requested_by_created_at")
            .HasDatabaseName("ix_survey_uploads_requested_by_created_at");

        // The sweep that abandons sessions which stopped receiving.
        builder.HasIndex(x => x.ExpiresAt, "ix_survey_uploads_receiving_expires_at")
            .HasDatabaseName("ix_survey_uploads_receiving_expires_at")
            .HasFilter($"status = {(short)SurveyUploadStatus.Receiving}");
    }
}

public sealed class SurveyUploadFileConfiguration : IEntityTypeConfiguration<SurveyUploadFile>
{
    public void Configure(EntityTypeBuilder<SurveyUploadFile> builder)
    {
        builder.ToTable("survey_upload_files");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.Role).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.RefusalCode).HasMaxLength(100);

        builder.HasOne<SurveyUpload>().WithMany().HasForeignKey(x => x.SurveyUploadId).OnDelete(DeleteBehavior.Cascade);

        // What a file became is referenced without being held: the document, the source and the
        // model each have their own way of being deleted, and this row then stops naming it.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SurveySource>().WithMany().HasForeignKey(x => x.SurveySourceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId).OnDelete(DeleteBehavior.SetNull);

        // A file is addressed by its name within its upload.
        builder.HasIndex(x => new { x.SurveyUploadId, x.Name }, "ux_survey_upload_files_name")
            .HasDatabaseName("ux_survey_upload_files_name")
            .IsUnique();
        builder.HasIndex(x => x.DocumentId, "ix_survey_upload_files_document_id")
            .HasDatabaseName("ix_survey_upload_files_document_id");
        builder.HasIndex(x => x.SurveySourceId, "ix_survey_upload_files_survey_source_id")
            .HasDatabaseName("ix_survey_upload_files_survey_source_id");
        builder.HasIndex(x => x.SurveyModelId, "ix_survey_upload_files_survey_model_id")
            .HasDatabaseName("ix_survey_upload_files_survey_model_id");
    }
}

public sealed class SurveyUploadPreferenceConfiguration : IEntityTypeConfiguration<SurveyUploadPreference>
{
    public void Configure(EntityTypeBuilder<SurveyUploadPreference> builder)
    {
        builder.ToTable("user_survey_upload_preferences");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).ValueGeneratedNever();

        builder.Property(x => x.AlwaysOutputs).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

        // No database default for the link switch: the entity starts it at true, and a database
        // default of true would make an explicit false indistinguishable from "not set" on insert.

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
