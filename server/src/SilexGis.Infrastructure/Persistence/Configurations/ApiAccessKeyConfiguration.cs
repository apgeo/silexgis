// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ApiAccessKeyConfiguration : IEntityTypeConfiguration<ApiAccessKey>
{
    /// <summary>Longest name a holder may give a key.</summary>
    public const int NameMaxLength = 100;

    public void Configure(EntityTypeBuilder<ApiAccessKey> builder)
    {
        builder.ToTable("api_access_keys");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(NameMaxLength);
        builder.Property(x => x.Prefix).HasMaxLength(AccessKeyText.PrefixLength);
        builder.Property(x => x.Purpose).HasConversion<short>();

        // A key acts as one account and means nothing once that account is gone, so it goes with
        // it rather than staying behind as a row a lookup could still hit.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The lookup every key-authenticated request makes, and the one the settings page makes.
        builder.HasIndex(x => x.SecretHash, "ix_api_access_keys_secret_hash")
            .HasDatabaseName("ix_api_access_keys_secret_hash")
            .IsUnique();
        builder.HasIndex(x => x.UserId, "ix_api_access_keys_user_id")
            .HasDatabaseName("ix_api_access_keys_user_id");
    }
}
