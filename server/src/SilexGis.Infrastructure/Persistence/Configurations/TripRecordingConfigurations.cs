// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TripRecordingConfiguration : IEntityTypeConfiguration<TripRecording>
{
    public void Configure(EntityTypeBuilder<TripRecording> builder)
    {
        builder.ToTable("trip_recordings");
        // The phone minted it; adopted verbatim, like a feature row's identifier.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(255);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.HasQueryFilter(x => x.DeletedAt == null);

        // A cave is soft-deleted, never removed while anything names it, so a recording keeps its
        // cave. Restrict makes a purge that forgot the recordings fail loudly instead of leaving a
        // recording that names no cave.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);
        // The report made from the recording is named by a bare column, not a key, on purpose. A key
        // would make the recording a row of its trip, and rows of a trip are hidden while the trip
        // is deleted — but the recording is the phone's, other phones carry it, and it outlives
        // any report made from it. Removing a trip for good clears the pointer, so the recording
        // can be made into a report again.

        // "Which recordings were made in these caves?" — the question the trips stream asks of
        // every page.
        builder.HasIndex(x => x.CaveFeatureId).HasDatabaseName("ix_trip_recordings_cave_feature_id");
        builder.HasIndex(x => x.OwnerUserId).HasDatabaseName("ix_trip_recordings_owner_user_id");
        // "Which recordings is this trip made from?" — asked when a trip is removed for good. Not
        // unique: one report per recording is the guarded claim's rule (a single column, set only
        // while empty), but one trip may hold several recordings — two phones of one party, each
        // imported onto the trip the party planned — and the archive import has always allowed it.
        builder.HasIndex(x => x.ConvertedTripLogId)
            .HasFilter("converted_trip_log_id is not null")
            .HasDatabaseName("ix_trip_recordings_converted_trip_log_id");
        builder.HasIndex(x => x.DeletedAt).HasFilter("deleted_at is not null")
            .HasDatabaseName("ix_trip_recordings_deleted_at");
    }
}

public sealed class TripRecordingPointConfiguration : IEntityTypeConfiguration<TripRecordingPoint>
{
    public void Configure(EntityTypeBuilder<TripRecordingPoint> builder)
    {
        builder.ToTable("trip_recording_points");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne(x => x.Recording).WithMany(r => r.Points).HasForeignKey(x => x.RecordingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Hidden while its recording is deleted — and only then. A point's own removal is not
        // filtered here, on purpose: the moment a point went is what tells a phone its recording
        // changed, so the trips stream reads removed points' times beside the live ones. Every
        // read of a recording's live points says so itself.
        builder.HasQueryFilter(x => x.Recording!.DeletedAt == null);

        // A place removed for good leaves the scan, at no place, rather than taking it with it.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.PlaceFeatureId).OnDelete(DeleteBehavior.SetNull);

        // A recording's points in the order they are read and changed: the stream asks for the
        // latest change of each recording's points, and a recording's points are listed by it.
        builder.HasIndex(x => new { x.RecordingId, x.UpdatedAt })
            .HasDatabaseName("ix_trip_recording_points_recording_updated");
        builder.HasIndex(x => x.PlaceFeatureId).HasFilter("place_feature_id is not null")
            .HasDatabaseName("ix_trip_recording_points_place_feature_id");
    }
}

public sealed class TripAttentionConfiguration : IEntityTypeConfiguration<TripAttention>
{
    public void Configure(EntityTypeBuilder<TripAttention> builder)
    {
        builder.ToTable("trip_attentions");
        builder.HasKey(x => x.TripLogId);
        builder.Property(x => x.Reasons).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        // The flag's version. It is raised by upload jobs and cleared by people, at any moment and
        // from different processes; a write made against a reading somebody else has since changed
        // fails instead of landing a new reason on a cleared flag or a clearance over a reason the
        // clearer never saw.
        builder.Property(x => x.UpdatedAt).IsConcurrencyToken();
        // A row of its trip: it goes with the trip when the trip is removed for good, and it is
        // hidden while the trip is deleted, like every other row that only means something
        // beside its trip.
        builder.HasOne(x => x.TripLog).WithOne().HasForeignKey<TripAttention>(x => x.TripLogId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(x => x.TripLog!.DeletedAt == null);
        // "Which trips still need looking at?" — the list the people who look after phone uploads
        // will be shown.
        builder.HasIndex(x => x.RaisedAt).HasFilter("cleared_at is null")
            .HasDatabaseName("ix_trip_attentions_raised_open");
    }
}
