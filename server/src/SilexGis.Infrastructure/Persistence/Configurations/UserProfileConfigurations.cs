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
        // The same cap the write path validates against; a zone name is a short identifier.
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        // The club someone declares on their profile, kept apart from the club rosters:
        // this is their own statement about themselves, a membership row is the group's.
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingClubId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(x => x.AvatarPreset).HasMaxLength(40);
        builder.Property(x => x.PendingEmail).HasMaxLength(256);
        builder.Property(x => x.PendingPhoneNumber).HasMaxLength(32);
        builder.Property(x => x.PreferredTwoFactorMethod).HasConversion<short?>();

        builder.Property(x => x.RealNameVisibility).HasConversion<short>();
        builder.Property(x => x.BioVisibility).HasConversion<short>();
        builder.Property(x => x.EmailVisibility).HasConversion<short>();
        builder.Property(x => x.PhoneVisibility).HasConversion<short>();
        builder.Property(x => x.CavingClubVisibility).HasConversion<short>();
        builder.Property(x => x.AddressVisibility).HasConversion<short>();
        builder.Property(x => x.AddressPointVisibility).HasConversion<short>();

        // An empty string is not valid jsonb, so this column's type-default is wrong for a row
        // that does not name it.
        builder.Property(x => x.UiPreferences).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // Used when checking whether a file is somebody's avatar; most rows have none.
        builder.HasIndex(x => x.AvatarFileId).HasFilter("avatar_file_id IS NOT NULL");

        // One number reaches exactly one account. Nothing needed this while the number was only
        // ever an outbound destination, but a number that two accounts share cannot be resolved
        // back to a person — and the number is a sign-in credential. Partial: most accounts have
        // none, and every one of those would otherwise collide with every other.
        builder.HasIndex(x => x.PhoneNumber).IsUnique().HasFilter("phone_number IS NOT NULL");

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
        // One channel per row, never a set, however permissive the flags type is: a row holding
        // two bits would be one choice pretending to be two and nothing downstream could tell.
        builder.ToTable(
            "user_notification_preferences",
            t => t.HasCheckConstraint(
                "ck_user_notification_preferences_one_channel",
                "channel > 0 AND (channel & (channel - 1)) = 0"));

        builder.Property(x => x.Category).HasConversion<short>();
        builder.Property(x => x.Channel).HasConversion<short>();
        builder.Property(x => x.Choice).HasConversion<short>();

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // The whole of what makes the matrix a matrix: one answer per category per channel.
        builder.HasIndex(x => new { x.UserId, x.Category, x.Channel }).IsUnique();
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

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        // Kind and id are one reference, so they are present together or absent together. A kind
        // without an id names nothing that can be looked up; an id without a kind is worse, because
        // "about nothing openable" is the one state a reader's access is never re-decided against
        // — a row in it would print the name its producer froze for ever. The invariant is stated
        // on the entity, so it is enforced where it is stated rather than trusted to every caller.
        builder.ToTable("notifications", t => t.HasCheckConstraint(
            "ck_notifications_target_pair", "(target_kind IS NULL) = (target_id IS NULL)"));

        builder.Property(x => x.Category).HasConversion<short>();
        builder.Property(x => x.TargetKind).HasConversion<short>();
        builder.Property(x => x.TemplateKey).HasMaxLength(64);
        builder.Property(x => x.Placeholders).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Retention: the one recurring maintenance query, which takes whole notifications past the
        // window. Without this it is a sequential scan of the table on every pass, to delete —
        // usually — nothing.
        builder.HasIndex(x => x.CreatedAt);

        // The listing: one person's own notifications, newest first. The identity column is the
        // ordering, so this index answers the page without a sort.
        builder.HasIndex(x => new { x.RecipientUserId, x.Id })
            .IsDescending(false, true);

        // The unread count, which is asked for far more often than the list itself and is asked
        // about one person. Partial, over the only rows it can ever count, so it stays roughly the
        // size of what is actually unread rather than of the whole table.
        builder.HasIndex(x => x.RecipientUserId)
            .HasFilter("read_at IS NULL")
            .HasDatabaseName("ix_notifications_unread");

        // The routing claim, which takes the oldest rows nothing has decided channels for yet.
        builder.HasIndex(x => x.Id)
            .HasFilter("routed_at IS NULL")
            .HasDatabaseName("ix_notifications_unrouted");
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");

        builder.Property(x => x.Channel).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        // Shorter than the processing queue's cap because this one is written inside the failure
        // path itself: a message too long to store would fail the save that records the failure.
        builder.Property(x => x.Error).HasMaxLength(1000);

        builder.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // A notification leaves on a given channel once. Retries are attempts on this row, never
        // a second one, so the unique index is also what stops a re-run of routing duplicating a
        // message somebody already received.
        builder.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();

        // The claim, moved down from the fused table intact: predicate and index together.
        // Serves both — the immediate drain and the due-summary gather.
        builder.HasIndex(x => new { x.Status, x.NotBefore, x.Id });

        // The summary claim narrows to one recipient, which is what the copied recipient id is
        // for. There is deliberately no index on the recipient alone: what a person's deliveries
        // were is read through their notifications, and nothing queries this table by recipient
        // without also naming a status.
        builder.HasIndex(x => new { x.RecipientUserId, x.Status });

        // How long the oldest unsent delivery has been waiting is the one number that says the
        // mail server has stopped answering, and the operator's health page asks for it every time
        // it is opened. Partial, so the index holds only what is still waiting: a healthy
        // installation keeps almost nothing here however many messages it has ever sent, and the
        // answer is the first entry rather than a scan of the whole table.
        builder.HasIndex(x => x.CreatedAt)
            .HasFilter("status = 0")
            .HasDatabaseName("ix_notification_deliveries_pending_created_at");
    }
}
