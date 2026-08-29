// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurveySourceConfiguration : IEntityTypeConfiguration<SurveySource>
{
    public void Configure(EntityTypeBuilder<SurveySource> builder)
    {
        builder.ToTable("survey_sources");
        builder.Property(x => x.Id).ValueGeneratedNever();

        // One width for both: an upload writes the same file name into each of them, so a narrower
        // display name would be a value the row accepts in one column and rejects in the other.
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.OriginalFileName).HasMaxLength(255);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Kind).HasConversion<short>();

        // Sources die with their cave (purge path); the archived document survives, because it is
        // a document like any other and may be filed, shared or attached somewhere else as well.
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CaveFeatureId);

        // One row per archived document: a source is a thread of revisions, and a second row
        // pointing at the same document would be the same source listed twice.
        builder.HasIndex(x => x.DocumentId).IsUnique();
    }
}
