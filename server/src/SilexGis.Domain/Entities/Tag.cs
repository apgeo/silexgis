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
/// Polymorphic tag assignment; unique per (tag, entity). Visibility of a tagging
/// follows the tagged entity — tags themselves are installation-public.
/// </summary>
public class Tagging
{
    public long Id { get; set; }

    public long TagId { get; set; }

    public AttachedEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid? AddedBy { get; set; }
}
