// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CavingGroupConfiguration : IEntityTypeConfiguration<CavingGroup>
{
    public void Configure(EntityTypeBuilder<CavingGroup> builder)
    {
        builder.ToTable("caving_groups");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side
        builder.Property(x => x.Type).HasConversion<short>();
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Slug).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Website).HasMaxLength(255);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class CavingGroupMemberConfiguration : IEntityTypeConfiguration<CavingGroupMember>
{
    public void Configure(EntityTypeBuilder<CavingGroupMember> builder)
    {
        builder.ToTable("caving_group_members");
        builder.Property(x => x.Role).HasConversion<short>();
        builder.HasIndex(x => new { x.CavingGroupId, x.UserId }).IsUnique();
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
