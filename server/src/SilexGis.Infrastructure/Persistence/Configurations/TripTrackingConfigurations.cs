// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TripTeamConfiguration : IEntityTypeConfiguration<TripTeam>
{
    public void Configure(EntityTypeBuilder<TripTeam> builder)
    {
        builder.ToTable("trip_teams");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.TripLogId);
    }
}

public sealed class TripPositionEventConfiguration : IEntityTypeConfiguration<TripPositionEvent>
{
    public void Configure(EntityTypeBuilder<TripPositionEvent> builder)
    {
        builder.ToTable("trip_position_events");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<short>();
        // Viewer-spelling station path; text kept even when the model row later disappears.
        builder.Property(x => x.StationName).HasMaxLength(400);
        builder.Property(x => x.DepthEnteredM).HasPrecision(7, 1);
        builder.Property(x => x.Note).HasMaxLength(2000);

        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Being tracked is a fact about the person; it blocks deleting the person, like the roster.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TripTeam>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TripLogId, x.CaverId, x.RecordedAt });
        builder.HasIndex(x => new { x.TripLogId, x.RecordedAt });
    }
}

public sealed class TripTrackingConfiguration : IEntityTypeConfiguration<TripTracking>
{
    public void Configure(EntityTypeBuilder<TripTracking> builder)
    {
        builder.ToTable("trip_tracking");
        builder.HasKey(x => x.TripLogId);
        builder.Property(x => x.State).HasConversion<short>().HasDefaultValue(TripTrackingState.Off);
        builder.Property(x => x.ReferenceStationName).HasMaxLength(400);
        builder.Property(x => x.DepthFilter).HasColumnType("text[]");
        builder.HasOne<TripLog>().WithOne().HasForeignKey<TripTracking>(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TripTrackingShareConfiguration : IEntityTypeConfiguration<TripTrackingShare>
{
    public void Configure(EntityTypeBuilder<TripTrackingShare> builder)
    {
        builder.ToTable("trip_tracking_shares");
        builder.Property(x => x.Id).ValueGeneratedNever();
        // SHA-256, base64url: 43 characters. This column is the only form the token exists in
        // once the mint response is gone, and the unique index is how the published read
        // resolves one.
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.TripLogId);

        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Who published the trip is part of what the link is, and cannot be removed out from
        // under the record — the same stance every other share link takes.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TripTrackingParticipantConfiguration : IEntityTypeConfiguration<TripTrackingParticipant>
{
    public void Configure(EntityTypeBuilder<TripTrackingParticipant> builder)
    {
        builder.ToTable("trip_tracking_participants");

        builder.Property(x => x.Id).ValueGeneratedNever();
        // One label per person per trip: a second row would make "what are they called here" a
        // question with two answers.
        builder.HasIndex(x => new { x.TripLogId, x.CaverId }).IsUnique();
        builder.Property(x => x.DisplayLabel).HasMaxLength(200);

        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Cascade, where the roster and the position log both restrict, and the difference is
        // what the row holds. Those record that a person was somewhere — a fact worth blocking a
        // delete for. This records how one page captioned them, which means nothing once the
        // person's entry is gone; and a merge moves the caption to the survivor before the
        // duplicate is removed, so the ordinary path does not lose the choice either.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Cascade);
    }
}
