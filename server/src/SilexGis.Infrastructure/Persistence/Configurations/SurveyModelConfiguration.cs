// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurveyModelConfiguration : IEntityTypeConfiguration<SurveyModel>
{
    public void Configure(EntityTypeBuilder<SurveyModel> builder)
    {
        builder.ToTable("survey_models");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Format).HasConversion<short>();
        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.ProcessingError).HasMaxLength(1000);
        builder.Property(x => x.Anchor).HasColumnType("geometry(Point, 4326)");

        // Models die with their cave (purge path); the stored file survives.
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany()
            .HasForeignKey(x => x.ConvertedFileId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CaveFeatureId);
    }
}
