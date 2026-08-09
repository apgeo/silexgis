// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CabinetConfiguration : IEntityTypeConfiguration<Cabinet>
{
    public void Configure(EntityTypeBuilder<Cabinet> builder)
    {
        builder.ToTable("cabinets",
            t => t.HasCheckConstraint("ck_cabinets_no_self_parent", "parent_id <> id"));
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);

        // Restrict: a cabinet that still holds shelves cannot be removed out from under
        // them. Emptying the tree is an explicit, audited act — a cascade would delete a
        // whole archive's structure as a side effect of one click on its root.
        builder.HasOne<Cabinet>().WithMany().HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Materialized path (labels = cabinet ids with '-' — legal path labels on PG 16+),
        // root first and including the cabinet itself, so a subtree is one indexed prefix
        // match and a breadcrumb is a parse rather than a walk. Shadow property so Domain
        // stays free of provider types; the cabinet write service is its sole mutator, and
        // being a shadow property also keeps this derived bookkeeping out of audit diffs.
        builder.Property<Microsoft.EntityFrameworkCore.LTree>("Path").HasColumnName("path");
        builder.HasIndex("Path").HasMethod("gist");

        // The same ancestry as a flat array, because the access filters overlap arrays
        // rather than parsing paths: "is this document filed at or below cabinet X" has to
        // be one indexed test inside a read filter that runs per row.
        builder.Property(x => x.AncestorIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
        builder.HasIndex(x => x.AncestorIds).HasMethod("gin");

        // What this shelf says about whatever lands on it. All four are nullable or empty by
        // default, which is what "this shelf has no opinion, ask the one above" looks like.
        builder.Property(x => x.DefaultVisibility).HasConversion<short?>();
        builder.Property(x => x.DefaultTagIds).HasColumnType("bigint[]")
            .HasDefaultValueSql("'{}'::bigint[]");
        builder.Property(x => x.RequiredMetadataKeys).HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");

        // Restrict, as documents do: a kind still named as a shelf's default cannot be removed
        // out from under it, or the shelf would silently start typing nothing.
        builder.HasOne<DocumentType>().WithMany().HasForeignKey(x => x.DefaultDocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Tag ids are an array rather than a junction table, and deliberately carry no
        // referential integrity: they are a default to copy onto new documents, not a
        // membership. A deleted tag leaves an id nothing resolves, which the resolver drops —
        // the alternative is a junction table whose only reader is the upload path.
        // A name identifies a cabinet among its siblings, not globally: "1987" may sit
        // under several archives. Postgres treats NULLs as distinct, so root cabinets need
        // an index of their own — a single (parent_id, name) index would let two roots
        // share a name.
        builder.HasIndex(x => new { x.ParentId, x.Name }).IsUnique()
            .HasFilter("parent_id IS NOT NULL")
            .HasDatabaseName("ix_cabinets_parent_name");
        builder.HasIndex(x => x.Name).IsUnique()
            .HasFilter("parent_id IS NULL")
            .HasDatabaseName("ix_cabinets_root_name");

        // The children lookup the tree read runs; the filtered uniqueness indexes above
        // cannot serve it.
        builder.HasIndex(x => x.ParentId);
    }
}

public sealed class CabinetDocumentConfiguration : IEntityTypeConfiguration<CabinetDocument>
{
    public void Configure(EntityTypeBuilder<CabinetDocument> builder)
    {
        builder.ToTable("cabinet_documents");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side

        builder.HasOne<Cabinet>().WithMany().HasForeignKey(x => x.CabinetId)
            .OnDelete(DeleteBehavior.Cascade);
        // Cascade as well: deleting a document takes its filings with it, so removing the
        // last version of an upload does not run into a membership row holding it back.
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // A document is filed in a cabinet once; filing it twice is the same fact.
        builder.HasIndex(x => new { x.CabinetId, x.DocumentId }).IsUnique();
        // The reverse lookup the cabinet-scope filter arm runs (document → its cabinets).
        builder.HasIndex(x => x.DocumentId);
    }
}
