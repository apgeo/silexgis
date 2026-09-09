// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class FeatureExternalIdConfiguration : IEntityTypeConfiguration<FeatureExternalId>
{
    public void Configure(EntityTypeBuilder<FeatureExternalId> builder)
    {
        builder.ToTable("feature_external_ids");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.System).HasConversion<short>();

        // Long enough for any register's numbering and short enough that a mistyped paste of a
        // whole page is refused rather than stored.
        builder.Property(x => x.Value).HasMaxLength(200);

        builder.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);

        // One identifier per feature per register: two numbers for the same cave in the same
        // register is a contradiction, and the database is where that is settled rather than in
        // whichever handler happens to write next.
        builder.HasIndex(x => new { x.FeatureId, x.System }).IsUnique();

        // "Which cave is this, in our terms" — asked from the other direction, by whoever is
        // holding somebody else's number.
        builder.HasIndex(x => new { x.System, x.Value });
    }
}
