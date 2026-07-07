// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>created_at/updated_at maintained by a SaveChanges interceptor (02-data-model.md conventions).</summary>
public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}
