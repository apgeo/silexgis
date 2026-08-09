// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("albums", t =>
        {
            // The subject is a feature or the polymorphic pair or nothing — never a mixture.
            // Stated here as well as in the write path because a row breaking it could not be
            // interpreted at read time at all.
            t.HasCheckConstraint(
                "ck_albums_subject",
                "(subject_feature_id is null and subject_entity_type is null and subject_entity_id is null) "
                + "or (subject_feature_id is not null and subject_entity_type is null and subject_entity_id is null) "
                + "or (subject_feature_id is null and subject_entity_type is not null and subject_entity_id is not null)");
        });
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.SubjectEntityType).HasConversion<short>();

        // Restrict, like every other owned content table: an account that still owns albums
        // cannot be dropped out from under them.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // The cover loses its pointer rather than holding the album back: a cover is a
        // presentation choice, and deleting the picture it names should leave an album with no
        // cover rather than an album nobody can delete.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.CoverDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.SubjectFeatureId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.CavingGroupId);
        builder.HasIndex(x => x.SubjectFeatureId).HasFilter("subject_feature_id is not null");
        builder.HasIndex(x => new { x.SubjectEntityType, x.SubjectEntityId })
            .HasFilter("subject_entity_id is not null");
    }
}

public sealed class AlbumItemConfiguration : IEntityTypeConfiguration<AlbumItem>
{
    public void Configure(EntityTypeBuilder<AlbumItem> builder)
    {
        builder.ToTable("album_items");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Caption).HasMaxLength(500);

        builder.HasOne<Album>().WithMany().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
        // Cascade as well: a document that is really gone leaves no membership behind. Soft
        // deletion never reaches here — a marked document keeps its place so restoring it puts
        // the picture back where it was in the album.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // A picture sits in an album once. Adding it twice is the same fact, not two places.
        builder.HasIndex(x => new { x.AlbumId, x.DocumentId }).IsUnique();
        // The listing's own order.
        builder.HasIndex(x => new { x.AlbumId, x.SortOrder });
        // "Which albums is this picture in", which the photograph's own panel asks.
        builder.HasIndex(x => x.DocumentId);
    }
}

public sealed class AlbumShareConfiguration : IEntityTypeConfiguration<AlbumShare>
{
    public void Configure(EntityTypeBuilder<AlbumShare> builder)
    {
        builder.ToTable("album_shares");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.Property(x => x.Mode).HasConversion<short>();

        builder.HasOne<Album>().WithMany().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);

        // The token is the identity a link resolves by, so it is unique and indexed: every
        // anonymous request arrives with nothing else to look the album up by.
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.AlbumId);
    }
}

public sealed class PhotoDetailsConfiguration : IEntityTypeConfiguration<PhotoDetails>
{
    public void Configure(EntityTypeBuilder<PhotoDetails> builder)
    {
        builder.ToTable("photo_details");

        // Keyed by the document: one row describes one photograph, and there is no second
        // identity for it to drift from.
        builder.HasKey(x => x.DocumentId);
        builder.Property(x => x.DocumentId).ValueGeneratedNever();

        builder.Property(x => x.PhotographerName).HasMaxLength(200);
        builder.Property(x => x.Caption).HasMaxLength(1000);
        builder.Property(x => x.LicenceCode).HasMaxLength(32);
        builder.Property(x => x.PlaceName).HasMaxLength(300);

        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        // A credit outlives the roster entry being tidied up: losing the link is better than
        // refusing to remove a caver because a photograph names them.
        builder.HasOne<Caver>().WithMany().HasForeignKey(x => x.PhotographerCaverId)
            .OnDelete(DeleteBehavior.SetNull);

        // "Everything Ana photographed" is the filter that makes crediting a caver worth doing.
        builder.HasIndex(x => x.PhotographerCaverId).HasFilter("photographer_caver_id is not null");
        // The public gallery reads only the marked ones, and there are few of them.
        builder.HasIndex(x => x.InPublicGallery).HasFilter("in_public_gallery");
    }
}
