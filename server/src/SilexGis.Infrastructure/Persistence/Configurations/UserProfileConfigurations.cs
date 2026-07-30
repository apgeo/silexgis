// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SilexGisUserConfiguration : IEntityTypeConfiguration<SilexGisUser>
{
    public void Configure(EntityTypeBuilder<SilexGisUser> builder)
    {
        builder.Property(x => x.DisplayName).HasMaxLength(100);
        builder.Property(x => x.Bio).HasMaxLength(2000);
        builder.Property(x => x.Locale).HasMaxLength(10);
        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.CavingClub).HasMaxLength(200);
        builder.Property(x => x.AvatarPreset).HasMaxLength(40);
        builder.Property(x => x.PendingEmail).HasMaxLength(256);

        builder.Property(x => x.RealNameVisibility).HasConversion<short>();
        builder.Property(x => x.BioVisibility).HasConversion<short>();
        builder.Property(x => x.EmailVisibility).HasConversion<short>();
        builder.Property(x => x.PhoneVisibility).HasConversion<short>();
        builder.Property(x => x.CavingClubVisibility).HasConversion<short>();
        builder.Property(x => x.AddressVisibility).HasConversion<short>();
        builder.Property(x => x.AddressPointVisibility).HasConversion<short>();

        builder.Property(x => x.NotifyDigest).HasConversion<short>();
        // Explicit defaults for the two columns whose type-default is wrong for existing rows:
        // an empty string is not valid jsonb, and notification email is on unless turned off.
        builder.Property(x => x.NotifyEmailEnabled).HasDefaultValue(true);
        builder.Property(x => x.UiPreferences).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // Used when checking whether a file is somebody's avatar; most rows have none.
        builder.HasIndex(x => x.AvatarFileId).HasFilter("avatar_file_id IS NOT NULL");

        // An avatar is either an uploaded image or a built-in one, never both.
        builder.ToTable("users", t => t.HasCheckConstraint(
            "ck_users_one_avatar_source",
            "avatar_file_id IS NULL OR avatar_preset IS NULL"));
    }
}

public sealed class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        builder.ToTable("user_addresses");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Geom).HasColumnType("geometry(Point, 4326)");

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.Geom).HasMethod("gist");
    }
}

public sealed class UserNotificationPreferenceConfiguration : IEntityTypeConfiguration<UserNotificationPreference>
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> builder)
    {
        builder.ToTable("user_notification_preferences");
        builder.Property(x => x.Category).HasConversion<short>();

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.Category }).IsUnique();
    }
}

public sealed class AccountDataExportConfiguration : IEntityTypeConfiguration<AccountDataExport>
{
    public void Configure(EntityTypeBuilder<AccountDataExport> builder)
    {
        builder.ToTable("account_data_exports");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.StoragePath).HasMaxLength(400);
        // Matches the processing-queue error cap; a longer exception message must not overflow.
        builder.Property(x => x.Error).HasMaxLength(4000);

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
