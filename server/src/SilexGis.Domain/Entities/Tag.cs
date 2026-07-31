// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Free-form label shared across the installation (name unique, slug for URLs).</summary>
public class Tag : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>Canonical slug: lower-case, diacritics stripped, non-alphanumerics → '-'.</summary>
    public static string Slugify(string name)
    {
        var normalized = name.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        var lastDash = true; // suppress leading dashes
        foreach (var c in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue; // strip combining diacritics
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastDash = false;
            }
            else if (!lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}

/// <summary>
/// Tag assignment; unique per (tag, target). The target is EITHER a feature (real FK)
/// OR a non-feature entity via the polymorphic pair — exactly one shape set
/// (CHECK-enforced), same convention as <see cref="Attachment"/>. Visibility of a
/// tagging follows the tagged entity — tags themselves are installation-public.
/// </summary>
public class Tagging : IAuditable, IAuditChild
{
    public long Id { get; set; }

    public long TagId { get; set; }

    /// <summary>Feature target (XOR with the polymorphic pair).</summary>
    public Guid? FeatureId { get; set; }

    public AttachedEntityType? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public Guid? AddedBy { get; set; }

    public string AuditId => Id.ToString();

    // A tagging surfaces in the timeline of whatever it tags.
    public string RootEntityType =>
        FeatureId is not null ? nameof(Feature) : AttachedEntityTypes.ClrName(EntityType!.Value);

    public string RootEntityId => (FeatureId ?? EntityId!.Value).ToString();
}
