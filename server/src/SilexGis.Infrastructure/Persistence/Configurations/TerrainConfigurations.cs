// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TerrainBuildConfiguration : IEntityTypeConfiguration<TerrainBuild>
{
    public void Configure(EntityTypeBuilder<TerrainBuild> builder)
    {
        // Progress is written by a worker rather than accepted from a request, so the database is
        // the only place that can hold it to its meaning. A percentage that has escaped its range
        // shows up as a progress bar that is off the end of its track, with nothing to say which
        // step got the arithmetic wrong.
        builder.ToTable("terrain_builds", t => t.HasCheckConstraint(
            "ck_terrain_builds_progress_range", "progress >= 0 and progress <= 100"));
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        builder.Property(x => x.Extent).HasColumnType("geometry(Polygon, 4326)");
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Phase).HasConversion<short>();
        builder.Property(x => x.HeightDatum).HasConversion<short>();
        builder.Property(x => x.Message).HasMaxLength(500);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.LogTail).HasMaxLength(8000);
        builder.Property(x => x.PyramidVersion).HasMaxLength(64);

        // Which ground already has terrain over it is a spatial question, and the answer decides
        // whether an area needs building at all.
        builder.HasIndex(x => x.Extent).HasMethod("gist");

        // At most one build is the terrain the scene draws. Held by the database rather than by
        // the handler that sets it: two administrators publishing at the same instant would each
        // read a state in which nothing was active and each write their own row, and the scene
        // would then draw whichever one a later query happened to return first.
        builder.HasIndex(x => x.IsActive).IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ux_terrain_builds_active");

        // The listing: every build, newest first.
        builder.HasIndex(x => x.CreatedAt);

        // The sweep that finds builds still owed work after a restart.
        builder.HasIndex(x => x.Status).HasFilter("status in (0, 1)");
    }
}

public sealed class TerrainBuildSourceConfiguration : IEntityTypeConfiguration<TerrainBuildSource>
{
    public void Configure(EntityTypeBuilder<TerrainBuildSource> builder)
    {
        builder.ToTable("terrain_build_sources");

        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Reference).HasMaxLength(2000);
        builder.Property(x => x.Attribution).HasMaxLength(500);
        builder.Property(x => x.Licence).HasMaxLength(200);

        // A source line describes bytes that only exist because of its build, and the credit it
        // carries is meaningless apart from the pyramid it was baked into.
        builder.HasOne<TerrainBuild>().WithMany().HasForeignKey(x => x.TerrainBuildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TerrainBuildId, x.Id });
    }
}
