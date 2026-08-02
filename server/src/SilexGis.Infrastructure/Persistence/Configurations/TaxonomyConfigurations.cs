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

public sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ConfigureTaxonomy("document_types");
        builder.Property(x => x.MetadataSchema).HasColumnType("jsonb");
        builder.Property(x => x.MetadataSchemaVersion).HasDefaultValue(1);
    }
}

public sealed class DocumentTypeSchemaConfiguration : IEntityTypeConfiguration<DocumentTypeSchema>
{
    public void Configure(EntityTypeBuilder<DocumentTypeSchema> builder)
    {
        builder.ToTable("document_type_schemas",
            t => t.HasCheckConstraint("ck_document_type_schemas_version", "version >= 1"));
        builder.Property(x => x.Schema).HasColumnType("jsonb");

        // The history goes with the type: a version of a schema means nothing without the
        // type it belongs to, and a type with no rows here has never published a schema.
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(x => x.DocumentTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.DocumentTypeId, x.Version }).IsUnique();
    }
}

public sealed class FeatureTypeConfiguration : IEntityTypeConfiguration<FeatureType>
{
    public void Configure(EntityTypeBuilder<FeatureType> builder)
    {
        builder.ConfigureTaxonomy("feature_types");
        builder.Property(x => x.Category).HasConversion<short>();
        // Accepted OGC classes as a smallint[] — kinds opt into Multi* explicitly.
        builder.Property(x => x.AcceptedGeometryClasses)
            .HasConversion(
                v => v.Select(c => (short)c).ToArray(),
                v => v.Select(c => (SilexGis.Domain.GeometryClass)c).ToArray(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<SilexGis.Domain.GeometryClass[]>(
                    (a, b) => (a ?? Array.Empty<SilexGis.Domain.GeometryClass>())
                        .SequenceEqual(b ?? Array.Empty<SilexGis.Domain.GeometryClass>()),
                    v => v.Aggregate(0, (h, c) => HashCode.Combine(h, c)),
                    v => v.ToArray()))
            .HasColumnType("smallint[]");
        builder.Property(x => x.ProtectedDisplay).HasConversion<short>();
        builder.Property(x => x.SymbolFile).HasMaxLength(100);
        builder.Property(x => x.Style).HasColumnType("jsonb");
        builder.Property(x => x.PropertiesSchema).HasColumnType("jsonb");
    }
}
