// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class UploadBatchConfiguration : IEntityTypeConfiguration<UploadBatch>
{
    public void Configure(EntityTypeBuilder<UploadBatch> builder)
    {
        builder.ToTable("upload_batches");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        builder.Property(x => x.Source).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Label).HasMaxLength(200);
        builder.Property(x => x.SourceDescription).HasMaxLength(1000);
        builder.Property(x => x.Error).HasMaxLength(2000);

        // A batch outlives the account that made it: "where did these four hundred scans come
        // from" is a question people ask about content whose uploader has long since left the
        // club, and the answer must not be deleted along with their login.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.StartedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The destination is a convenience for reading the batch back, not a claim on the
        // shelf: deleting a cabinet must not be blocked by an old batch that mentions it, and
        // the batch's own lines still name the shelf each file actually landed on.
        builder.HasOne<Cabinet>().WithMany().HasForeignKey(x => x.CabinetId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.SetNull);

        // The listing: this person's drops, newest first.
        builder.HasIndex(x => new { x.StartedByUserId, x.CreatedAt });

        // The sweep that finishes batches nothing is adding to any more.
        builder.HasIndex(x => x.Status).HasFilter("status in (0, 1)");
    }
}

public sealed class UploadBatchItemConfiguration : IEntityTypeConfiguration<UploadBatchItem>
{
    public void Configure(EntityTypeBuilder<UploadBatchItem> builder)
    {
        builder.ToTable("upload_batch_items");

        builder.Property(x => x.SourcePath).HasMaxLength(2000);
        builder.Property(x => x.Outcome).HasConversion<short>();
        builder.Property(x => x.Reason).HasMaxLength(100);

        builder.HasOne<UploadBatch>().WithMany().HasForeignKey(x => x.UploadBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // No foreign key to the document on purpose. A line of a report is a record of what
        // happened, and it stays readable — "march.pdf, stored" — after the document it names
        // has been deleted. A restricting key would block that deletion and a cascading one
        // would erase the line, and neither is what a report is for.
        builder.HasIndex(x => new { x.UploadBatchId, x.Id });
        builder.HasIndex(x => x.DocumentId).HasFilter("document_id is not null");
    }
}

public sealed class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable("upload_sessions", t => t.HasCheckConstraint(
            "ck_upload_sessions_received_within_declared",
            "received_bytes >= 0 and received_bytes <= declared_size_bytes"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OriginalName).HasMaxLength(500);
        builder.Property(x => x.StoragePath).HasMaxLength(500);
        builder.Property(x => x.RelativePath).HasMaxLength(2000);
        builder.Property(x => x.AttachEntityType).HasConversion<short>();

        // An abandoned session's bytes go with the account that abandoned them.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Cabinet>().WithMany().HasForeignKey(x => x.CabinetId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<UploadBatch>().WithMany().HasForeignKey(x => x.UploadBatchId)
            .OnDelete(DeleteBehavior.SetNull);

        // The sweep that collects sessions nobody came back to, and their partial bytes.
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class DuplicateUploadRecordConfiguration : IEntityTypeConfiguration<DuplicateUploadRecord>
{
    public void Configure(EntityTypeBuilder<DuplicateUploadRecord> builder)
    {
        builder.ToTable("duplicate_upload_records");

        builder.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();

        // Deliberately no foreign keys to either document. This table exists so an
        // administrator can notice that the store is holding copies it could not tell anyone
        // about; a cascade would erase exactly that record the moment somebody deleted one of
        // the two copies, which is the point at which it becomes most worth having.
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.Sha256);
    }
}
