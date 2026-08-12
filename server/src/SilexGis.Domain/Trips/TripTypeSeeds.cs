// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>One shipped trip-type row: the code installations exchange it by, and the
/// English name a client without wording of its own falls back to.</summary>
public readonly record struct TripTypeSeed(string Code, string Name);

/// <summary>
/// The shipped trip-purpose vocabulary — the single home for which codes ship. The startup
/// seeder inserts these rows, and the admin surface consults the same list to refuse code
/// changes and deletion on them: shipped codes are what clients translate labels by and what
/// installations exchange records under, so a renamed or deleted code would silently break
/// both. Rows a club adds are installation-local and fully editable, and are shown under the
/// name whoever added them wrote.
/// </summary>
public static class TripTypeSeeds
{
    /// <summary>
    /// Append only. The seeder derives a row's sort order from its position here and never
    /// re-sorts a row that already exists, so a code inserted in the middle takes a different
    /// sort order on a fresh installation than on one being upgraded — silently, and only the
    /// vocabulary's display order gives it away.
    /// </summary>
    public static readonly IReadOnlyList<TripTypeSeed> All =
    [
        new("exploration", "Exploration"),
        new("survey", "Survey / mapping"),
        new("maintenance", "Maintenance / rigging"),
        new("training", "Training"),
        new("tourism", "Tourism / visit"),
        new("rescue", "Rescue"),
        new("science", "Science"),
        new("other", "Other"),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped row (code immutable, row undeletable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
