// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ResLinkRelationTypeConfiguration : IEntityTypeConfiguration<ResLinkRelationType>
{
    public void Configure(EntityTypeBuilder<ResLinkRelationType> builder)
    {
        builder.ConfigureTaxonomy("res_link_relation_types");
        builder.Property(x => x.InverseName).HasMaxLength(100);
    }
}

public sealed class ResLinkConfiguration : IEntityTypeConfiguration<ResLink>
{
    public void Configure(EntityTypeBuilder<ResLink> builder)
    {
        builder.ToTable("res_links");
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Fixed-length base62 permalink code; this unique index is what the CSPRNG
        // draw retries against on the rare collision.
        builder.Property(x => x.ShortCode).HasMaxLength(8);
        builder.HasIndex(x => x.ShortCode).IsUnique();

        // Restrict, not cascade: deleting a relation type in use would silently untype
        // every link that meant it — the delete is refused while links reference the row.
        builder.HasOne<ResLinkRelationType>().WithMany().HasForeignKey(x => x.RelationTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ResLinkMemberConfiguration : IEntityTypeConfiguration<ResLinkMember>
{
    public void Configure(EntityTypeBuilder<ResLinkMember> builder)
    {
        builder.ToTable("res_link_members", t =>
        {
            // Two-world target, same XOR convention as attachments/taggings: EITHER a
            // feature (real FK) OR a non-feature entity via the FK-less polymorphic
            // pair — never both, never neither.
            t.HasCheckConstraint(
                "ck_res_link_members_one_target",
                "(feature_id IS NOT NULL AND entity_type IS NULL AND entity_id IS NULL) OR " +
                "(feature_id IS NULL AND entity_type IS NOT NULL AND entity_id IS NOT NULL)");
            // Whole (0) is the only anchor kind without a payload; every other kind
            // carries one — the database mirror of the write-path payload rule.
            t.HasCheckConstraint(
                "ck_res_link_members_anchor_payload",
                "(anchor_kind = 0 AND anchor IS NULL) OR (anchor_kind <> 0 AND anchor IS NOT NULL)");
        });
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EntityType).HasConversion<short?>();
        builder.Property(x => x.AnchorKind).HasConversion<short>();
        builder.Property(x => x.Anchor).HasColumnType("jsonb");

        builder.HasOne<ResLink>().WithMany().HasForeignKey(x => x.ResLinkId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        // The measured-against file pin: dropped when the file is purged, the member stays.
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.AnchorFileId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ResLinkId);
        // The distinguished member a directed relation reads from: at most one per link.
        // Named so it stays a second index beside the plain members-of-a-link one above.
        builder.HasIndex(x => x.ResLinkId, "ix_res_link_members_main").HasFilter("is_main").IsUnique()
            .HasDatabaseName("ix_res_link_members_main");
        builder.HasIndex(x => x.FeatureId).HasFilter("feature_id IS NOT NULL");
        // The polymorphic target has no FK by design; lookups are by (type, id).
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        // One Whole membership per (link, target) — anchored parts are unconstrained.
        // Two indexes because the target lives in one of two column shapes.
        builder.HasIndex(x => new { x.ResLinkId, x.FeatureId }).IsUnique()
            .HasFilter("feature_id IS NOT NULL AND anchor_kind = 0")
            .HasDatabaseName("ix_res_link_members_whole_feature");
        builder.HasIndex(x => new { x.ResLinkId, x.EntityType, x.EntityId }).IsUnique()
            .HasFilter("entity_type IS NOT NULL AND anchor_kind = 0")
            .HasDatabaseName("ix_res_link_members_whole_entity");

        // Walking from one end of a link to the other. Asking which trips a cave is named on,
        // or which places a trip named, starts at a target and needs the links it is in; the
        // two indexes above answer only the first half of that from the index and go to the
        // table for the link id, once per row, on a join that is then walked back the other
        // way. These carry the link id, so both hops stay in the index. Named explicitly
        // because they are second indexes on columns that already lead one.
        builder.HasIndex(x => new { x.FeatureId, x.ResLinkId }, "ix_res_link_members_feature_link")
            .HasFilter("feature_id IS NOT NULL")
            .HasDatabaseName("ix_res_link_members_feature_link");
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.ResLinkId }, "ix_res_link_members_entity_link")
            .HasFilter("entity_type IS NOT NULL")
            .HasDatabaseName("ix_res_link_members_entity_link");
    }
}
