// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Survey file format of a 3D cave model. Stored as smallint.</summary>
public enum SurveyModelFormat : short
{
    /// <summary>Therion Loch export (.lox).</summary>
    Lox = 0,

    /// <summary>Survex image file (.3d).</summary>
    Survex3d = 1,
}

/// <summary>
/// A 3D cave survey model (Therion .lox / Survex .3d) rendered client-side by the
/// embedded viewer. Belongs to exactly one cave and inherits its access control — no own
/// RLS columns. The file carries absolute georeferenced coordinates, so for
/// location-protected caves the whole record (and its file URL) is withheld from callers
/// without the exact-location permission.
/// </summary>
public class SurveyModel : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaveId { get; set; }

    public required string Name { get; set; }

    public Guid FileId { get; set; }

    public SurveyModelFormat Format { get; set; }

    public string? Description { get; set; }

    public DateOnly? SurveyedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
