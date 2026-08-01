// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class FeatureConfiguration : IEntityTypeConfiguration<Feature>
{
    public void Configure(EntityTypeBuilder<Feature> builder)
    {
        builder.ToTable("features", t =>
        {
            // Generic rows carry the data-level kind; subtyped kinds keep their own taxonomies.
            t.HasCheckConstraint(
                "ck_features_generic_type",
                "(kind = 0) = (feature_type_id IS NOT NULL)");

            // Per-kind geometry class floors. geometrytype() ignores the Z flag, so PointZ
            // passes 'POINT'. Generic kinds are validated against their feature type's
            // accepted classes in the write path (data-driven, not expressible here).
            t.HasCheckConstraint(
                "ck_features_cave_geom",
                "kind <> 1 OR geom IS NULL OR geometrytype(geom) = 'POINT'");
            t.HasCheckConstraint(
                "ck_features_entrance_geom",
                "kind <> 2 OR (geom IS NOT NULL AND geometrytype(geom) = 'POINT')");
            t.HasCheckConstraint(
                "ck_features_centerline_geom",
                "kind <> 3 OR (geom IS NOT NULL AND geometrytype(geom) = 'MULTILINESTRING')");

            // Untyped geometry column (mixed classes and dimensions by design); SRID
            // enforced here instead of a typmod, like geofile_features.
            t.HasCheckConstraint("ck_features_geom_srid", "geom IS NULL OR st_srid(geom) = 4326");
        });

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Category).HasConversion<short>();
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.Visibility).HasConversion<short>();
        builder.Property(x => x.Properties).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        // Mixed geometry classes and dimensions by design (one column for every kind; GPX/
        // survey sources carry Z). SRID enforced by the check above, like geofile_features.
        builder.Property(x => x.Geom).HasColumnType("geometry");

        // Self + all ancestors over every containment path — the flat cascade/protection
        // context every read filter splices. Maintained only by the feature write service.
        builder.Property(x => x.AncestorIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");

        // The (id, kind) pair is the composite-FK target that makes cross-kind subtype rows
        // impossible at the database level (each subtype table FKs it with its kind CHECKed).
        builder.HasAlternateKey(x => new { x.Id, x.Kind });

        builder.HasOne<FeatureType>().WithMany().HasForeignKey(x => x.FeatureTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId).OnDelete(DeleteBehavior.SetNull);

        // Two indexes over one column need distinct model names or EF collapses them; the
        // database names are pinned because the naming convention would suffix-dedupe them.
        builder.HasIndex(x => x.Geom, "ix_features_geom").HasMethod("gist")
            .HasDatabaseName("ix_features_geom");
        // The entrance layer is the hot map path; its partial index keeps bbox scans from
        // reading other kinds' rows at all.
        builder.HasIndex(x => x.Geom, "ix_features_geom_entrances").HasMethod("gist").HasFilter("kind = 2")
            .HasDatabaseName("ix_features_geom_entrances");
        builder.HasIndex(x => x.Kind);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.CavingGroupId);
        builder.HasIndex(x => x.DeletedAt).HasFilter("deleted_at IS NULL");

        // Accent-insensitive full-text search over the supertype payload (shadow property so
        // Domain stays free of provider types; queries use EF.Property<NpgsqlTsVector>).
        // Kind-specific extras (cave toponyms) keep their own vector on the subtype table.
        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', immutable_unaccent(coalesce(name, '') || ' ' || " +
                "coalesce(description, '')))",
                stored: true);
        builder.HasIndex("SearchVector").HasMethod("gin");

        // Soft delete (stamped over the whole containment subtree by the write service).
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class CaveConfiguration : IEntityTypeConfiguration<Cave>
{
    public void Configure(EntityTypeBuilder<Cave> builder)
    {
        builder.ToTable("caves", t => t.HasCheckConstraint("ck_caves_kind", "kind = 1"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Composite FK (id, kind) against the features alternate key + the CHECK above:
        // a caves row can only ever sit on a kind=Cave feature.
        // The sentinel says which value means "not set" and so leaves the column to the
        // database default. Nothing ever assigns this shadow property, so that is the CLR
        // default — stating it keeps the model from warning that it had to assume as much.
        builder.Property<FeatureKind>("Kind").HasConversion<short>()
            .HasDefaultValue(FeatureKind.Cave).HasSentinel(FeatureKind.Generic);
        builder.HasOne(x => x.Feature).WithOne(f => f.Cave)
            .HasForeignKey<Cave>(nameof(Cave.Id), "Kind")
            .HasPrincipalKey<Feature>(nameof(Feature.Id), nameof(Feature.Kind))
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.OtherToponyms).HasMaxLength(250);
        builder.Property(x => x.IdentificationCode).HasMaxLength(50);
        builder.Property(x => x.Website).HasMaxLength(255);
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.Property(x => x.HydrographicBasin).HasMaxLength(100);
        builder.Property(x => x.Valley).HasMaxLength(100);
        builder.Property(x => x.TributaryRiver).HasMaxLength(100);
        builder.Property(x => x.ClosestAddress).HasMaxLength(200);
        builder.Property(x => x.LandRegistryNumber).HasMaxLength(50);
        builder.Property(x => x.RockAge).HasMaxLength(50);
        builder.Property(x => x.ProtectionClass).HasMaxLength(50);
        builder.Property(x => x.DiscoveryDate).HasMaxLength(50);
        builder.Property(x => x.Discoverer).HasMaxLength(255);

        // Meters with cm precision.
        foreach (var metric in new[]
        {
            nameof(Cave.SurveyedLength), nameof(Cave.EstimatedLength), nameof(Cave.RealExtension),
            nameof(Cave.ProjectedExtension), nameof(Cave.Depth), nameof(Cave.PositiveDepth),
            nameof(Cave.NegativeDepth), nameof(Cave.PotentialDepth), nameof(Cave.Altitude),
            nameof(Cave.Volume), nameof(Cave.Area), nameof(Cave.ShowCaveLength),
        })
        {
            builder.Property(metric).HasPrecision(10, 2);
        }

        builder.Property(x => x.RamificationIndex).HasPrecision(6, 3);
        builder.Property(x => x.ExplorationStatus).HasConversion<short>();

        builder.HasOne<CaveType>().WithMany().HasForeignKey(x => x.CaveTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RockType>().WithMany().HasForeignKey(x => x.RockTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Region);

        // Toponym/code search lives on the subtype (the supertype vector covers name +
        // description); the search endpoint consults both.
        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', immutable_unaccent(coalesce(other_toponyms, '') || ' ' || " +
                "coalesce(identification_code, '')))",
                stored: true);
        builder.HasIndex("SearchVector").HasMethod("gin");

        // Mirror the supertype's soft-delete filter so subtype-rooted queries cannot see
        // rows whose feature is deleted.
        builder.HasQueryFilter(x => x.Feature.DeletedAt == null);
    }
}

public sealed class CaveEntranceConfiguration : IEntityTypeConfiguration<CaveEntrance>
{
    public void Configure(EntityTypeBuilder<CaveEntrance> builder)
    {
        builder.ToTable("cave_entrances", t => t.HasCheckConstraint("ck_cave_entrances_kind", "kind = 2"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property<FeatureKind>("Kind").HasConversion<short>()
            .HasDefaultValue(FeatureKind.CaveEntrance).HasSentinel(FeatureKind.Generic);
        builder.HasOne(x => x.Feature).WithOne(f => f.Entrance)
            .HasForeignKey<CaveEntrance>(nameof(CaveEntrance.Id), "Kind")
            .HasPrincipalKey<Feature>(nameof(Feature.Id), nameof(Feature.Kind))
            .OnDelete(DeleteBehavior.Cascade);

        // Structural exactly-one-cave truth; the write service mirrors it as the primary
        // containment edge. Entrances die with their cave on purge.
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<EntranceType>().WithMany().HasForeignKey(x => x.EntranceTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Altitude).HasPrecision(10, 2);
        builder.Property(x => x.PositionQuality).HasConversion<short>();

        builder.HasIndex(x => x.CaveFeatureId);

        builder.HasQueryFilter(x => x.Feature.DeletedAt == null);
    }
}

public sealed class CenterlineConfiguration : IEntityTypeConfiguration<Centerline>
{
    public void Configure(EntityTypeBuilder<Centerline> builder)
    {
        builder.ToTable("centerlines", t => t.HasCheckConstraint("ck_centerlines_kind", "kind = 3"));
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property<FeatureKind>("Kind").HasConversion<short>()
            .HasDefaultValue(FeatureKind.Centerline).HasSentinel(FeatureKind.Generic);
        builder.HasOne(x => x.Feature).WithOne(f => f.Centerline)
            .HasForeignKey<Centerline>(nameof(Centerline.Id), "Kind")
            .HasPrincipalKey<Feature>(nameof(Feature.Id), nameof(Feature.Kind))
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId).OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.Skeleton).HasColumnType("geometry(MultiLineString, 4326)");
        builder.Property(x => x.LengthM).HasPrecision(12, 2);
        builder.Property(x => x.Source).HasConversion<short>();

        builder.HasIndex(x => x.CaveFeatureId);
        // The cave's current shape: at most one default centerline per cave.
        builder.HasIndex(x => x.CaveFeatureId).HasFilter("is_default").IsUnique()
            .HasDatabaseName("ix_centerlines_default_per_cave");

        builder.HasQueryFilter(x => x.Feature.DeletedAt == null);
    }
}

public sealed class FeatureHierarchyEdgeConfiguration : IEntityTypeConfiguration<FeatureHierarchyEdge>
{
    public void Configure(EntityTypeBuilder<FeatureHierarchyEdge> builder)
    {
        builder.ToTable("feature_hierarchy_edges",
            t => t.HasCheckConstraint("ck_feature_hierarchy_edges_no_self", "parent_id <> child_id"));

        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ParentId, x.ChildId }).IsUnique();
        builder.HasIndex(x => x.ChildId);
        // Exactly one primary parent per feature (roots have no edges at all).
        builder.HasIndex(x => x.ChildId).HasFilter("is_primary").IsUnique()
            .HasDatabaseName("ix_feature_hierarchy_edges_primary");
    }
}

