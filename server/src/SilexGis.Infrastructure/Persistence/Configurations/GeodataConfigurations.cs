// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files", t =>
        {
            t.HasCheckConstraint("ck_files_page_count", "page_count is null or page_count >= 0");
            t.HasCheckConstraint("ck_files_duration_seconds", "duration_seconds is null or duration_seconds >= 0");
        });
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StoragePath).HasMaxLength(300);
        builder.Property(x => x.OriginalName).HasMaxLength(255);
        // Same width the readers bound their values to, so the column and the code that fills
        // it cannot disagree about what fits.
        builder.Property(x => x.MimeType).HasMaxLength(FileFormats.MaxMediaTypeLength);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Geom).HasColumnType("geometry(Point, 4326)");
        builder.Property(x => x.PositionSource).HasConversion<short>();

        // Facts read out of the bytes get columns of their own rather than a place in the
        // jsonb bag, because document lists filter and order on them.
        builder.Property(x => x.Author).HasMaxLength(255);
        builder.Property(x => x.Producer).HasMaxLength(255);
        builder.Property(x => x.Codec).HasMaxLength(64);

        builder.Property(x => x.TextExtraction).HasConversion<short>();
        builder.Property(x => x.TextExtractionError).HasMaxLength(1000);
        // The maintenance sweep that finds text still to read asks by state and nothing else —
        // once for the files nothing has read, once for the files it has to check the age of —
        // over a table that grows with every upload the installation ever takes.
        builder.HasIndex(x => x.TextExtraction);

        builder.Property(x => x.Conversion).HasConversion<short>();
        // A converted copy points at the upload it came from, and a version holds at most one
        // copy of any upload — the unique index is what makes a re-delivered conversion job
        // unable to produce a second one. Deleting the upload takes its copy with it: a copy of
        // something that is gone is not a document, it is an orphan nobody can name.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ConvertedFromFileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ConvertedFromFileId)
            .IsUnique()
            .HasFilter("converted_from_file_id is not null");

        // Bytes belong to a document revision; deleting the revision takes them with it.
        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_files_orientation", "orientation_quarter_turns between 0 and 3"));

        builder.HasIndex(x => x.Sha256);
        builder.HasIndex(x => x.DocumentVersionId);
        // The gallery reads the newest pictures first over a table that also holds every survey
        // and report, so the kind is part of the index rather than a filter applied after it.
        builder.HasIndex(x => new { x.Kind, x.CreatedAt });
        // Photo-map endpoint filters by bbox on the EXIF point.
        builder.HasIndex(x => x.Geom).HasMethod("gist");
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
        builder.Property(x => x.SourceOptions).HasColumnType("jsonb");
        builder.Property(x => x.Bbox).HasColumnType("geometry(Polygon, 4326)");

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.CavingGroupId);
    }
}

public sealed class GeofileFeatureConfiguration : IEntityTypeConfiguration<GeofileFeature>
{
    public void Configure(EntityTypeBuilder<GeofileFeature> builder)
    {
        // Untyped geometry: a typmod like geometry(Geometry,4326) enforces 2D, but
        // imported files legitimately mix dimensions (GPX tracks carry elevations as Z).
        // The SRID typmod is replaced by a check constraint; the vector reader is the
        // single place that normalizes everything to 4326.
        builder.ToTable("geofile_features",
            t => t.HasCheckConstraint("ck_geofile_features_geom_srid", "st_srid(geom) = 4326"));

        builder.Property(x => x.Geom).HasColumnType("geometry");
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

        // Two questions asked per kind rather than across the whole table: is a pass of this kind
        // already waiting or running, and when did one of this kind last finish. The second is how
        // a page says when a scheduled check last ran, which is what stops a check that never ran
        // reading as a check that found nothing wrong — so it is asked on a page's own request and
        // must not be a scan of every job ever queued.
        builder.HasIndex(x => new { x.Kind, x.CompletedAt });
    }
}
