// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class MapLayerConfiguration : IEntityTypeConfiguration<MapLayer>
{
    public void Configure(EntityTypeBuilder<MapLayer> builder)
    {
        builder.ToTable("map_layers");
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.UrlTemplate).HasMaxLength(500);
        builder.Property(x => x.Attribution).HasMaxLength(500);
        builder.Property(x => x.GroupName).HasMaxLength(100);
        builder.Property(x => x.ApiKeyName).HasMaxLength(100);
        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.LayerKind).HasConversion<short>();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
