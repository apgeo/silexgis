// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// How a sheet of tracking reports is to be read: its shape, not what its rows mean.
/// </summary>
/// <remarks>
/// Deliberately the same choices, spelled the same way, as the trip sheet's options — a club
/// bringing both files should not have to learn two vocabularies for "which character separates
/// the columns".
/// </remarks>
public sealed record TrackingCsvOptions
{
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Characters that separate several people in one cell. A row is one moment for a party, so
    /// the caver column is the one column that routinely carries more than one value.
    /// </summary>
    public IReadOnlyList<char> MultiValueSeparators { get; init; } = [';', ','];

    /// <summary>
    /// Which of a date's leading numbers is the day, where the numbers do not settle it.
    /// </summary>
    /// <remarks>
    /// A starting point rather than the last word: the parser reads the whole moment column first
    /// and lets the file's own evidence overrule this, because one row cannot settle a question
    /// that 5/11 answers either way.
    /// </remarks>
    public TripCsvDateOrder DateOrder { get; init; } = TripCsvDateOrder.DayFirst;

    /// <summary>Cell contents that mean "nothing was written here".</summary>
    public IReadOnlySet<string> SkipTokens { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "-", "--", "n/a", "na", "?" };

    /// <summary>The words this sheet writes "went in" and "came out" in.</summary>
    public TrackingCsvStateWords StateWords { get; init; } = TrackingCsvStateWords.Default;

    /// <summary>A bound on the width of a sheet, so a mangled file is refused rather than folded.</summary>
    public int MaxColumns { get; init; } = 512;

    public static TrackingCsvOptions Default { get; } = new();
}
