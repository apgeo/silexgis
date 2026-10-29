// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TermRuleSetConfiguration : IEntityTypeConfiguration<TermRuleSet>
{
    public void Configure(EntityTypeBuilder<TermRuleSet> builder)
    {
        builder.ToTable("term_rule_sets", t =>
        {
            // A group set names its group and nothing else does: without this, a set could
            // claim a group scope with no group, and the resolution walk would step over it
            // for ever without anyone being able to see why.
            t.HasCheckConstraint(
                "ck_term_rule_sets_group",
                "(scope = 1 and caving_group_id is not null) or (scope <> 1 and caving_group_id is null)");
        });
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Scope).HasConversion<short>();
        builder.Property(x => x.Rules).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // A closed account does not take a club's rules with it — the set outlives its author
        // and an administrator can still edit it.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.Cascade);

        // One default per scope instance. Partial unique indexes rather than application
        // checks, because promotion is the operation two administrators are most likely to
        // race on and the loser must be told rather than silently win.
        builder.HasIndex(x => x.Scope)
            .IsUnique()
            .HasFilter("is_default and scope = 0")
            .HasDatabaseName("ux_term_rule_sets_installation_default");
        builder.HasIndex(x => x.CavingGroupId)
            .IsUnique()
            .HasFilter("is_default and scope = 1")
            .HasDatabaseName("ux_term_rule_sets_group_default");
        builder.HasIndex(x => x.OwnerUserId)
            .IsUnique()
            .HasFilter("is_default and scope = 2")
            .HasDatabaseName("ux_term_rule_sets_user_default");

        builder.HasIndex(x => x.IsSeeded).HasFilter("is_seeded");
    }
}

public sealed class GeofileImportSessionConfiguration : IEntityTypeConfiguration<GeofileImportSession>
{
    public void Configure(EntityTypeBuilder<GeofileImportSession> builder)
    {
        builder.ToTable("geofile_import_sessions");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Decisions).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // A review of a file that is gone is not a review; deleting the upload takes it.
        builder.HasOne<Geofile>().WithMany().HasForeignKey(x => x.GeofileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.GeofileId, x.UserId }).IsUnique();
    }
}

public sealed class PhotoImportSessionConfiguration : IEntityTypeConfiguration<PhotoImportSession>
{
    public void Configure(EntityTypeBuilder<PhotoImportSession> builder)
    {
        builder.ToTable("photo_import_sessions");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileIds).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Decisions).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // One review per person: a drop is a sitting, and starting a new one replaces the last.
        // The unique index is what makes that true of the database rather than of the handler.
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}

public sealed class TripImportSessionConfiguration : IEntityTypeConfiguration<TripImportSession>
{
    public void Configure(EntityTypeBuilder<TripImportSession> builder)
    {
        builder.ToTable("trip_import_sessions");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Decisions).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // A review of a file that is gone is not a review; deleting the upload takes it.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.StoredFileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One review per person per file: two people going through the same sheet keep their own
        // decisions instead of overwriting each other. The unique index is what makes that true
        // of the database rather than of the handler.
        builder.HasIndex(x => new { x.StoredFileId, x.UserId }).IsUnique();
    }
}

public sealed class SpeleolocImportSessionConfiguration : IEntityTypeConfiguration<SpeleolocImportSession>
{
    public void Configure(EntityTypeBuilder<SpeleolocImportSession> builder)
    {
        builder.ToTable("speleoloc_import_sessions");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Decisions).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // A review of an archive that is gone is not a review; deleting the upload takes it.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.StoredFileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One review per person per archive: two people going through the same upload keep their
        // own decisions instead of overwriting each other. The unique index is what makes that
        // true of the database rather than of the handler.
        builder.HasIndex(x => new { x.StoredFileId, x.UserId }).IsUnique();
    }
}

