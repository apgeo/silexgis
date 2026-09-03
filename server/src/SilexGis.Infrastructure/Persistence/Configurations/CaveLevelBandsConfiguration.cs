// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CaveLevelBandsConfiguration : IEntityTypeConfiguration<CaveLevelBands>
{
    /// <summary>
    /// How long a note about a reading may be. Long enough for the paragraph that explains why the
    /// proposal was edited, short enough that this stays a caption on a figure rather than turning
    /// into a second place cave descriptions are written.
    /// </summary>
    public const int NoteMaxLength = 2000;

    public void Configure(EntityTypeBuilder<CaveLevelBands> builder)
    {
        builder.ToTable("cave_level_bands");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        // The reading dies with the cave: levels of a cave that is really gone describe nothing.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        // Who read the cave this way is the point of the record, so deleting that account is
        // refused rather than allowed to rewrite the record into an anonymous one.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.ConfirmedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Bands).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.Note).HasMaxLength(NoteMaxLength);

        // Two indexes over one column need distinct model names or they are collapsed into one,
        // and the database names are pinned so the naming convention does not suffix-dedupe them.
        builder.HasIndex(x => x.CaveFeatureId, "ix_cave_level_bands_cave")
            .HasDatabaseName("ix_cave_level_bands_cave");

        // At most one current reading per cave, held by the database and not by the handler. Two
        // live rows would not be two opinions on the record but an ambiguous one: the panel would
        // show whichever the read happened to find, and superseding would stamp the other.
        builder.HasIndex(x => x.CaveFeatureId, "ux_cave_level_bands_current").IsUnique()
            .HasFilter("superseded_at IS NULL")
            .HasDatabaseName("ux_cave_level_bands_current");
    }
}
