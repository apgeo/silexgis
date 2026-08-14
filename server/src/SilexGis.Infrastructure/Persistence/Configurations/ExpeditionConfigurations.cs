// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ExpeditionConfiguration : IEntityTypeConfiguration<Expedition>
{
    public void Configure(EntityTypeBuilder<Expedition> builder)
    {
        // A stored end date means "and it ran on to", so it is either absent or strictly after
        // the start. Held here and not only in the write path's validation, because every reader
        // is written against it: a row with an end equal to its start would make a single-day
        // camp read as a range of itself everywhere at once, and no reader would notice.
        builder.ToTable("expeditions", t => t.HasCheckConstraint(
            "ck_expeditions_dates", "end_date IS NULL OR end_date > start_date"));

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.State).HasConversion<short>();

        // The column's own type carries the SRID, so a geometry in another reference system is
        // refused by the database without a constraint of its own.
        builder.Property(x => x.Geom).HasColumnType("geometry(Geometry, 4326)");

        // The owner is restricted and the club is set-null, the same asymmetry every owned row
        // carries: an account with content behind it is not deleted out from under it, while a
        // club that dissolves leaves its camps standing without an owner.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
        builder.HasIndex(x => x.StartDate);
        builder.HasIndex(x => x.OwnerUserId);
    }
}