public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("import_batches");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TermRuleSetName).HasMaxLength(200);
        builder.Property(x => x.Mode).HasConversion<short>();
        builder.Property(x => x.Source).HasConversion<short>();
        builder.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // The geofile goes; the batch stays. A batch is the record of what was created from a
        // file, and that record outliving the upload is the whole point.
        builder.HasOne<Geofile>().WithMany().HasForeignKey(x => x.GeofileId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TermRuleSet>().WithMany().HasForeignKey(x => x.TermRuleSetId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        // Same reasoning as the geofile: deleting the trip a drop was filed under must not
        // erase the record of what that drop created.
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.SyncResult).HasColumnType("jsonb");
        builder.Property(x => x.Failures).HasColumnType("jsonb");

        builder.HasIndex(x => x.GeofileId);
        builder.HasIndex(x => x.ConfirmedByUserId);

        // What makes a resent upload answerable rather than applied twice, and the database's
        // job rather than the handler's: two copies of the same request can be in flight at
        // once, so a check-then-insert would let both through. Scoped to the account because a
        // device identifier is only ever meaningful inside the account that minted it, and
        // because a batch identifier that collided across accounts would say that somebody
        // else's batch exists.
        builder.HasIndex(x => new { x.ConfirmedByUserId, x.SyncBatchId })
            .IsUnique()
            .HasFilter("sync_batch_id is not null")
            .HasDatabaseName("ux_import_batches_user_sync_batch");

        builder.Property(x => x.ReviewState).HasConversion<short>();
        builder.Property(x => x.ReviewNote).HasMaxLength(2000);
        builder.Property(x => x.HeldRequest).HasColumnType("jsonb");
        // A reviewer's account removed later leaves the record of the decision, unattributed,
        // rather than the decision itself going or blocking the removal.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        // The review queue: the batches still waiting, oldest first. Almost every batch was never
        // held, so the index covers only those that were.
        builder.HasIndex(x => new { x.ReviewState, x.HeldAt })
            .HasFilter("review_state <> 0")
            .HasDatabaseName("ix_import_batches_review_state_held_at");
    }
}

