// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ChecklistConfiguration : IEntityTypeConfiguration<Checklist>
{
    public void Configure(EntityTypeBuilder<Checklist> builder)
    {
        builder.ToTable("checklists");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Visibility).HasConversion<short>();

        // The owner is restricted and the club is set-null, the same asymmetry every owned row
        // carries: an account with content behind it is not deleted out from under it, while a
        // club that dissolves leaves its rows standing without one. It matters more here than
        // usual, because a list published for the whole installation is an ordinary row owned
        // by the account that published it — cascading would delete the installation's shared
        // lists along with an administrator who left.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId);
    }
}

public sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("checklist_items");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Text).HasMaxLength(500);

        // A line has no existence apart from the list it is on, so it goes with it.
        builder.HasOne<Checklist>().WithMany().HasForeignKey(x => x.ChecklistId)
            .OnDelete(DeleteBehavior.Cascade);

        // Read in one order, always: the order its author put it in, with the row key
        // settling ties so two lines given the same place still come back the same way twice.
        builder.HasIndex(x => new { x.ChecklistId, x.SortOrder, x.Id });

        // The list and the line together are a key of their own, so that a confirmation naming
        // both can be referenced against it. Without it the list recorded on a confirmation would
        // be a copy the database has no opinion about, free to name one list while the line it
        // names belongs to another.
        builder.HasAlternateKey(x => new { x.ChecklistId, x.Id });
    }
}

public sealed class TripChecklistTickConfiguration : IEntityTypeConfiguration<TripChecklistTick>
{
    public void Configure(EntityTypeBuilder<TripChecklistTick> builder)
    {
        builder.ToTable("trip_checklist_ticks");

        // One confirmation per line per trip: confirming the same line twice is the same fact
        // said twice, and two rows for it would make "is this settled" a question with two
        // answers and a count that says three of two.
        builder.HasKey(x => new { x.TripLogId, x.ItemId });

        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId)
            .OnDelete(DeleteBehavior.Cascade);

        // Referenced by list and line together against the key those two make on the line, so the
        // list recorded here cannot disagree with the list the line is actually on. A line taken
        // off a list takes its confirmations with it — a confirmation of something no longer on
        // the list is not a fact about anything.
        builder.HasOne<ChecklistItem>().WithMany()
            .HasForeignKey(x => new { x.ChecklistId, x.ItemId })
            .HasPrincipalKey(x => new { x.ChecklistId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Who confirmed it is nulled when the account goes, never cascaded: the confirmation is a
        // fact about the trip's preparation and outlives whoever recorded it.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.TickedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // How settled one trip is, asked of a page of trips at once.
        builder.HasIndex(x => new { x.TripLogId, x.ChecklistId });
    }
}
