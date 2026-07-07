// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CaveConfiguration : IEntityTypeConfiguration<Cave>
{
    public void Configure(EntityTypeBuilder<Cave> builder)
    {
        builder.ToTable("caves");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.OtherToponyms).HasMaxLength(250);
        builder.Property(x => x.IdentificationCode).HasMaxLength(50);
        builder.Property(x => x.Website).HasMaxLength(255);
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.Property(x => x.HydrographicBasin).HasMaxLength(100);
        builder.Property(x => x.Valley).HasMaxLength(100);
        builder.Property(x => x.TributaryRiver).HasMaxLength(100);
        builder.Property(x => x.ClosestAddress).HasMaxLength(200);
        builder.Property(x => x.LandRegistryNumber).HasMaxLength(50);
        builder.Property(x => x.RockAge).HasMaxLength(50);
        builder.Property(x => x.ProtectionClass).HasMaxLength(50);
        builder.Property(x => x.DiscoveryDate).HasMaxLength(50);
        builder.Property(x => x.Discoverer).HasMaxLength(255);

        // Meters with cm precision.
        foreach (var metric in new[]
        {
            nameof(Cave.SurveyedLength), nameof(Cave.EstimatedLength), nameof(Cave.RealExtension),
            nameof(Cave.ProjectedExtension), nameof(Cave.Depth), nameof(Cave.PositiveDepth),
            nameof(Cave.NegativeDepth), nameof(Cave.PotentialDepth), nameof(Cave.Altitude),
            nameof(Cave.Volume), nameof(Cave.Area), nameof(Cave.ShowCaveLength),
        })
        {
            builder.Property(metric).HasPrecision(10, 2);
        }

        builder.Property(x => x.RamificationIndex).HasPrecision(6, 3);
        builder.Property(x => x.ExplorationStatus).HasConversion<short>();
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.MainGeom).HasColumnType("geometry(Point, 4326)");

        builder.HasOne<CaveType>().WithMany().HasForeignKey(x => x.CaveTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RockType>().WithMany().HasForeignKey(x => x.RockTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.MainGeom).HasMethod("gist");
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.Region);
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.TeamId);
        builder.HasIndex(x => x.DeletedAt).HasFilter("deleted_at IS NULL");

        // Accent-insensitive full-text search. Shadow property so Domain stays free of
        // provider types; queries use EF.Property<NpgsqlTsVector>. immutable_unaccent is
        // created in the AddCaveSearchVector migration (generated columns require
        // IMMUTABLE expressions; the two-arg unaccent form qualifies).
        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', immutable_unaccent(coalesce(name, '') || ' ' || " +
                "coalesce(other_toponyms, '') || ' ' || coalesce(description, '')))",
                stored: true);
        builder.HasIndex("SearchVector").HasMethod("gin");

        // Soft delete.
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class CaveEntranceConfiguration : IEntityTypeConfiguration<CaveEntrance>
{
    public void Configure(EntityTypeBuilder<CaveEntrance> builder)
    {
        builder.ToTable("cave_entrances");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Altitude).HasPrecision(10, 2);
        builder.Property(x => x.PositionQuality).HasConversion<short>();
        builder.Property(x => x.Geom).HasColumnType("geometry(Point, 4326)");

        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<EntranceType>().WithMany().HasForeignKey(x => x.EntranceTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
        builder.HasIndex(x => x.CaveId);
    }
}

public sealed class MapLayerConfiguration : IEntityTypeConfiguration<MapLayer>
{
    public void Configure(EntityTypeBuilder<MapLayer> builder)
    {
        builder.ToTable("map_layers");
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.UrlTemplate).HasMaxLength(500);
        builder.Property(x => x.Attribution).HasMaxLength(500);
        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.LayerKind).HasConversion<short>();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