public sealed class ImportBatchItemConfiguration : IEntityTypeConfiguration<ImportBatchItem>
{
    public void Configure(EntityTypeBuilder<ImportBatchItem> builder)
    {
        builder.ToTable("import_batch_items");

        builder.Property(x => x.RuleId).HasMaxLength(100);
        builder.Property(x => x.RuleName).HasMaxLength(200);
        builder.Property(x => x.Action).HasConversion<short>();
        builder.Property(x => x.SourceProperties).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.AttachmentIds).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);

        // A purged feature takes its provenance line with it; a soft-deleted one keeps it,
        // which is what makes a reverted batch restorable and auditable.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.AttachedToFeatureId).OnDelete(DeleteBehavior.SetNull);
        // The picture a line was made from goes null rather than taking the line with it: a
        // batch that says "a cave was created here from a photograph somebody has since
        // deleted" is still the answer somebody needs.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.SetNull);
        // A trip deleted by hand afterwards leaves the line that says it was imported, the same
        // way a deleted upload does. Taking the line with it would erase the record of an import
        // one of whose rows somebody later removed, which is exactly the import worth finding.
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.SetNull);
        // The one pointer here that goes with its object rather than outliving it. A position
        // event is corrected in place and keeps its row, so this line survives a correction and
        // still names the row the import wrote. A row taken off the log is kept and still named
        // by its line, so it can be put back and an undo still takes it; the row goes only when
        // it is destroyed for good, and a line still naming a row that has gone would send an undo
        // looking for something nobody can find. Cascade, so the line goes with the row.
        builder.HasOne<TripPositionEvent>().WithMany().HasForeignKey(x => x.TripPositionEventId)
            .OnDelete(DeleteBehavior.Cascade);
        // A phone user an undo removes, or one deleted afterwards, leaves the line that says the
        // upload sent them, for the reason the trip pointer outlives its trip.
        builder.HasOne<DevicePerson>().WithMany().HasForeignKey(x => x.DevicePersonId)
            .OnDelete(DeleteBehavior.SetNull);
        // A document is removed by stamping it, so these normally outlive nothing; a document or
        // a revision destroyed for good afterwards leaves the line, for the trip pointer's reason.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId)
            .OnDelete(DeleteBehavior.SetNull);
        // A recording or a scan removed for good leaves the line that says the upload sent it.
        builder.HasOne<TripRecording>().WithMany().HasForeignKey(x => x.TripRecordingId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TripRecordingPoint>().WithMany().HasForeignKey(x => x.TripRecordingPointId)
            .OnDelete(DeleteBehavior.SetNull);
        // A raster map or a pin removed for good leaves the line that says the upload sent it.
        builder.HasOne<RasterMap>().WithMany().HasForeignKey(x => x.RasterMapId)
            .OnDelete(DeleteBehavior.SetNull);
        // A pin is the link that pins its place on the map's picture, under the phone's identifier.
        builder.HasOne<ResLink>().WithMany().HasForeignKey(x => x.RasterPinId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ImportBatchId);
        // "Where did this cave come from?" is a lookup by feature, and it is the question the
        // whole table exists to answer.
        builder.HasIndex(x => x.FeatureId).HasFilter("feature_id is not null");
        // "What did this photograph become?" is the same question asked from the other end, and
        // it is what the attachment panel asks about every picture it shows.
        builder.HasIndex(x => x.SourceFileId).HasFilter("source_file_id is not null");
        // "Which import produced this trip?" — the same question the feature index answers, asked
        // of the objects a spreadsheet becomes.
        builder.HasIndex(x => x.TripLogId).HasFilter("trip_log_id is not null");
        // "Which import produced this position?" — and, the way an undo asks it, "which positions
        // did this import produce?", which the batch index above already answers.
        builder.HasIndex(x => x.TripPositionEventId).HasFilter("trip_position_event_id is not null");
        // "Which uploads sent this phone user?" — how the directory finds the links an account has
        // made, and how removing a phone user finds the lines to clear.
        builder.HasIndex(x => x.DevicePersonId).HasFilter("device_person_id is not null")
            .HasDatabaseName("ix_import_batch_items_device_person_id");
        // "Which uploads touched this document?" — and the set-null of the foreign keys above,
        // which without an index would scan every line when a document is destroyed.
        builder.HasIndex(x => x.DocumentId).HasFilter("document_id is not null")
            .HasDatabaseName("ix_import_batch_items_document_id");
        builder.HasIndex(x => x.DocumentVersionId).HasFilter("document_version_id is not null")
            .HasDatabaseName("ix_import_batch_items_document_version_id");

        // "Which uploads sent this recording?" — how an undo and the conversion find what an upload
        // brought, and how a recording's own provenance is read.
        builder.HasIndex(x => x.TripRecordingId).HasFilter("trip_recording_id is not null")
            .HasDatabaseName("ix_import_batch_items_trip_recording_id");
        builder.HasIndex(x => x.TripRecordingPointId).HasFilter("trip_recording_point_id is not null")
            .HasDatabaseName("ix_import_batch_items_trip_recording_point_id");
        // The same question for raster maps and pins, and the set-null of the keys above.
        builder.HasIndex(x => x.RasterMapId).HasFilter("raster_map_id is not null")
            .HasDatabaseName("ix_import_batch_items_raster_map_id");
        builder.HasIndex(x => x.RasterPinId).HasFilter("raster_pin_id is not null")
            .HasDatabaseName("ix_import_batch_items_raster_pin_id");

        builder.Property(x => x.ReviewDecision).HasConversion<short?>();
        builder.Property(x => x.Proposal).HasColumnType("jsonb");
        builder.Property(x => x.ProposalSection).HasMaxLength(32);
        builder.Property(x => x.AcceptedFields).HasColumnType("text[]");
        builder.Property(x => x.DecisionNote).HasMaxLength(2000);
        builder.Property(x => x.ReviewFailureCode).HasMaxLength(64);
        builder.Property(x => x.ReviewFailureDetail).HasMaxLength(2000);
        // A line can depend on several held lines, so the dependencies are a list rather than a
        // key. A line another depends on is never deleted on its own (lines go with their batch),
        // and a dependency on a line that is gone reads as one that cannot be written.
        builder.Property(x => x.DependsOnItemIds).HasColumnType("bigint[]");
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        // The identity a phone is answered with: one line per review id, anywhere.
        builder.HasIndex(x => x.ReviewId).IsUnique().HasFilter("review_id is not null")
            .HasDatabaseName("ux_import_batch_items_review_id");
        // "Is this row already waiting?" — how a resend of a held row is matched to the proposal
        // it repeats or replaces, and how a later row finds the held row it depends on. Only the
        // lines still waiting are ever asked about.
        builder.HasIndex(x => new { x.ProposalRowId, x.ProposalSection })
            .HasFilter("review_decision = 0")
            .HasDatabaseName("ix_import_batch_items_pending_row");
        // "Which waiting lines hang on these?" — asked when a resend replaces a held line.
        builder.HasIndex(x => x.DependsOnItemIds).HasMethod("gin").HasFilter("depends_on_item_ids is not null")
            .HasDatabaseName("ix_import_batch_items_depends_on_item_ids");
        // "Does a waiting line still need these bytes?" — asked before staged bytes are released.
        builder.HasIndex(x => x.HeldUploadSessionId).HasFilter("held_upload_session_id is not null")
            .HasDatabaseName("ix_import_batch_items_held_upload_session_id");
    }
}
