// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(300);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // Restrict, like every other owned content table: an account that still owns
        // documents cannot be dropped out from under them.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);
        // Restrict as well: a kind that documents still claim cannot be removed out from
        // under them, because the metadata they hold is only readable against its schema.
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(x => x.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.CavingGroupId);
        builder.HasIndex(x => x.DocumentTypeId);
    }
}

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions",
            t => t.HasCheckConstraint("ck_document_versions_number", "version_number >= 1"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Label).HasMaxLength(200);
        builder.Property(x => x.ChangeNote).HasMaxLength(2000);
        builder.Property(x => x.VersionNumber).HasDefaultValue(1);

        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.SetNull);

        // One row per (document, number); doubles as the concurrency guard for racing uploads.
        builder.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
        // "Which version is current" gets exactly one answer, enforced here rather than by
        // convention: a second current version is rejected by the database.
        builder.HasIndex(x => x.DocumentId).HasFilter("is_current").IsUnique()
            .HasDatabaseName("ix_document_versions_current");
        builder.HasIndex(x => x.UploadedBy);
    }
}

public sealed class DocumentPageConfiguration : IEntityTypeConfiguration<DocumentPage>
{
    public void Configure(EntityTypeBuilder<DocumentPage> builder)
    {
        builder.ToTable("document_pages",
            t => t.HasCheckConstraint("ck_document_pages_number", "page_number >= 1"));

        // Pages are re-derivable from the bytes, so they go with the file they describe.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.FileId, x.PageNumber }).IsUnique();
    }
}
