// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class GeoreferencedMapConfiguration : IEntityTypeConfiguration<GeoreferencedMap>
{
    public void Configure(EntityTypeBuilder<GeoreferencedMap> builder)
    {
        builder.ToTable("georeferenced_maps");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.ProcessingError).HasMaxLength(2000);
        builder.Property(x => x.Attribution).HasMaxLength(300);
        builder.Property(x => x.MapKind).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.DefaultOpacity).HasPrecision(3, 2);
        builder.Property(x => x.Bbox).HasColumnType("geometry(Polygon, 4326)");
        builder.Property(x => x.SourceLayers).HasColumnType("jsonb");
        builder.Property(x => x.RenderLayers).HasColumnType("jsonb");
        builder.Property(x => x.RenderDpi).HasPrecision(6, 2);
        builder.Property(x => x.RenderWarnings).HasMaxLength(2000);
        builder.Property(x => x.Placement).HasColumnType("jsonb");
        builder.Property(x => x.EmbeddedPlacement).HasColumnType("jsonb");
        builder.Property(x => x.PlacementSource).HasConversion<short?>();
        builder.Property(x => x.PlacementRmsMetres).HasPrecision(10, 3);

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.MasterFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.PreviewFileId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Bbox).HasMethod("gist");
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.CaveFeatureId);
        builder.HasIndex(x => x.SourceFileId);
        builder.HasIndex(x => x.MasterFileId);
        builder.HasIndex(x => x.PreviewFileId);
    }
}
