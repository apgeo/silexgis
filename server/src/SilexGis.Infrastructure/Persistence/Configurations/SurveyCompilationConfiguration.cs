// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurveyCompilationConfiguration : IEntityTypeConfiguration<SurveyCompilation>
{
    public void Configure(EntityTypeBuilder<SurveyCompilation> builder)
    {
        builder.ToTable("survey_compilations");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status).HasConversion<short>();
        builder.Property(x => x.Outcome).HasConversion<short>();

        // Widths for text this application did not write: every one of these comes out of a file
        // somebody uploaded, so the handler shortens a value that would not fit rather than letting
        // the save fail — a refused row would lose the whole reading over a long version string.
        builder.Property(x => x.CompilerVersion).HasMaxLength(200);
        builder.Property(x => x.CompilerReleaseDate).HasMaxLength(100);
        builder.Property(x => x.IncompleteStage).HasMaxLength(200);
        builder.Property(x => x.ReadError).HasMaxLength(1000);

        // The record dies with the archived log it was read from, and with the cave that owns both.
        // No foreign key to the stored file: that column is provenance and has to keep answering
        // which bytes were read after the file itself is gone.
        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveFeatureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SurveySource>().WithMany().HasForeignKey(x => x.SurveySourceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Loops)
            .WithOne()
            .HasForeignKey(x => x.SurveyCompilationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CaveFeatureId);

        // One reading per archived log. A log read a second time replaces the figures of the read
        // before it rather than standing beside them: two rows for one file would be two answers to
        // "how well does this survey close" with nothing saying which is current.
        builder.HasIndex(x => x.SurveySourceId).IsUnique();
    }
}

public sealed class SurveyCompilationLoopConfiguration : IEntityTypeConfiguration<SurveyCompilationLoop>
{
    public void Configure(EntityTypeBuilder<SurveyCompilationLoop> builder)
    {
        builder.ToTable("survey_compilation_loops");
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Deliberately unbounded: the station chain is the compiler's own text, one entry per
        // station in the loop, and a large loop prints a long line. Capping it would refuse a row
        // for being about a big cave.
        builder.Property(x => x.Stations);

        // Read whole and in the compiler's order, which is what the ordinal preserves.
        builder.HasIndex(x => new { x.SurveyCompilationId, x.Ordinal }).IsUnique();
    }
}