public sealed class FeatureAncestorConfiguration : IEntityTypeConfiguration<FeatureAncestor>
{
    public void Configure(EntityTypeBuilder<FeatureAncestor> builder)
    {
        builder.ToTable("feature_ancestors");
        builder.HasKey(x => new { x.FeatureId, x.AncestorId });

        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.AncestorId).OnDelete(DeleteBehavior.Cascade);

        // Subtree queries: every feature under an ancestor.
        builder.HasIndex(x => x.AncestorId);
    }
}

public sealed class HierarchyConfiguration : IEntityTypeConfiguration<Hierarchy>
{
    public void Configure(EntityTypeBuilder<Hierarchy> builder)
    {
        builder.ToTable("hierarchies");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Slug).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class HierarchyMembershipConfiguration : IEntityTypeConfiguration<HierarchyMembership>
{
    public void Configure(EntityTypeBuilder<HierarchyMembership> builder)
    {
        builder.ToTable("hierarchy_memberships");

        builder.HasOne<Hierarchy>().WithMany().HasForeignKey(x => x.HierarchyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.ParentFeatureId).OnDelete(DeleteBehavior.Cascade);

        // Materialized path (labels = feature ids with '-' — legal ltree labels on PG 16+).
        // Shadow property so Domain stays free of provider types; parallel hierarchies are
        // strict trees, organizational only — no security semantics ride on them.
        builder.Property<Microsoft.EntityFrameworkCore.LTree>("Path").HasColumnName("path");
        builder.HasIndex("Path").HasMethod("gist");

        builder.HasIndex(x => new { x.HierarchyId, x.FeatureId }).IsUnique();
        builder.HasIndex(x => x.FeatureId);
    }
}

public sealed class LinkKindConfiguration : IEntityTypeConfiguration<LinkKind>
{
    public void Configure(EntityTypeBuilder<LinkKind> builder) => builder.ConfigureTaxonomy("link_kinds");
}

public sealed class FeatureLinkConfiguration : IEntityTypeConfiguration<FeatureLink>
{
    public void Configure(EntityTypeBuilder<FeatureLink> builder)
    {
        builder.ToTable("feature_links",
            t => t.HasCheckConstraint("ck_feature_links_no_self", "from_id <> to_id"));

        builder.Property(x => x.Note).HasMaxLength(1000);

        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FromId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.ToId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LinkKind>().WithMany().HasForeignKey(x => x.LinkKindId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.FromId, x.ToId, x.LinkKindId }).IsUnique();
        builder.HasIndex(x => x.ToId);
    }
}

public sealed class FeatureShareConfiguration : IEntityTypeConfiguration<FeatureShare>
{
    public void Configure(EntityTypeBuilder<FeatureShare> builder)
    {
        builder.ToTable("feature_shares");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.Property(x => x.Mode).HasConversion<short>();

        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.FeatureId);
    }
}
