// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class TripLogConfiguration : IEntityTypeConfiguration<TripLog>
{
    public void Configure(EntityTypeBuilder<TripLog> builder)
    {
        builder.ToTable("trip_logs");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(255);
        builder.Property(x => x.Type).HasConversion<short>();
        builder.Property(x => x.WeatherConditions).HasMaxLength(300);
        builder.Property(x => x.LocationText).HasMaxLength(300);
        builder.Property(x => x.OrganizingClub).HasMaxLength(200);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.Geom).HasColumnType("geometry(Geometry, 4326)");

        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
        builder.HasIndex(x => x.TripDate);
        builder.HasIndex(x => x.OwnerUserId);
    }
}

public sealed class TripLogCaveConfiguration : IEntityTypeConfiguration<TripLogCave>
{
    public void Configure(EntityTypeBuilder<TripLogCave> builder)
    {
        builder.ToTable("trip_log_caves");
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TripLogId, x.CaveId }).IsUnique();
        builder.HasIndex(x => x.CaveId);
    }
}

public sealed class TripLogParticipantConfiguration : IEntityTypeConfiguration<TripLogParticipant>
{
    public void Configure(EntityTypeBuilder<TripLogParticipant> builder)
    {
        builder.ToTable("trip_log_participants");
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.NameText).HasMaxLength(200);
        builder.HasOne<TripLog>().WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.TripLogId);
        // Exactly one of user_id / name_text per row.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_trip_log_participants_one_identity",
            "(user_id IS NULL) <> (name_text IS NULL)"));
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
        builder.ToTable("taggings");
        builder.Property(x => x.EntityType).HasConversion<short>();
        builder.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.TagId, x.EntityType, x.EntityId }).IsUnique();
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
