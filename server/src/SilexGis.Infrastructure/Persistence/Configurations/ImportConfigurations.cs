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

        builder.HasIndex(x => x.ImportBatchId);
        // "Where did this cave come from?" is a lookup by feature, and it is the question the
        // whole table exists to answer.
        builder.HasIndex(x => x.FeatureId).HasFilter("feature_id is not null");
        // "What did this photograph become?" is the same question asked from the other end, and
        // it is what the attachment panel asks about every picture it shows.
        builder.HasIndex(x => x.SourceFileId).HasFilter("source_file_id is not null");
    }
}
