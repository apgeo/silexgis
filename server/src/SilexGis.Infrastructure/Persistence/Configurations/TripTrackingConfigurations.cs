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
        builder.HasOne(x => x.TripLog).WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.TripLogId);
        // Hidden while its trip is deleted, like everything else a trip's tracking holds.
        builder.HasQueryFilter(x => x.TripLog.DeletedAt == null);
    }
}

public sealed class TripPositionEventConfiguration : IEntityTypeConfiguration<TripPositionEvent>
{
    public void Configure(EntityTypeBuilder<TripPositionEvent> builder)
    {
        // The far end of a stretch stands only on a station report that names its first station,
        // and is never that same station. Said by the table as well as by the routes because a
        // report's place is rewritten from several sides — a correction, a sheet, an import — and
        // one of them forgetting the second station would otherwise leave it standing beside a
        // place it was never recorded with.
        builder.ToTable("trip_position_events", table =>
        {
            table.HasCheckConstraint(
                StretchCheck,
                "viewer_to_station_name IS NULL OR (kind = 1 AND viewer_station_name IS NOT NULL "
                    + "AND viewer_to_station_name <> viewer_station_name)");
            // A note about the cave is the one row with no person, and no other row may lack one:
            // everything that folds the log into people leaves out the rows without a person, so a
            // person's report that lost its person would vanish from the party, and a note about
            // the cave that gained one would move that person to wherever the hazard is. Such a
            // note also always has words — it says nothing else — and neither a depth nor a team,
            // which are facts about somebody. Each half is written so that a missing value fails
            // the rule rather than passing it as unknown.
            table.HasCheckConstraint(
                CaveNoteCheck,
                "(kind = 5) = (caver_id IS NULL) AND (kind <> 5 OR (note IS NOT NULL AND btrim(note) <> '' "
                    + "AND depth_entered_m IS NULL AND team_id IS NULL))");
        });
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<short>();
        // Provenance, stored beside the row and deliberately outside every read-side decision:
        // nothing about who may learn a position depends on which path wrote it.
        builder.Property(x => x.Source).HasConversion<short>().HasDefaultValue(TripPositionEventSource.Reported);
        // The viewer's own spelling of the station path; text kept even when the model row later
        // disappears.
        builder.Property(x => x.ViewerStationName).HasMaxLength(400);
        builder.Property(x => x.ViewerToStationName).HasMaxLength(400);
        builder.Property(x => x.DepthEnteredM).HasPrecision(7, 1);
        builder.Property(x => x.Note).HasMaxLength(2000);
        // Short by intent: a few words a list can show in a column. A club writing a paragraph
        // here is writing a note, and the column length is what says so.
        builder.Property(x => x.Activity).HasMaxLength(200);

        builder.HasOne(x => x.TripLog).WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Where a party was is read from the model's side and from a person's side as well as
        // from the trip's, so the reports of a deleted trip are hidden here rather than by every
        // reader remembering to ask the trip first. A report taken off the log is hidden by the
        // same filter, for the same reason: it is in no fold, no list and no published read
        // because the model leaves it out, not because each of them was told to. One filter and
        // not two, so a reader who asks past it has asked past both and must say which rows it
        // then wants — there is no way to see removed reports that silently also keeps a deleted
        // trip's rows out.
        builder.HasQueryFilter(x => x.TripLog.DeletedAt == null && x.RemovedAt == null);
        // Being tracked is a fact about the person; it blocks deleting the person, like the roster.
        // Optional only for a note about the cave, which is about nobody (the check above).
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TripTeam>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
        // SurveyModelId is deliberately a bare column with no foreign key. Under one the delete of
        // a model blanked it, and a station name beside no model is already this slice's spelling
        // of "kept from this reader" — so a report made in an older survey came back wearing the
        // one shape that makes a reader draw it on the current one. The column is a record of what
        // the report was measured against, not a live pointer; it may name a row that is gone, and
        // saying that plainly is the whole point. Protection continues to hang off CaveFeatureId.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.SetNull);
        // Who took a report off the log is a courtesy on the row; the account going must not take
        // the report, nor block the account's removal.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RemovedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.TripLogId, x.CaverId, x.RecordedAt });
        builder.HasIndex(x => new { x.TripLogId, x.RecordedAt });
        // Which trips reported against a survey model is asked from the model's side (which trips
        // a movie of that model can show); without this it reads every report of every trip.
        builder.HasIndex(x => x.SurveyModelId);

        // The key of the act of reporting that wrote the row. A column of the model with no member
        // on the class, so that nothing which reads a report can hand it out and the snapshot the
        // history takes of a new row never holds it.
        builder.Property<Guid?>(TripPositionEvent.ClientKeyProperty);
        // What makes a second send of one act write nothing: the same act cannot hold two rows
        // about the same person on the same trip. One act writes a row per person, so the key
        // alone is not unique. Rows no keyed send wrote are outside the index altogether, which
        // is what leaves two typed reports about one person at one instant possible — they are
        // two acts. A report taken off the log stays inside it: a late re-send must not write
        // again what somebody removed on purpose. Named, in the model and in the database,
        // because the write that loses a race to its own duplicate recognises this constraint by
        // its name.
        //
        // "No person" is a value here, not an unknown. A unique index ordinarily lets any number
        // of rows share a key when one of its columns is empty, which would let a note about the
        // cave — the one row with no person — be written again by every repeat of its send. Said
        // not to be distinct, two such rows under one key on one trip collide exactly as two
        // rows about one person do, under the same name, and the same recovery answers both.
        builder.HasIndex(
                [nameof(TripPositionEvent.TripLogId), TripPositionEvent.ClientKeyProperty, nameof(TripPositionEvent.CaverId)],
                ClientKeyIndex)
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("client_key IS NOT NULL")
            .HasDatabaseName(ClientKeyIndex);
    }

