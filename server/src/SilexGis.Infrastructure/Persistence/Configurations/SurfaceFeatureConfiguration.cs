// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurfaceFeatureConfiguration : IEntityTypeConfiguration<SurfaceFeature>
{
    public void Configure(EntityTypeBuilder<SurfaceFeature> builder)
    {
        builder.ToTable("surface_features");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Geom).HasColumnType("geometry(Geometry, 4326)");
        builder.Property(x => x.Properties).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Visibility).HasConversion<short>();

        builder.HasOne<FeatureType>().WithMany().HasForeignKey(x => x.FeatureTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
        builder.HasIndex(x => x.FeatureTypeId);
        builder.HasIndex(x => x.CaveId);
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.TeamId);

        // Accent-insensitive full-text search over name + description, mirroring caves.
        // immutable_unaccent already exists (created by the cave search migration).
        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', immutable_unaccent(coalesce(name, '') || ' ' || coalesce(description, '')))",
                stored: true);
        builder.HasIndex("SearchVector").HasMethod("gin");
    }
}
