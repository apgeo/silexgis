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

        // Metres to one decimal, which is finer than any relayed report and coarser than a
        // survey's own arithmetic. Fixed rather than floating because the depth is a key: two
        // rows that differ in the tenth bit of a double are two rows to a database and one depth
        // to everybody else.
        builder.Property(x => x.DepthM).HasPrecision(7, 1);

        // One row per depth per cave, enforced here rather than by whoever writes: the depth IS
        // the key this table is read by, and a cave that answered its own question twice would
        // hand a reported depth two stations with nothing to choose between them.
        builder.HasIndex(x => new { x.CaveFeatureId, x.DepthM })
            .IsUnique()
            .HasDatabaseName("ux_cave_depth_places_cave_depth");
    }
}
