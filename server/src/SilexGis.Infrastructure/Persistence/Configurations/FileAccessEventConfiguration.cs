// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class FileAccessEventConfiguration : IEntityTypeConfiguration<FileAccessEvent>
{
    public void Configure(EntityTypeBuilder<FileAccessEvent> builder)
    {
        builder.ToTable("file_access_log");

        // No foreign keys. The history has to survive the things it is about: a superseded
        // revision's file is deletable, and a cascade would quietly erase the evidence that
        // somebody took a copy of it before it went.

        // "Who read this document", newest first — the only shape the per-document surface asks for.
        builder.HasIndex(x => new { x.DocumentId, x.At }).IsDescending(false, true);

        // "What did I read", newest first. Also what the de-duplication check rides: a
        // person's rows inside the last window are a handful of entries at the head of
        // their range, so the check costs an index probe rather than a scan of the file.
        builder.HasIndex(x => new { x.UserId, x.At }).IsDescending(false, true);

        // The retention sweep deletes by age alone and has no other predicate to lean on.
        builder.HasIndex(x => x.At);
    }
}