    /// <summary>
    /// The database's name for the constraint that keeps one act of reporting from being written
    /// twice — what a write that lost a race to its own duplicate is told it violated.
    /// </summary>
    public const string ClientKeyIndex = "ux_trip_position_events_client_key";

    /// <summary>The database's name for the rule on where the far end of a stretch may stand.</summary>
    public const string StretchCheck = "ck_trip_position_events_stretch";

    /// <summary>
    /// The database's name for the rule that ties "about nobody" to a note about the cave and says
    /// what such a note holds.
    /// </summary>
    public const string CaveNoteCheck = "ck_trip_position_events_cave_note";
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
        builder.HasOne(x => x.TripLog).WithOne().HasForeignKey<TripTracking>(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // A deleted trip has no watch: it is followed by nobody, counted among no cave's parties,
        // and guards no survey against being removed.
        builder.HasQueryFilter(x => x.TripLog.DeletedAt == null);
        // Bare column, no foreign key, for the reason the event rows carry one: a watch whose model
        // was deleted must read as a watch whose model was deleted, and a self-blanking reference
        // turns it into a watch that reads as never having had one — the same null a withheld
        // configuration sends. Keeping the id is what lets the read say which of the two it is.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.SetNull);
        // Which watches point at a survey model is asked from the model's side as well as by the
        // survey-delete guard.
        builder.HasIndex(x => x.SurveyModelId);
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

        builder.HasOne(x => x.TripLog).WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // The load-bearing one. A published address is resolved by looking its token up here, with
        // nobody signed in to ask anything else of — so a link whose trip is deleted has to be a
        // link that is not found, by the same lookup that fails for a token nobody ever minted.
        // That is what makes a deleted trip's address answer exactly as an invented one does.
        builder.HasQueryFilter(x => x.TripLog.DeletedAt == null);
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

        builder.HasOne(x => x.TripLog).WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Hidden while its trip is deleted, like everything else a trip's tracking holds.
        builder.HasQueryFilter(x => x.TripLog.DeletedAt == null);
        // Cascade, where the roster and the position log both restrict, and the difference is
        // what the row holds. Those record that a person was somewhere — a fact worth blocking a
        // delete for. This records how one page captioned them, which means nothing once the
        // person's entry is gone; and a merge moves the caption to the survivor before the
        // duplicate is removed, so the ordinary path does not lose the choice either.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TripPartyNumberConfiguration : IEntityTypeConfiguration<TripPartyNumber>
{
    /// <summary>
    /// The database name of the rule that one person holds one number on one trip. Named, and
    /// named here, because a writer that loses a race is recognised by the constraint it broke.
    /// </summary>
    public const string OnePerPersonIndex = "ux_trip_party_numbers_trip_caver";

    /// <summary>
    /// The database name of the table's key — the rule that one number is given once on one trip.
    /// Named here for the same reason: two writers that reach for the same next number are told
    /// apart from every other failed save by this name.
    /// </summary>
    public const string KeyName = "pk_trip_party_numbers";

    public void Configure(EntityTypeBuilder<TripPartyNumber> builder)
    {
        builder.ToTable("trip_party_numbers", table =>
            table.HasCheckConstraint("ck_trip_party_numbers_number_from_one", "number >= 1"));

        // The trip and the number are the row: a number is given once and never changes, which is
        // what a key is, and it makes "no two people share a number on a trip" the table's own
        // shape rather than a second index beside an invented id.
        builder.HasKey(x => new { x.TripLogId, x.Number }).HasName(KeyName);
        builder.Property(x => x.Number).ValueGeneratedNever();

        // One number per person per trip. A number whose holder is gone has no person, and any
        // number of those may stand on one trip — which is what an index over an empty value
        // allows by itself.
        builder.HasIndex(x => new { x.TripLogId, x.CaverId }, OnePerPersonIndex)
            .IsUnique()
            .HasDatabaseName(OnePerPersonIndex);
        builder.HasIndex(x => x.CaverId, "ix_trip_party_numbers_caver_id")
            .HasDatabaseName("ix_trip_party_numbers_caver_id");

        builder.HasOne(x => x.TripLog).WithMany().HasForeignKey(x => x.TripLogId).OnDelete(DeleteBehavior.Cascade);
        // Hidden while its trip is deleted, like everything else a trip's tracking holds.
        builder.HasQueryFilter(x => x.TripLog.DeletedAt == null);
        // Emptied, where the roster restricts and a caption goes with its person. The row holds
        // nobody in place — whether somebody was on a trip is the roster's and the log's to say —
        // but the number must outlive them, or the next person the trip names would be given it
        // and a page already showing "Caver 3" would come to mean somebody else.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.CaverId).OnDelete(DeleteBehavior.SetNull);
    }
}
