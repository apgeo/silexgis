// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        // A stored end date means "and it ran on to", so it is either absent or strictly after
        // the start. Held here and not only in the write path's validation, because every reader
        // is written against it: a row with an end equal to its start would make a single-day
        // event read as a range of itself everywhere at once, and no reader would notice.
        builder.ToTable("events", t => t.HasCheckConstraint(
            "ck_events_dates", "end_date IS NULL OR end_date > start_date"));

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Place).HasMaxLength(255);

        // Kind, audience and lifecycle state are all closed vocabularies whose numbers are part
        // of the schema contract, so they are stored as the smallints they are declared to be
        // rather than as their names: a value renamed in the code must not silently stop matching
        // the rows already written under it.
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.State).HasConversion<short>();

        // The owner is restricted and the club is set-null, the same asymmetry every owned row
        // carries: an account with content behind it is not deleted out from under it, while a
        // club that dissolves leaves its events standing without one.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // Every read of this table is a window over days, so the first day is the column the
        // window is narrowed on.
        builder.HasIndex(x => x.StartDate);
        builder.HasIndex(x => x.OwnerUserId);
    }
}
