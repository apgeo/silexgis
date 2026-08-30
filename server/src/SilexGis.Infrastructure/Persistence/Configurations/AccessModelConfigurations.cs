// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class PermissionGroupConfiguration : IEntityTypeConfiguration<PermissionGroup>
{
    public void Configure(EntityTypeBuilder<PermissionGroup> builder)
    {
        builder.ToTable("permission_groups");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Slug).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class PermissionGroupMemberConfiguration : IEntityTypeConfiguration<PermissionGroupMember>
{
    public void Configure(EntityTypeBuilder<PermissionGroupMember> builder)
    {
        builder.ToTable("permission_group_members");
        builder.Property(x => x.MemberKind).HasConversion<short>();

        builder.HasOne<PermissionGroup>().WithMany().HasForeignKey(x => x.PermissionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // member_id has no FK (kind-dependent); the integrity verifier flags dangling
        // ids, and the live-admin guard keeps Full Administrators reachable regardless.
        builder.HasIndex(x => new { x.PermissionGroupId, x.MemberKind, x.MemberId }).IsUnique();
        builder.HasIndex(x => new { x.MemberKind, x.MemberId });
    }
}

public sealed class AccessEntryConfiguration : IEntityTypeConfiguration<AccessEntry>
{
    public void Configure(EntityTypeBuilder<AccessEntry> builder)
    {
        // Two homes, XOR-checked: a ruleset entry or a direct subject entry — never
        // both, never neither. The scope-anchor CHECK keeps the columns structurally
        // consistent with the scope kind; the full semantic table (domain validity,
        // Create restrictions, narrowing rules) lives in AccessEntryRules.
        builder.ToTable("access_entries", t =>
        {
            t.HasCheckConstraint(
                "ck_access_entries_one_home",
                "(permission_group_id IS NOT NULL AND subject_kind IS NULL AND subject_id IS NULL) OR " +
                "(permission_group_id IS NULL AND subject_kind IS NOT NULL AND subject_id IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_access_entries_scope_anchor",
                "(scope_kind IN (0, 1) AND scope_feature_id IS NULL AND scope_id IS NULL) OR " +
                "(scope_kind IN (2, 4, 6) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR " +
                "(scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR " +
                "(scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR " +
                "(domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");
            t.HasCheckConstraint(
                "ck_access_entries_narrowing",
                "(feature_kind IS NULL AND feature_type_id IS NULL) OR " +
                "(domain = 0 AND scope_kind IN (0, 1) AND (feature_kind IS NULL OR feature_type_id IS NULL))");

            // A camp's sharing reaches the trips it gathered, one rule per member trip and
            // nothing else: a marked row is therefore always a trip-log rule at object reach
            // (domain 1, scope kind 5). Structural, so a later writer that marks some other
            // row is refused by the database rather than by whoever reviews it — the marker
            // is what withdrawal and re-application key on, and a row it can reach but was
            // never meant to would be withdrawn by an act that has nothing to do with it.
            t.HasCheckConstraint(
                "ck_access_entries_granted_via_expedition",
                "granted_via_expedition_id IS NULL OR (domain = 1 AND scope_kind = 5)");
        });

        builder.Property(x => x.SubjectKind).HasConversion<short?>();
        builder.Property(x => x.Effect).HasConversion<short>();
        builder.Property(x => x.Domain).HasConversion<short>();
        builder.Property(x => x.Actions).HasConversion<int>();
        builder.Property(x => x.ScopeKind).HasConversion<short>();
        builder.Property(x => x.FeatureKind).HasConversion<short?>();

        builder.HasOne<PermissionGroup>().WithMany().HasForeignKey(x => x.PermissionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Anchors are RESTRICT on purpose: deleting a feature or feature type that
        // entries hang on requires removing those entries first, an explicit and
        // audited act — a cascade would silently cancel denies.
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.ScopeFeatureId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeatureType>().WithMany().HasForeignKey(x => x.FeatureTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.GrantedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // RESTRICT, like every other thing a rule hangs on: a camp going away withdraws the
        // rules its sharing wrote, and that withdrawal is loaded and removed by the delete
        // handler so the trail records who lost what. A database cascade would remove them
        // with nothing in the change tracker to say it had happened.
        builder.HasOne<Expedition>().WithMany().HasForeignKey(x => x.GrantedViaExpeditionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PermissionGroupId);
        builder.HasIndex(x => new { x.SubjectKind, x.SubjectId });
        builder.HasIndex(x => x.ScopeFeatureId);

        // The non-feature anchor has no foreign key to bring an index with it, and every
        // read of "the rules written on this object" — the object permissions tab, each
        // delete flow that withdraws what pointed at the row it removes — filters on it.
        builder.HasIndex(x => x.ScopeId);
    }
}

public sealed class FeatureSetConfiguration : IEntityTypeConfiguration<FeatureSet>
{
    public void Configure(EntityTypeBuilder<FeatureSet> builder)
    {
        builder.ToTable("feature_sets");
        builder.Property(x => x.Id).ValueGeneratedNever(); // uuid v7 generated app-side
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Slug).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class FeatureSetMemberConfiguration : IEntityTypeConfiguration<FeatureSetMember>
{
    public void Configure(EntityTypeBuilder<FeatureSetMember> builder)
    {
        builder.ToTable("feature_set_members");
        builder.HasKey(x => new { x.FeatureSetId, x.FeatureId });
        builder.HasOne<FeatureSet>().WithMany().HasForeignKey(x => x.FeatureSetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        // The reverse lookup the set-scope filter arm runs (feature → its sets).
        builder.HasIndex(x => x.FeatureId);
    }
}
