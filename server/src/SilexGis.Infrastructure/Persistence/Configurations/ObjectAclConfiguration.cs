// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ObjectAclConfiguration : IEntityTypeConfiguration<ObjectAcl>
{
    public void Configure(EntityTypeBuilder<ObjectAcl> builder)
    {
        // Two-world target, same XOR convention as attachments. Feature grants gain FK
        // integrity (cascade closes the historical orphaned-grant gap for features).
        builder.ToTable("object_acl", t => t.HasCheckConstraint(
            "ck_object_acl_one_target",
            "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR " +
            "(feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)"));

        builder.Property(x => x.EntityType).HasConversion<short?>();
        builder.Property(x => x.SubjectKind).HasConversion<short>();
        builder.Property(x => x.Permissions).HasConversion<int>();

        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.SetNull);

        // Uniqueness per target world (partial — NULLs never collide in a plain unique index).
        builder.HasIndex(x => new { x.FeatureId, x.SubjectKind, x.SubjectId })
            .IsUnique().HasFilter("feature_id IS NOT NULL");
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.SubjectKind, x.SubjectId })
            .IsUnique().HasFilter("entity_type IS NOT NULL");
        // Grant lookups run per caller: by subject across entities.
        builder.HasIndex(x => new { x.SubjectKind, x.SubjectId });
    }
}
