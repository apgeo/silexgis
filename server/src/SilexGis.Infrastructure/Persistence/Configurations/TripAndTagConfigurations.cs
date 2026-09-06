// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TripLogConfiguration : IEntityTypeConfiguration<TripLog>
{
    public void Configure(EntityTypeBuilder<TripLog> builder)
    {
        builder.ToTable("trip_logs");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(255);
        builder.Property(x => x.WeatherConditions).HasMaxLength(300);
        builder.Property(x => x.LocationText).HasMaxLength(300);

        // Metres to a decimetre, sized to what the measurement can plausibly be: no cave is a
        // thousand kilometres deep, and a surveyed length runs longer than a depth does. Scale
        // is what matters more than the width — a fixed scale is what lets these be summed and
        // ranked without every reader deciding how much precision to believe.
        builder.Property(x => x.DepthReachedM).HasPrecision(7, 1);
        builder.Property(x => x.LengthSurveyedM).HasPrecision(9, 1);
        builder.Property(x => x.RopeMetres).HasPrecision(7, 1);
        // False rather than null, and defaulted in the database so a row written by anything
        // that does not know about the column still says "nothing went wrong" rather than
        // "unknown" — a count of incidents must never have a third answer.
        builder.Property(x => x.HadIncident).HasDefaultValue(false);

        // The three per-purpose sections. Defaulted to an empty object in the database as well
        // as in the entity, so a row inserted by anything that does not know about the columns
        // still reads as "answered nothing" rather than as a null a reader has to guard.
        builder.Property(x => x.FieldData).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Logistics).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Safety).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.OrganizingCavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.State).HasConversion<short>();
        // Defaulted in the database as well as in the entity so a row written by anything that
        // does not know about the column says "nobody arranged a callout" rather than leaving a
        // null that a pass watching for overdue parties would have to guard.
        builder.Property(x => x.CalloutState).HasConversion<short>().HasDefaultValue(TripCalloutState.None);
        builder.Property(x => x.Geom).HasColumnType("geometry(Geometry, 4326)");
        builder.Property(x => x.MeetingGeom).HasColumnType("geometry(Geometry, 4326)");

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);

        // Restricted rather than set-null: a purpose still in use is a purpose the vocabulary
        // surface refuses to delete, and letting the database quietly unset it instead would
        // retype every trip that held it to "not said".
        builder.HasOne<TripType>().WithMany().HasForeignKey(x => x.TripTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
        // Its own spatial index rather than a shared one: the map layer asks the two columns
        // separately, and a trip whose only position is where its party meets has to be found by
        // the same window query that finds a trip with a sketch.
        builder.HasIndex(x => x.MeetingGeom).HasMethod("gist");
        builder.HasIndex(x => x.TripDate);
        builder.HasIndex(x => x.OwnerUserId);
        // The one selection a scheduled pass makes over this table: parties whose alarm time has
        // gone by and whose check is still live. Leading with the state keeps that pass reading a
        // handful of rows rather than every trip ever recorded, and it is the state that stays
        // small — almost every row is a trip that already happened.
        builder.HasIndex(x => new { x.CalloutState, x.CalloutAlarmAt });
        // The two columns the filter panel groups on. Both are asked as a count per value over
        // every trip the caller may read, which without an index is a scan of the whole table for
        // each of them every time somebody opens a facet. The audience column and the went-wrong
        // flag are grouped the same way and are deliberately left unindexed: they have three and
        // two values, so an index over them selects most of the table and the planner ignores it.
        builder.HasIndex(x => x.TripTypeId);
        builder.HasIndex(x => x.State);
    }
}

