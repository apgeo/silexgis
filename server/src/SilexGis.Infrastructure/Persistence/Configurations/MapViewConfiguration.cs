// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class MapViewConfiguration : IEntityTypeConfiguration<MapView>
{
    public void Configure(EntityTypeBuilder<MapView> builder)
    {
        builder.ToTable("map_views");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Config).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Visibility).HasConversion<short>();

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ShareToken).IsUnique().HasFilter("share_token IS NOT NULL");
        builder.HasIndex(x => x.OwnerUserId);
    }
}
