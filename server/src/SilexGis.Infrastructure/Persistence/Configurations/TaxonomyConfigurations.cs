// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

internal static class TaxonomyConfiguration
{
    public static void ConfigureTaxonomy<T>(this EntityTypeBuilder<T> builder, string table)
        where T : TaxonomyBase
    {
        builder.ToTable(table);
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class CaveTypeConfiguration : IEntityTypeConfiguration<CaveType>
{
    public void Configure(EntityTypeBuilder<CaveType> builder) => builder.ConfigureTaxonomy("cave_types");
}

public sealed class EntranceTypeConfiguration : IEntityTypeConfiguration<EntranceType>
{
    public void Configure(EntityTypeBuilder<EntranceType> builder) => builder.ConfigureTaxonomy("entrance_types");
}

public sealed class RockTypeConfiguration : IEntityTypeConfiguration<RockType>
{
    public void Configure(EntityTypeBuilder<RockType> builder) => builder.ConfigureTaxonomy("rock_types");
}

public sealed class FeatureTypeConfiguration : IEntityTypeConfiguration<FeatureType>
{
    public void Configure(EntityTypeBuilder<FeatureType> builder)
    {
        builder.ConfigureTaxonomy("feature_types");
        builder.Property(x => x.GeometryKind).HasConversion<short>();
        builder.Property(x => x.SymbolFile).HasMaxLength(100);
        builder.Property(x => x.Style).HasColumnType("jsonb");
        builder.Property(x => x.PropertiesSchema).HasColumnType("jsonb");
    }
}
