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
