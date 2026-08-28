// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SyncSetConfiguration : IEntityTypeConfiguration<SyncSet>
{
    public void Configure(EntityTypeBuilder<SyncSet> builder)
    {
        // The revision is the device's only way to tell a copy it holds from the current one,
        // so a value that has gone backwards would silently make a stale device look current.
        // Held by the database because the handler that increments it is not the only thing
        // that will ever write this table.
        builder.ToTable("sync_sets", t => t.HasCheckConstraint(
            "ck_sync_sets_revision_positive", "revision >= 1"));
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        // It is also what serialises two writers. The settings page and the caver's own phone
        // hold the same set and each posts the whole of it; carrying the value the writer read
        // into the update's own condition makes the second write fail loudly instead of
        // overwriting the first and leaving one revision standing for two different states.
        builder.Property(x => x.Revision).IsConcurrencyToken();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Settings).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.UploadVisibility).HasConversion<short>();

        // A sync set describes one account's device and means nothing without it.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Losing a group unbinds the set rather than destroying the caver's selection: the
        // caves they chose to carry are still the caves they chose to carry.
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // Every read of this table is "the sets belonging to this account".
        builder.HasIndex(x => x.OwnerUserId);
    }
}

public sealed class SyncSetMemberConfiguration : IEntityTypeConfiguration<SyncSetMember>
{
    public void Configure(EntityTypeBuilder<SyncSetMember> builder)
    {
        builder.ToTable("sync_set_members");

        builder.HasOne<SyncSet>().WithMany().HasForeignKey(x => x.SyncSetId)
            .OnDelete(DeleteBehavior.Cascade);

        // The membership row is a pointer, not content, so it goes when the row it points at
        // is really gone. Note what that does and does not cover: deleting a feature through
        // the application stamps it deleted and leaves it in place, so this cascade does not
        // fire and a membership naming a deleted root survives. Anything reading a set's roots
        // must therefore tolerate a root that resolves to a deleted feature — the cascade is
        // the backstop for a row that leaves the table outright, not the everyday case.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.RootFeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        // One root appears at most once in a set, and naming it twice is a mistake rather
        // than a doubled selection.
        builder.HasIndex(x => new { x.SyncSetId, x.RootFeatureId }).IsUnique()
            .HasDatabaseName("ux_sync_set_members_set_root");
    }
}