public sealed class TripLogParticipantConfiguration : IEntityTypeConfiguration<TripLogParticipant>
{
    public void Configure(EntityTypeBuilder<TripLogParticipant> builder)
    {
        builder.ToTable("trip_log_participants");
        // A sentence about one person's part in the trip, not a second report: bounded here so an
        // over-long note is a plain refusal rather than a database error several layers down.
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Restrict, not cascade: removing someone from the roster must not quietly rewrite the
        // history of the trips they were on. Merging their duplicate entry is the way out.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Restrict);
        // Restricted for the same reason a trip's purpose is: a role still in use is a role the
        // vocabulary surface refuses to delete, and letting the database quietly unset it would
        // turn everybody who held it into somebody whose job on the trip was never recorded.
        builder.HasOne<TripParticipantRole>().WithMany().HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.TripLogId);
        builder.HasIndex(x => x.CaverId);
        builder.HasIndex(x => x.RoleId);
        // One person, one job, one row — so being the leader and the surveyor is two rows and
        // neither displaces the other.
        builder.HasIndex(x => new { x.TripLogId, x.RoleId, x.CaverId }).IsUnique();
    }
}

public sealed class TripInvitationConfiguration : IEntityTypeConfiguration<TripInvitation>
{
    public void Configure(EntityTypeBuilder<TripInvitation> builder)
    {
        // Exactly one subject per row, decided by the database. The alternative — one column
        // naming a kind and one holding an id — would give up both foreign keys and both
        // cascades, so deleting an event would leave its answers behind pointing at nothing and
        // nothing in the schema could say so.
        builder.ToTable("trip_invitations", t => t.HasCheckConstraint(
            "ck_trip_invitations_one_subject",
            "(trip_log_id IS NOT NULL AND event_id IS NULL) OR "
            + "(trip_log_id IS NULL AND event_id IS NOT NULL)"));
        // The remark beside an answer, bounded to the same length a roster remark gets so the
        // two surfaces never disagree about what fits.
        builder.Property(x => x.Note).HasMaxLength(TripInvitationRules.MaxNoteLength);
        // Stored as its number, which is what makes "invited and silent" a value with a column
        // behind it rather than a null standing in for two different facts.
        builder.Property(x => x.Response).HasConversion<short>();
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // The second subject, with its own foreign key and the same cascade: an event that is
        // deleted takes the answers about it with it, exactly as a trip does.
        builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        // Cascade, and deliberately not the Restrict the roster above uses. Being named on a
        // trip is a fact about what happened and must survive the roster being tidied, so that
        // row refuses to go; having once been asked whether you were coming is not, and
        // somebody who only ever declined an invitation must not thereby become undeletable.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Cascade);
        // The people who did the asking and the writing-down are attribution, so a closed
        // account leaves the answer standing and takes only the name off it.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RespondedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.TripLogId);
        builder.HasIndex(x => x.EventId);
        builder.HasIndex(x => x.CaverId);
        // One person, one subject, one standing answer — so changing your mind rewrites the
        // answer you already gave instead of leaving you holding two that disagree. One index per
        // subject rather than one over both, because a unique index treats every row whose
        // subject is null as distinct from every other, so a single index over the pair would
        // hold nobody to anything.
        builder.HasIndex(x => new { x.TripLogId, x.CaverId }).IsUnique().HasFilter("trip_log_id IS NOT NULL");
        builder.HasIndex(x => new { x.EventId, x.CaverId }).IsUnique().HasFilter("event_id IS NOT NULL");
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.Property(x => x.Slug).HasMaxLength(80);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class TaggingConfiguration : IEntityTypeConfiguration<Tagging>
{
    public void Configure(EntityTypeBuilder<Tagging> builder)
    {
        // Two-world target, same XOR convention as attachments.
        builder.ToTable("taggings", t => t.HasCheckConstraint(
            "ck_taggings_one_target",
            "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR " +
            "(feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)"));
        builder.Property(x => x.EntityType).HasConversion<short?>();
        builder.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.TagId, x.FeatureId }).IsUnique().HasFilter("feature_id IS NOT NULL");
        builder.HasIndex(x => new { x.TagId, x.EntityType, x.EntityId }).IsUnique().HasFilter("entity_type IS NOT NULL");
        builder.HasIndex(x => x.FeatureId);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
