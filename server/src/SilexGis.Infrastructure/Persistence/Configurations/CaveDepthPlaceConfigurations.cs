// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CaveDepthPlaceConfiguration : IEntityTypeConfiguration<CaveDepthPlace>
{
    public void Configure(EntityTypeBuilder<CaveDepthPlace> builder)
    {
        builder.ToTable("cave_depth_places");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        // The declaration dies with the cave: a depth that meant a station of a cave that is
        // really gone has nothing left to answer about.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.ViewerStationName)
            .HasMaxLength(TripTrackingRules.MaxStationNameLength)
            .IsRequired();

        builder.Property(x => x.PlaceLabel).HasMaxLength(TripTrackingRules.MaxTitleLength);
        builder.Property(x => x.Note).HasMaxLength(TripTrackingRules.MaxPlaceNoteLength);

        // Not null, and false for every row written before the flag existed: a place nobody said
        // is drawn on the survey is one that is named in lists only, which is all a row could be
        // until then.
        builder.Property(x => x.IsLandmark).IsRequired();

        // Metres to one decimal, which is finer than any relayed report and coarser than a
        // survey's own arithmetic. Fixed rather than floating because the depth is a key: two
        // rows that differ in the tenth bit of a double are two rows to a database and one depth
        // to everybody else. Null for a place named without a depth.
        builder.Property(x => x.DepthM).HasPrecision(7, 1);

        // One row per depth per cave, enforced here rather than by whoever writes: the depth IS
        // the key this table is read by, and a cave that answered its own question twice would
        // hand a reported depth two stations with nothing to choose between them. A row without a
        // depth is not caught by this one — nulls are distinct to a unique index — and is held by
        // the next.
        //
        // Both indexes are declared under a model name of their own as well as a database name:
        // two indexes over overlapping columns declared without one can be folded by the model
        // into a single index, and the migration then drops one of them without saying so.
        builder.HasIndex(x => new { x.CaveFeatureId, x.DepthM }, "ux_cave_depth_places_cave_depth")
            .IsUnique()
            .HasDatabaseName("ux_cave_depth_places_cave_depth");

        // A station named without a depth is named once per cave: two depthless rows for one
        // station would be one place with two names and nothing to choose between them, and the
        // write that keys such a row by its station would find two. Partial, so a station keeps
        // whatever depths it is also declared at.
        builder.HasIndex(x => new { x.CaveFeatureId, x.ViewerStationName }, "ux_cave_depth_places_cave_station_depthless")
            .IsUnique()
            .HasFilter("depth_m IS NULL")
            .HasDatabaseName("ux_cave_depth_places_cave_station_depthless");
    }
}
