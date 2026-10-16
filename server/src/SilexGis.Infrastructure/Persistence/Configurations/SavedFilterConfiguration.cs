// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SavedFilterConfiguration : IEntityTypeConfiguration<SavedFilter>
{
    public void Configure(EntityTypeBuilder<SavedFilter> builder)
    {
        builder.ToTable("saved_filters");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(SavedFilterRules.MaxNameLength);
        builder.Property(x => x.Description).HasMaxLength(SavedFilterRules.MaxDescriptionLength);
        builder.Property(x => x.Document).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Visibility).HasConversion<short>();

        // Cascades with its owner, as a saved map view does: both are one person's saved
        // workspace, and a filter describes rows rather than holding any, so nothing anybody else
        // relies on goes with it. A club that dissolves leaves its shared filters standing, private
        // to whoever wrote them.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId).HasDatabaseName("ix_saved_filters_owner_user_id");
        builder.HasIndex(x => x.CavingGroupId).HasDatabaseName("ix_saved_filters_caving_group_id");
    }
}
