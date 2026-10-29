// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class RasterMapConfiguration : IEntityTypeConfiguration<RasterMap>
{
    public void Configure(EntityTypeBuilder<RasterMap> builder)
    {
        builder.ToTable("raster_maps");
        // The phone minted it; adopted verbatim, like a feature row's identifier.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(255);
        builder.Property(x => x.MapType).HasConversion<short>();
        builder.HasQueryFilter(x => x.DeletedAt == null);

        // A cave is soft-deleted, never removed while anything names it; Restrict makes a purge that
        // forgot the maps fail loudly instead of leaving a map that names no cave.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Restrict);
        // The area is only a hint at which part of the cave the picture covers: losing it leaves
        // the map of the whole cave.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveAreaFeatureId).OnDelete(DeleteBehavior.SetNull);
        // A map is its picture; a document destroyed for good (the trash purge) takes the map with it
        // rather than blocking the purge for as long as a map names it. Removing a map by the sync
        // stamps both, so this fires only for a picture removed on its own and left long enough.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        // "Which maps are of these caves?" — the question the rasters stream asks of every page.
        builder.HasIndex(x => x.CaveFeatureId).HasDatabaseName("ix_raster_maps_cave_feature_id");
        // One map per picture: a place pinned on a map is a link naming the map's picture, and that
        // is how the pin's map is found again.
        builder.HasIndex(x => x.DocumentId).IsUnique().HasDatabaseName("ux_raster_maps_document_id");
        builder.HasIndex(x => x.CaveAreaFeatureId).HasFilter("cave_area_feature_id is not null")
            .HasDatabaseName("ix_raster_maps_cave_area_feature_id");
        builder.HasIndex(x => x.OwnerUserId).HasDatabaseName("ix_raster_maps_owner_user_id");
        builder.HasIndex(x => x.DeletedAt).HasFilter("deleted_at is not null")
            .HasDatabaseName("ix_raster_maps_deleted_at");
    }
}
