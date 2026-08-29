// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CaveQrPublicationConfiguration : IEntityTypeConfiguration<CaveQrPublication>
{
    public void Configure(EntityTypeBuilder<CaveQrPublication> builder)
    {
        builder.ToTable("cave_qr_publications");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        // The publication dies with the cave: a code that resolved to a row that is really
        // gone has nothing left to answer about.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        // The person is the point of the record, so losing them must not silently rewrite who
        // decided. Deleting an account that published something is refused until the
        // publication is dealt with, exactly as it is for a share link.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.PublishedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Every read of this table is "what has been decided about this cave", so the plain
        // index carries the history lookup. Two indexes over one column need distinct model
        // names or they are collapsed into one, and the database names are pinned so the
        // naming convention does not suffix-dedupe them.
        builder.HasIndex(x => x.FeatureId, "ix_cave_qr_publications_feature")
            .HasDatabaseName("ix_cave_qr_publications_feature");

        // At most one live publication per cave, held by the database rather than by the
        // handler that writes it. Two live rows would not be a doubled decision but an
        // ambiguous one — withdrawing would leave the codes resolving through the row nobody
        // looked at — and "is this cave published" would depend on which row the read
        // happened to find. Revoked rows sit outside the constraint on purpose: they are the
        // history, and a cave may be published and withdrawn any number of times.
        builder.HasIndex(x => x.FeatureId, "ux_cave_qr_publications_live").IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ux_cave_qr_publications_live");
    }
}
