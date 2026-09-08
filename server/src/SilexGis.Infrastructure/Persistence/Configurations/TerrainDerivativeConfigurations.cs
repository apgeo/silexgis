// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TerrainDerivativeLayerConfiguration
    : IEntityTypeConfiguration<TerrainDerivativeLayer>
{
    public void Configure(EntityTypeBuilder<TerrainDerivativeLayer> builder)
    {
        // The count of times this picture has been drawn, and it only ever goes up. Held by the
        // database because it is also part of the path the files sit at, so a negative one would be
        // a directory name nothing could ever find again.
        builder.ToTable("terrain_derivative_layers", t => t.HasCheckConstraint(
            "ck_terrain_derivative_layers_version", "version >= 0"));
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        builder.Property(x => x.Derivative).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Settings).HasColumnType("jsonb");
        builder.Property(x => x.SettingsHash).HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.Message).HasMaxLength(500);

        // Deleting a build takes its pictures with it. They are computed from its rasters and mean
        // nothing without them, and the build's whole folder — the pictures included — is swept off
        // disk by the same delete.
        builder.HasOne<TerrainBuild>()
            .WithMany()
            .HasForeignKey(x => x.TerrainBuildId)
            .OnDelete(DeleteBehavior.Cascade);

        // The run that produced what is stored. Kept when the job row is swept away: which run it
        // was is a detail, that the picture exists is not.
        builder.HasOne<ProcessingJob>()
            .WithMany()
            .HasForeignKey(x => x.ProcessingJobId)
            .OnDelete(DeleteBehavior.SetNull);

        // One picture of one build, once. Held by the database rather than by the endpoint that
        // accepts the request: two administrators asking for the same shaded relief at the same
        // instant would each find nothing stored and each queue a job, and the installation would
        // then hold two identical rasters, of which whichever a later query returned first would be
        // the one on screen.
        builder.HasIndex(x => new { x.TerrainBuildId, x.Derivative, x.SettingsHash })
            .IsUnique()
            .HasDatabaseName("ux_terrain_derivative_layers_request");

        // The sweep that finds pictures still owed work after a restart.
        builder.HasIndex(x => x.Status).HasFilter("status in (0, 1)");
    }
}

public sealed class TerrainDerivativeRasterConfiguration
    : IEntityTypeConfiguration<TerrainDerivativeRaster>
{
    public void Configure(EntityTypeBuilder<TerrainDerivativeRaster> builder)
    {
        builder.ToTable("terrain_derivative_rasters");

        builder.Property(x => x.SourcePath).HasMaxLength(1000);
        builder.Property(x => x.Path).HasMaxLength(1000);
        builder.Property(x => x.Footprint).HasColumnType("geometry(Polygon, 4326)");

        builder.HasOne<TerrainDerivativeLayer>()
            .WithMany()
            .HasForeignKey(x => x.TerrainDerivativeLayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Which of a picture's files covers the ground somebody is looking at.
        builder.HasIndex(x => x.Footprint).HasMethod("gist");

        // Reading one picture's files back, in a stable order.
        builder.HasIndex(x => new { x.TerrainDerivativeLayerId, x.Id });

        // A picture holds one file per elevation raster it was computed from. Recomputing replaces
        // them; without this a re-run interrupted between its delete and its insert could leave two
        // rows naming one file, and a listing would report a picture twice the size it is.
        builder.HasIndex(x => new { x.TerrainDerivativeLayerId, x.Path }).IsUnique();
    }
}
