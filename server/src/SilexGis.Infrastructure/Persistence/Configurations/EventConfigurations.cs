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
        builder.ToTable("events", t =>
        {
            t.HasCheckConstraint("ck_events_dates", "end_date IS NULL OR end_date > start_date");

            // Words describing how something repeats, on a row that is part of no series, would
            // be a sentence about a repetition that is not happening — and the surface that drew
            // it would say so to every reader. The two columns are one fact and are written
            // together or not at all.
            t.HasCheckConstraint(
                "ck_events_series_rule", "series_rule IS NULL OR series_id IS NOT NULL");
        });

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Place).HasMaxLength(255);
        builder.Property(x => x.SeriesRule).HasMaxLength(200);

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

        // Every question anybody asks about a series is "the occurrences of this one, in the order
        // they happen" or "the ones from this day on", so the two columns are indexed together and
        // in that order. Filtered, because the great majority of events belong to no series and an
        // index over their nulls would be most of the table saying nothing.
        builder.HasIndex(x => new { x.SeriesId, x.StartDate }).HasFilter("series_id IS NOT NULL");
    }
}
