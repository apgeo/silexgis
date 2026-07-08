// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StoragePath).HasMaxLength(300);
        builder.Property(x => x.OriginalName).HasMaxLength(255);
        builder.Property(x => x.MimeType).HasMaxLength(127);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Sha256);
    }
}

public sealed class GeofileConfiguration : IEntityTypeConfiguration<Geofile>
{
    public void Configure(EntityTypeBuilder<Geofile> builder)
    {
        builder.ToTable("geofiles");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.ImportError).HasMaxLength(2000);
        builder.Property(x => x.Format).HasConversion<short>();
        builder.Property(x => x.ImportStatus).HasConversion<short>();
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.Style).HasColumnType("jsonb");
        builder.Property(x => x.Bbox).HasColumnType("geometry(Polygon, 4326)");

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.TeamId);
    }
}

public sealed class GeofileFeatureConfiguration : IEntityTypeConfiguration<GeofileFeature>
{
    public void Configure(EntityTypeBuilder<GeofileFeature> builder)
    {
        builder.ToTable("geofile_features");

        builder.Property(x => x.Geom).HasColumnType("geometry(Geometry, 4326)");
        builder.Property(x => x.Properties).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // Bulk re-import deletes by geofile id; cascade keeps cleanup transactional.
        builder.HasOne<Geofile>().WithMany().HasForeignKey(x => x.GeofileId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.GeofileId);
        builder.HasIndex(x => x.Geom).HasMethod("gist");
    }
}

public sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ToTable("processing_jobs");

        builder.Property(x => x.Kind).HasMaxLength(100);
        builder.Property(x => x.Payload).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Error).HasMaxLength(4000);

        // The worker polls for queued jobs ordered by id.
        builder.HasIndex(x => new { x.Status, x.Id });
    }
}
