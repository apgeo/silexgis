// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurveyStationConfiguration : IEntityTypeConfiguration<SurveyStation>
{
    public void Configure(EntityTypeBuilder<SurveyStation> builder)
    {
        builder.ToTable("survey_stations");

        builder.Property(x => x.Name).HasMaxLength(400);
        builder.Property(x => x.SurveyName).HasMaxLength(400);

        // Stored as an integer rather than the smallint the other vocabularies use: this one is a
        // bit set with nine members already reaching 256, and a tenth would not fit a smallint for
        // long. Widening a column later is cheaper than the alternative, which is renumbering the
        // bits and changing what every existing row says.
        builder.Property(x => x.Flags).HasConversion<int>();

        // Three dimensions by column type, not by convention. Two stations of a deep cave share a
        // plan position often enough that dropping the altitude has been measured inflating a
        // reduced network to 143% of the length actually surveyed; a station row with no altitude
        // is not a station this application can use, so the database refuses one.
        builder.Property(x => x.Position).HasColumnType("geometry(PointZ, 4326)");

        // Stations belong to the model they were read out of and have no life without it. Deleting
        // the model, or the cave above it, takes them.
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both reads this serves at once: every station of one model (the leading column alone)
        // and one station of one model by name. Unique because the name is the identity — one of
        // the two formats renumbers its station ids on every re-export, so the name is the only
        // thing that stays the same survey station across two exports of the same cave. A file
        // whose names collide is an extraction that failed to qualify them, and it fails loudly
        // here instead of quietly storing two stations as one.
        builder.HasIndex(x => new { x.SurveyModelId, x.Name }).IsUnique();

        // Where the stations of a cave are is a spatial question the statistics ask.
        builder.HasIndex(x => x.Position).HasMethod("gist");
    }
}

public sealed class SurveyShotConfiguration : IEntityTypeConfiguration<SurveyShot>
{
    public void Configure(EntityTypeBuilder<SurveyShot> builder)
    {
        // A leg is exactly one straight shot between two measured points. Legs arrive one per
        // record from both formats and are never merged into a polyline on the way in, because a
        // station carrying splays is an interior vertex of any merged line and a rule that only
        // inspects a line's ends never sees it.
        builder.ToTable("survey_shots", t => t.HasCheckConstraint(
            "ck_survey_shots_two_points", "st_npoints(geom) = 2"));

        builder.Property(x => x.FromStationName).HasMaxLength(400);
        builder.Property(x => x.ToStationName).HasMaxLength(400);
        builder.Property(x => x.SurveyName).HasMaxLength(400);
        builder.Property(x => x.Flags).HasConversion<int>();
        builder.Property(x => x.Geom).HasColumnType("geometry(LineStringZ, 4326)");

        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId)
            .OnDelete(DeleteBehavior.Cascade);

        // No foreign key to the station rows, on purpose. A splay's far end is often a point the
        // file gives no station for, and a constraint that refused those legs would throw away the
        // splay flags these rows exist to carry.

        // Every leg of one model, which is how the whole set is read.
        builder.HasIndex(x => x.SurveyModelId);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
    }
}
