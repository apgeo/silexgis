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

public sealed class CaverConfiguration : IEntityTypeConfiguration<Caver>
{
    public void Configure(EntityTypeBuilder<Caver> builder)
    {
        builder.ToTable("cavers");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side
        builder.Property(x => x.FullName).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.Phone).HasMaxLength(40);

        // One account at most per person, and severing the account keeps the person: their trips,
        // credits and roster membership outlive the login.
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.FullName);
    }
}

public sealed class CavingGroupMembershipConfiguration : IEntityTypeConfiguration<CavingGroupMembership>
{
    public void Configure(EntityTypeBuilder<CavingGroupMembership> builder)
    {
        builder.ToTable("caving_group_memberships");
        builder.Property(x => x.Role).HasConversion<short>();
        builder.HasIndex(x => new { x.CaverId, x.CavingGroupId }).IsUnique();
        builder.HasIndex(x => x.CavingGroupId);
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
