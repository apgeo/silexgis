// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// One of a user's own addresses. The map point is optional — a user may give only the text, only
/// the point, or both.
/// </summary>
/// <remarks>
/// Deliberately not auditable. The audit interceptor writes a full old/new diff of every auditable
/// entity into a table that the admin audit page and the per-entity history endpoint expose, whose
/// only redaction rule covers cave coordinates — so auditing this would copy a home street and its
/// coordinates somewhere the profile-visibility rule does not reach. The accepted cost is that
/// address edits leave no history timeline.
/// </remarks>
public class UserAddress : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>What the user calls this address ("Home", "Cabin", "Parents").</summary>
    public required string Label { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    /// <summary>Street, number and everything else, as the user chooses to write it.</summary>
    public string? AddressText { get; set; }

    /// <summary>Optional point the user picked on a map (SRID 4326).</summary>
    public Point? Geom { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
