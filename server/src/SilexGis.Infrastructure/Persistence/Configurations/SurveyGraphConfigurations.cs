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

public sealed class SurveyLrudConfiguration : IEntityTypeConfiguration<SurveyLrud>
{
    public void Configure(EntityTypeBuilder<SurveyLrud> builder)
    {
        // Two constraints the database keeps rather than the code remembering them.
        //
        // The first: a wall distance is never negative. Both compiled formats say "this was not
        // measured" with a negative number, so a negative here is that sentinel stored as if it
        // were a measurement — the exact defect that yields negative passage widths and volumes
        // that look like data. Refusing it in the database means no later reader can reintroduce
        // it quietly.
        //
        // The second: a row states at least one distance. The formats emit all four fields
        // whether or not the surveyor filled any of them, so a reading in which nothing was
        // measured is a row carrying no fact, and a table full of them makes "how many stations
        // have dimensions" answer wrongly.
        builder.ToTable("survey_lrud", t =>
        {
            t.HasCheckConstraint(
                "ck_survey_lrud_non_negative",
                "(left_m is null or left_m >= 0) and (right_m is null or right_m >= 0) and "
                    + "(up_m is null or up_m >= 0) and (down_m is null or down_m >= 0)");
            t.HasCheckConstraint(
                "ck_survey_lrud_measured",
                "left_m is not null or right_m is not null or up_m is not null or down_m is not null");
        });

        builder.Property(x => x.StationName).HasMaxLength(400);

        // A small closed vocabulary, so smallint; the enum has no zero member because "the file
        // did not say" is this column being null.
        builder.Property(x => x.Section).HasConversion<short>();

        // Readings belong to the model they were read out of and have no life without it. Deleting
        // the model, or the cave above it, takes them.
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId)
            .OnDelete(DeleteBehavior.Cascade);

        // The originating leg, where the format states one. Written as a navigation rather than a
        // bare column because the leg's id is assigned by the database and both rows are written
        // in one unit of work: this way the reading learns its leg's id from the change tracker
        // instead of the legs having to be saved first.
        //
        // Cascade rather than set-null: a reading whose leg is gone is a reading of nothing, and
        // both rows are written and deleted together in any case.
        builder.HasOne(x => x.Shot).WithMany().HasForeignKey(x => x.ShotId)
            .OnDelete(DeleteBehavior.Cascade);

        // No foreign key to the station rows, for the same reason the legs have none: what
        // identifies a station is its name, which is unique within a model but is not that
        // table's primary key.

        // Both reads at once: every reading of one model, from the leading column alone, and the
        // readings at one named station of it, from both columns. Not unique — two legs meeting
        // at a station each measure its walls, and they are allowed to disagree.
        builder.HasIndex(x => new { x.SurveyModelId, x.StationName });
    }
}
