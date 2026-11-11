// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class ProcessingProfileConfiguration : IEntityTypeConfiguration<ProcessingProfile>
{
    public const int NameLength = 200;
    public const int DescriptionLength = 2000;
    public const int OriginLength = 300;

    public void Configure(EntityTypeBuilder<ProcessingProfile> builder)
    {
        builder.ToTable("processing_profiles", table =>
        {
            // The scope and the columns that say whose it is have to agree, or a row would be read
            // by a rule written for another scope: a group profile names its group and nothing
            // else does, and a personal profile names the person it belongs to.
            table.HasCheckConstraint("ck_processing_profiles_group_scope",
                "(scope = 1) = (caving_group_id is not null)");
            table.HasCheckConstraint("ck_processing_profiles_personal_owner",
                "scope <> 2 or owner_user_id is not null");
        });
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StepId).HasMaxLength(ScanArtifactConfiguration.StepIdLength);
        builder.Property(x => x.ToolVersion).HasMaxLength(ScanArtifactConfiguration.ToolVersionLength);
        builder.Property(x => x.Name).HasMaxLength(NameLength);
        builder.Property(x => x.Description).HasMaxLength(DescriptionLength);
        builder.Property(x => x.Scope).HasConversion<short>();
        builder.Property(x => x.Values).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Origin).HasMaxLength(OriginLength);

        // The author is restricted and the club is cascaded. An installation profile is an
        // ordinary row owned by whoever wrote it, so an account with profiles behind it is not
        // deleted out from under them. A group profile is the group's and means nothing without
        // it, so it goes when the group does — set-null would leave a group-scoped row naming no
        // group, which the scope check above forbids.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Named at declaration, every one: a second unnamed index over a column edits the first.
        // Profiles are always listed for one step, so the step leads.
        builder.HasIndex(x => new { x.StepId, x.Scope }, "ix_processing_profiles_step_scope")
            .HasDatabaseName("ix_processing_profiles_step_scope");
        builder.HasIndex(x => x.OwnerUserId, "ix_processing_profiles_owner_user_id")
            .HasDatabaseName("ix_processing_profiles_owner_user_id");
        builder.HasIndex(x => x.CavingGroupId, "ix_processing_profiles_caving_group_id")
            .HasDatabaseName("ix_processing_profiles_caving_group_id");
    }
}

public sealed class ScanStepDefaultsConfiguration : IEntityTypeConfiguration<ScanStepDefaults>
{
    public void Configure(EntityTypeBuilder<ScanStepDefaults> builder)
    {
        builder.ToTable("scan_step_defaults");
        builder.HasKey(x => x.StepId);
        builder.Property(x => x.StepId).HasMaxLength(ScanArtifactConfiguration.StepIdLength).ValueGeneratedNever();
        builder.Property(x => x.ToolVersion).HasMaxLength(ScanArtifactConfiguration.ToolVersionLength);
        builder.Property(x => x.SoftwareVersion).HasMaxLength(ScanArtifactConfiguration.ToolVersionLength);
        builder.Property(x => x.Values).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
    }
}

public sealed class ScanProfileChoiceConfiguration : IEntityTypeConfiguration<ScanProfileChoice>
{
    public void Configure(EntityTypeBuilder<ScanProfileChoice> builder)
    {
        builder.ToTable("user_scan_profile_choices");
        builder.HasKey(x => new { x.UserId, x.StepId });
        builder.Property(x => x.StepId).HasMaxLength(ScanArtifactConfiguration.StepIdLength);

        // A remembered choice is the account's and names a profile; it means nothing once either
        // is gone, so it goes with both.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProcessingProfile>().WithMany().HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ProfileId, "ix_user_scan_profile_choices_profile_id")
            .HasDatabaseName("ix_user_scan_profile_choices_profile_id");
    }
}
