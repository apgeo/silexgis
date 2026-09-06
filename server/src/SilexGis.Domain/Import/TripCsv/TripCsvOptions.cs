// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TripCsv;

/// <summary>Which of a numeric date's first two components is the day.</summary>
public enum TripCsvDateOrder
{
    /// <summary>day/month/year, the convention where these sheets are written.</summary>
    DayFirst,

    /// <summary>month/day/year.</summary>
    MonthFirst,
}

/// <summary>What settled the day/month order actually used.</summary>
public enum TripCsvDateOrderSource
{
    /// <summary>Nothing in the file could settle it; the stated preference stands.</summary>
    Stated,

    /// <summary>A component above twelve somewhere in the file settled it.</summary>
    File,

    /// <summary>The file contains evidence for both orders; the stated preference stands.</summary>
    Conflict,
}

/// <summary>
/// Everything about how a trip spreadsheet is read that is not the mapping. All of it is
/// deliberately explicit — the parser sniffs nothing.
/// </summary>
public sealed record TripCsvOptions
{
    /// <summary>
    /// The field separator, pinned rather than detected. Detection scores candidate characters
    /// by how often they occur, so one free-text column full of semicolons flips the reading of
    /// every row in the file and the result still looks like a successful parse.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// The characters that separate several values inside one cell. A forward slash is not one
    /// of them: it is an ordinary character in <c>n/a</c>, in a name, and in a date somebody
    /// pasted into the wrong column, and treating it as a separator quietly turns each of those
    /// into two values.
    /// </summary>
    public IReadOnlyList<char> MultiValueSeparators { get; init; } = [';', ','];

    /// <summary>
    /// Columns where a forward slash also separates values. Opt-in per column, because a cave
    /// column really does get written <c>P. Ialomitei / P. Ursilor</c> while a massif column is
    /// as likely to hold <c>Piatra Craiului/Bucegi</c> meaning one place.
    /// </summary>
    public IReadOnlySet<TripCsvField> SlashSeparatedFields { get; init; } = new HashSet<TripCsvField>();

    /// <summary>
    /// Values that mean "nothing here". Compared folded, against the whole cell before it is
    /// split and against each value after. Checking only the pieces is how <c>n/a</c> becomes two
    /// people in a column where a slash separates values.
    /// </summary>
    public IReadOnlySet<string> SkipTokens { get; init; } =
        new HashSet<string>(StringComparer.Ordinal) { "-", "--", "---", "n/a", "na", "?", "??", "tbd", "x" };

    /// <summary>The day/month order to use where the file itself cannot settle it.</summary>
    public TripCsvDateOrder DateOrder { get; init; } = TripCsvDateOrder.DayFirst;

    /// <summary>
    /// Fields a row cannot be missing. A row missing one is still returned, carrying the
    /// diagnostic that says so, so a reviewer sees why it cannot be imported.
    /// </summary>
    public IReadOnlySet<TripCsvField> RequiredFields { get; init; } =
        new HashSet<TripCsvField> { TripCsvField.Title, TripCsvField.StartDate };

    /// <summary>Guard on a hand-made file: a header this wide is a parse gone wrong, not data.</summary>
    public int MaxColumns { get; init; } = 512;

    public static TripCsvOptions Default { get; } = new();
}
