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
        builder.ToTable("object_acl");

        builder.Property(x => x.EntityType).HasConversion<short>();
        builder.Property(x => x.SubjectKind).HasConversion<short>();
        builder.Property(x => x.Permissions).HasConversion<int>();

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.SubjectKind, x.SubjectId }).IsUnique();
        // Grant lookups run per caller: by subject across entities.
        builder.HasIndex(x => new { x.SubjectKind, x.SubjectId });
    }
}
