// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.Property(x => x.Id).ValueGeneratedNever();

        // A deleted document is gone from every query that does not deliberately ask for it.
        //
        // As a model-wide filter rather than a condition each read path remembers, because
        // "every listing filters on this" is the kind of rule that holds for a year and then
        // does not: the one query somebody adds without it shows a document the interface has
        // already told them is deleted. The two places that must see through it — restoring
        // one, and the sweep that purges them — say IgnoreQueryFilters, which is a visible
        // decision at the call site rather than an omission.
        builder.HasQueryFilter(d => d.DeletedAt == null);

        builder.Property(x => x.Title).HasMaxLength(300);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Language).HasMaxLength(DocumentLanguage.MaxLength);

        // Restrict, like every other owned content table: an account that still owns
        // documents cannot be dropped out from under them.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);
        // Restrict as well: a kind that documents still claim cannot be removed out from
        // under them, because the metadata they hold is only readable against its schema.
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(x => x.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // The drop this arrived in. Set null rather than restricting: a batch is a record of
        // something that happened and may be pruned, and its documents outlive it — losing
        // the reference is a loss of provenance, not of content.
        builder.HasOne<UploadBatch>().WithMany().HasForeignKey(x => x.UploadBatchId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.CavingGroupId);
        builder.HasIndex(x => x.DocumentTypeId);

        // "Everything that came out of that archive" — filtered, because almost every row is
        // null and an index over them would be mostly a copy of the table.
        builder.HasIndex(x => x.UploadBatchId).HasFilter("upload_batch_id is not null");

        // The purge sweep reads by this and nothing else, and almost every row is null: a
        // filtered index over the deleted ones is small however large the archive is.
        builder.HasIndex(x => x.DeletedAt).HasFilter("deleted_at is not null");
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.DeletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
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

        builder.Property(x => x.Extractor).HasMaxLength(64);

        builder.HasIndex(x => new { x.FileId, x.PageNumber }).IsUnique();

        // The full-text vector over page_text is deliberately NOT mapped here. It is a real
        // column with a GIN index, maintained by a database trigger and read only by the raw
        // content-search query. Mapping it would pull a vector derived from up to two million
        // characters into memory for every page row loaded on a re-extraction, for a value no
        // C# code ever looks at. The column, its trigger and its index are created in the
        // migration that introduced content search; because nothing here maps it, no later
        // model change will propose dropping it either.
    }
}

public sealed class DocumentCommentConfiguration : IEntityTypeConfiguration<DocumentComment>
{
    public void Configure(EntityTypeBuilder<DocumentComment> builder)
    {
        builder.ToTable("document_comments", t =>
        {
            // The whole-document anchor is the one kind with no payload, and the only one
            // that is never pinned to a file. Stated here as well as in the rules class
            // because a row that broke it could not be interpreted at read time at all.
            t.HasCheckConstraint(
                "ck_document_comments_anchor_payload",
                "(anchor_kind = 0 AND anchor IS NULL AND anchor_file_id IS NULL) OR (anchor_kind <> 0 AND anchor IS NOT NULL)");
            t.HasCheckConstraint("ck_document_comments_body", "length(btrim(body)) > 0");
        });

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Body).HasMaxLength(DocumentCommentRules.MaxBodyLength);
        builder.Property(x => x.AnchorKind).HasConversion<short>();
        builder.Property(x => x.Anchor).HasColumnType("jsonb");

        // The comment belongs to the document, not to the bytes: deleting the document
        // takes its discussion with it, and a reply goes with the comment it answers.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DocumentComment>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
        // The pin names an immutable file it was measured against; losing that file
        // degrades the anchor to a coarser one rather than deleting the remark.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.AnchorFileId).OnDelete(DeleteBehavior.SetNull);

        // The listing order: a document's thread, oldest first.
        builder.HasIndex(x => new { x.DocumentId, x.CreatedAt });
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => x.AuthorId);
        builder.HasIndex(x => x.AnchorFileId);
    }
}

public sealed class TextSearchLanguageConfiguration : IEntityTypeConfiguration<TextSearchLanguage>
{
    public void Configure(EntityTypeBuilder<TextSearchLanguage> builder)
    {
        builder.ToTable("text_search_languages");
        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code).HasMaxLength(DocumentLanguage.MaxLength);
        builder.Property(x => x.Configuration).HasMaxLength(64);
    }
}
