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
                "(scope_kind IN (2, 4) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR " +
                "(scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR " +
                "(scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR " +
                "(domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");
            t.HasCheckConstraint(
                "ck_access_entries_narrowing",
                "(feature_kind IS NULL AND feature_type_id IS NULL) OR " +
                "(domain = 0 AND scope_kind IN (0, 1) AND (feature_kind IS NULL OR feature_type_id IS NULL))");
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

        builder.HasIndex(x => x.PermissionGroupId);
        builder.HasIndex(x => new { x.SubjectKind, x.SubjectId });
        builder.HasIndex(x => x.ScopeFeatureId);
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
