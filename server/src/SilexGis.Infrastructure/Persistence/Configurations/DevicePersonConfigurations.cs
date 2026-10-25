// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class DevicePersonConfiguration : IEntityTypeConfiguration<DevicePerson>
{
    public void Configure(EntityTypeBuilder<DevicePerson> builder)
    {
        builder.ToTable("device_persons");
        builder.HasKey(x => x.DeviceUserId);
        // The phone minted it; adopted verbatim, like a feature row's identifier.
        builder.Property(x => x.DeviceUserId).ValueGeneratedNever();
        builder.Property(x => x.Username).HasMaxLength(200);
        builder.Property(x => x.FirstName).HasMaxLength(200);
        builder.Property(x => x.LastName).HasMaxLength(200);
        builder.Property(x => x.LinkSource).HasConversion<short>();

        // A caver deleted from the roster, or merged into another (the merge moves the links
        // first), leaves the phone user unlinked rather than taking them with it: the phone still
        // has that user, and somebody has to say who they are again.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);

        // "Which phone users are this caver?" and "which phone users has this account sent?"
        builder.HasIndex(x => x.CaverId).HasFilter("caver_id is not null")
            .HasDatabaseName("ix_device_persons_caver_id");
        builder.HasIndex(x => x.UploadedByUserId).HasFilter("uploaded_by_user_id is not null")
            .HasDatabaseName("ix_device_persons_uploaded_by_user_id");
    }
}
