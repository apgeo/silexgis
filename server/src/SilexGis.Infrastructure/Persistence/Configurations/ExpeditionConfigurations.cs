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

public sealed class ExpeditionTripConfiguration : IEntityTypeConfiguration<ExpeditionTrip>
{
    public void Configure(EntityTypeBuilder<ExpeditionTrip> builder)
    {
        builder.ToTable("expedition_trips");

        // "A trip belongs to at most one camp" is this index and nothing else. Stated as a rule
        // in a handler it would hold until the second writer, and the second writer is a bulk
        // path or an import — the row it would leave behind reads as a trip in two camps, and
        // every roll-up over either camp would then count it.
        builder.HasIndex(x => x.TripLogId).IsUnique();
        builder.HasIndex(x => x.ExpeditionId);

        // Both sides cascade the *membership row* and nothing else: deleting a camp releases its
        // trips, which stand alone perfectly well, and deleting a trip takes its place in the
        // camp with it. Neither ever reaches through to the other row.
        builder.HasOne<Expedition>().WithMany().HasForeignKey(x => x.ExpeditionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ExpeditionRosterEntryConfiguration : IEntityTypeConfiguration<ExpeditionRosterEntry>
{
    public void Configure(EntityTypeBuilder<ExpeditionRosterEntry> builder)
    {
        // A stored end means "and they stayed on to", exactly as the camp's own end date does, so
        // it is either absent or strictly after the first day. Held in the database and not only
        // in the write path, because every reader of the interval is written against it: a row
        // whose end equalled its start would make one day read as a range of itself everywhere at
        // once, and no reader would notice.
        builder.ToTable("expedition_roster", t => t.HasCheckConstraint(
            "ck_expedition_roster_dates", "to_date IS NULL OR to_date > from_date"));

        builder.Property(x => x.Note).HasMaxLength(500);

        // The presence record goes with the camp: who was where for a fortnight means nothing once
        // the camp it belonged to is gone.
        builder.HasOne<Expedition>().WithMany().HasForeignKey(x => x.ExpeditionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not cascade, for the reason a trip's people are restricted: removing somebody
        // from the club's directory must not quietly erase the record that they were at a camp for
        // a fortnight. Merging their duplicate entry is the way out, and the delete handler
        // refuses first so the answer is a reason rather than a constraint violation.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restricted for the same reason a trip's role is: a role somebody is still recorded under
        // is a role the vocabulary surface refuses to delete, and letting the database quietly
        // unset it would turn everybody who held it into somebody who was there as nothing.
        builder.HasOne<ExpeditionRosterRole>().WithMany().HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ExpeditionId);
        builder.HasIndex(x => x.CaverId);
        builder.HasIndex(x => x.RoleId);

        // Deliberately no uniqueness over (camp, person, role): a person may leave and come back,
        // so two rows for one person in one role on one camp is an ordinary record of two stays.
        // The trip's own people are unique on (trip, role, person) because a trip is an afternoon
        // and nobody attends one twice; a fortnight is not that, and copying the index would
        // refuse the very thing this table is for.
    }
}
