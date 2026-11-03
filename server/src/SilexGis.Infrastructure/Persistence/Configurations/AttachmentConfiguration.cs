// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        // Two-world target: EITHER a feature (real FK, cascade cleanup) OR a non-feature
        // entity via the FK-less polymorphic pair — never both, never neither.
        builder.ToTable("attachments", t => t.HasCheckConstraint(
            "ck_attachments_one_target",
            "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR " +
            "(feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EntityType).HasConversion<short?>();
        builder.Property(x => x.Role).HasConversion<short>();
        builder.Property(x => x.Caption).HasMaxLength(500);

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.SetNull);

        // Every index here carries a model name: a second unnamed HasIndex over the same properties
        // would not add the partial unique index below but turn this plain lookup into it, leaving
        // a listing of an object's attachments, and the cascade when a feature is purged, to read
        // the whole table.
        builder.HasIndex(x => x.FeatureId, "ix_attachments_feature_id")
            .HasDatabaseName("ix_attachments_feature_id");
        // The polymorphic target has no FK by design; lookups are by (type, id).
        builder.HasIndex(x => new { x.EntityType, x.EntityId }, "ix_attachments_entity_type_entity_id")
            .HasDatabaseName("ix_attachments_entity_type_entity_id");
        builder.HasIndex(x => x.FileId);

        // One headline picture per object, enforced rather than agreed. Two of them is a state
        // nothing downstream can resolve, and it would resolve itself differently on every
        // query. Two indexes because the target is a feature or the polymorphic pair, and a
        // single index over both would let one of each coexist.
        builder.HasIndex(x => x.FeatureId, "ux_attachments_primary_feature").IsUnique()
            .HasFilter("is_primary and feature_id is not null")
            .HasDatabaseName("ux_attachments_primary_feature");
        builder.HasIndex(x => new { x.EntityType, x.EntityId }, "ux_attachments_primary_entity").IsUnique()
            .HasFilter("is_primary and entity_id is not null")
            .HasDatabaseName("ux_attachments_primary_entity");
    }
}
