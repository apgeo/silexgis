// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>A caving club, informal group or organization that can own objects.</summary>
public class CavingGroup : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public CavingGroupType Type { get; set; } = CavingGroupType.CavingClub;

    public string? Description { get; set; }

    public string? Website { get; set; }

    public Guid? LogoFileId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
