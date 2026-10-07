// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// The one place that knows the survey viewer spells a station differently from the rows read out
/// of the same file, and the only place allowed to convert between the two.
///
/// <para>
/// <b>What diverges, and why.</b> A Therion <c>.lox</c> file carries an explicit survey tree whose
/// root survey is its own parent. Reading the file into rows qualifies every station with the full
/// survey path it sits under, so the root survey's own name becomes the first component of the
/// stored name: a station <c>1</c> of survey <c>entrance</c> under a root called <c>cave</c> is
/// stored as <c>cave.entrance.1</c>. The viewer's reader of the same format deliberately skips the
/// root survey when it builds its tree — a survey that is its own parent is not added as a node —
/// so the same station is addressed there as <c>entrance.1</c>. A Survex <c>.3d</c> file has no
/// survey tree at all: each station's label already carries its whole path, both sides take it as
/// it is, and the two spellings are the same string.
/// </para>
///
/// <para>
/// So the difference is exactly one leading component, present only for <c>.lox</c>, and only where
/// the root survey has a name of its own — an unnamed root contributes nothing to either spelling,
/// and a file whose root is unnamed already agrees with the viewer. That makes the conversion a
/// prefix, and its inverse the same prefix, which is why both directions are stated here from one
/// rule rather than derived twice. Two derivations that drifted apart is what this exists to stop
/// happening again.
/// </para>
///
/// <para>
/// <b>Measured, and the measurement says this is latent rather than something anybody has seen.</b>
/// Every compiled Therion file available when this was written — eighteen of them, from several
/// unrelated surveying projects, two to three hundred and thirty-nine surveys each — carries exactly
/// one root survey, and in every one of them that root is <em>unnamed</em>. The compiler appears to
/// write a synthetic root and put the named surveys under it. On all of those files the prefix
/// below is therefore empty, the two spellings already agree, and this converts nothing. It is kept
/// because the format plainly permits a named root, because the reader of it here and the viewer's
/// reader of it disagree about that case in writing, and because the cost of being right about it
/// is one string comparison. What it must <em>not</em> be taken for is an explanation of an
/// observed defect: a party that does not appear on a compiled Therion model is, on the evidence
/// above, something else, and looking here for it will waste the next reader's afternoon.
/// </para>
///
/// <para>
/// <b>The separator is a full stop on both sides and is not a variable.</b> The reader of the
/// compiled Therion format never varies it, and the viewer's tree joins path components with a full
/// stop unconditionally, whatever the file said. Taking it from the model would therefore be
/// inventing a degree of freedom neither side has.
/// </para>
///
/// <para>
/// <b>A station the file gives no name at all is stored under the label the viewer gives it.</b>
/// Both sides have to call such a station something, and each falls back on the number the file
/// wrote it at. For a compiled Therion file the viewer writes that number in square brackets, so
/// the rows store it in square brackets too — the pressed station and the stored station are then
/// one string and nothing has to be converted. A Survex file is the other case: the viewer leaves
/// a station without a label out of its tree altogether, so there is no label to share and the row
/// keeps the number behind a hash sign, a spelling nobody can press. <see cref="IsNameless"/> says
/// which rows these are and <see cref="HasNoViewerLabel"/> which of them the viewer cannot draw.
/// </para>
///
/// <para>
/// <b>That number is the file's, not the cave's.</b> It is stable for as long as the model is the
/// same file, which is all a position reported on that model needs. It is reassigned by the next
/// export, so nothing that outlives a model — a place declared for the cave as a whole — may be
/// pinned to it.
/// </para>
/// </summary>
public static class SurveyStationNames
{
    /// <summary>
    /// What separates the components of a station path, on both sides of the conversion.
    /// </summary>
    public const char Separator = '.';

    /// <summary>
    /// Whether <paramref name="leafName"/> is a compiled survey's way of saying <em>there is no
    /// station here</em>, rather than the name of one.
    ///
    /// <para>
    /// <b>What the two spellings are.</b> The survey language writes a lone <c>-</c> or a lone
    /// <c>.</c> in a station column to fire a shot at the passage wall from a station without
    /// naming the far end, because the far end is a point on the rock and not a place anybody
    /// will come back to. The compiler keeps that token as a station record whose name is literally
    /// that one character, so the shot has two endpoints to reference. It is a placeholder, and it
    /// is emitted once per wall shot: a survey with four hundred wall shots has four hundred of
    /// them, all spelled the same.
    /// </para>
    ///
    /// <para>
    /// <b>Why this is a naming rule and not a statistic.</b> The two characters are the format's
    /// own vocabulary, fixed by the language that writes them, so recognising them is reading the
    /// file rather than guessing at the surveyor's habits. The flag that would otherwise say the
    /// same thing cannot be relied on: of eighteen compiled files measured, six — including the
    /// public demo survey of the viewer this application embeds — set the wall-shot flag on no leg
    /// at all while carrying tens of thousands of these placeholders.
    /// </para>
    ///
    /// <para>
    /// <b>Exactly these two, and only whole.</b> Measured over those eighteen files: every leaf name
    /// containing no letter or digit at all is exactly one of these two characters — there is no
    /// third placeholder to miss, and nothing longer to over-match. The comparison is deliberately
    /// against the whole name and not a prefix, because real station names beginning with the same
    /// punctuation exist and are ordinary names.
    /// </para>
    ///
    /// <para>
    /// <b>This is the leaf name, before the survey path is put in front of it.</b> A qualified name
    /// ends in the placeholder rather than being it, and every wall shot of one survey qualifies to
    /// the same string — which is how a file full of them used to arrive at the station table as
    /// one name repeated thousands of times.
    /// </para>
    /// </summary>
    public static bool IsAnonymousPoint(string? leafName) => leafName is "-" or ".";

    /// <summary>
    /// What a station the file gives no name is called in the rows of a model of
    /// <paramref name="format"/>: the number the file wrote it at, in the viewer's own punctuation
    /// where the viewer labels such a station at all, and behind a hash sign where it does not.
    /// This is the last component of the stored name; the survey path goes in front of it as for
    /// any other station.
    /// </summary>
    public static string NamelessLeaf(SurveyModelFormat format, long fileStationId) =>
        format == SurveyModelFormat.Lox ? ViewerNamelessLeaf(fileStationId) : UnlabelledLeaf(fileStationId);

    /// <summary>
    /// Whether the station a row calls <paramref name="storedName"/> has no name of its own — what
    /// identifies it is only the number the file wrote it at.
    ///
    /// <para>
    /// <b>Exact, and asked of the row's own number.</b> The name has to be precisely the fallback
    /// for this very station: a surveyor may well call a station <c>#3</c> or <c>[3]</c>, and that
    /// is an ordinary name unless the file also happens to have written that station third. A
    /// test on how the name starts, or on its shape alone, would take those names away from the
    /// people who chose them.
    /// </para>
    ///
    /// <para>
    /// A compiled Therion model answers yes under either punctuation, because a model read before
    /// its rows took the viewer's label holds the same station behind a hash sign until it is read
    /// again. A Survex label is already the whole name, so there the whole name is compared; a
    /// Therion station is named within its survey, so there it is the last component.
    /// </para>
    /// </summary>
    /// <param name="fileStationId">The number the file gives the station; a row without one has a name.</param>
    public static bool IsNameless(SurveyModelFormat format, string storedName, long? fileStationId)
    {
        ArgumentNullException.ThrowIfNull(storedName);

        if (fileStationId is not { } id)
        {
            return false;
        }

        return format == SurveyModelFormat.Lox
            ? EndsInLeaf(storedName, ViewerNamelessLeaf(id)) || EndsInLeaf(storedName, UnlabelledLeaf(id))
            : string.Equals(storedName, UnlabelledLeaf(id), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the station is a nameless one the viewer has no label for, and so one that can be
    /// neither pressed nor drawn: every nameless station of a Survex model, and a nameless station
    /// of a Therion model whose rows were read before they took the viewer's label.
    ///
    /// <para>
    /// Such a row is real — the survey's legs run through it and its altitude is as good as any —
    /// but a position stamped with its name is a marker that never appears, so nothing that
    /// chooses a station on somebody's behalf may choose it.
    /// </para>
    /// </summary>
    public static bool HasNoViewerLabel(SurveyModelFormat format, string storedName, long? fileStationId)
    {
        ArgumentNullException.ThrowIfNull(storedName);

        return fileStationId is { } id
            && (format == SurveyModelFormat.Lox
                ? EndsInLeaf(storedName, UnlabelledLeaf(id))
                : string.Equals(storedName, UnlabelledLeaf(id), StringComparison.Ordinal));
    }

    /// <summary>
    /// Where the rows of a model read before nameless stations took the viewer's label would hold
    /// the station the viewer addresses as <paramref name="given"/> — or null when
    /// <paramref name="given"/> is not the viewer's label for a nameless station at all.
    ///
    /// <para>
    /// This exists to tell two refusals apart. A pressed station the model has no row for is
    /// ordinarily a name from some other survey, and the remedy is to pick a station that is
    /// there. A pressed nameless station whose row is still spelled the earlier way is a station
    /// the model does have, and the remedy is to read the survey again — which nobody would guess
    /// from being told the station is unknown. The caller looks for a row under one of the names
    /// returned whose number is the one returned; only that is the earlier spelling of this
    /// station rather than a coincidence of names.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<string> StoredNames, long FileStationId)? EarlierNamelessSpelling(
        SurveyModelFormat format, string? rootSurveyName, string given)
    {
        ArgumentNullException.ThrowIfNull(given);

        if (format != SurveyModelFormat.Lox)
        {
            return null;
        }

        var leafStart = given.LastIndexOf(Separator) + 1;
        var leaf = given.AsSpan(leafStart);
        if (leaf.Length < 3 || leaf[0] != '[' || leaf[^1] != ']')
        {
            return null;
        }

        // Digits and nothing else, and no leading zero: the label is the number as the viewer
        // prints it, and "[007]" or "[+7]" is a name somebody chose, not that label.
        var digits = leaf[1..^1];
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !digits.SequenceEqual(id.ToString(CultureInfo.InvariantCulture)))
        {
            return null;
        }

        var earlier = string.Concat(given.AsSpan(0, leafStart), UnlabelledLeaf(id));
        return (StoredCandidates(format, rootSurveyName, earlier), id);
    }

    /// <summary>
    /// How the viewer addresses the station a row calls <paramref name="storedName"/>.
    ///
    /// <para>
    /// A stored name that does not carry the root survey's prefix is returned unchanged rather than
    /// cut: a station whose survey the file failed to place has no survey path at all, and both
    /// sides then call it by its bare name. Cutting on the first separator regardless would rename
    /// those, and would rename every station of a <c>.3d</c> model, whose names are already whole.
    /// </para>
    /// </summary>
    /// <param name="format">The format the model was read from.</param>
    /// <param name="rootSurveyName">
    /// The name of the file's root survey, or null/empty where it has none — see
    /// <see cref="SurveyModel.RootSurveyName"/>.
    /// </param>
    /// <param name="storedName">The station's name as the survey rows hold it.</param>
    public static string ViewerName(SurveyModelFormat format, string? rootSurveyName, string storedName)
    {
        ArgumentNullException.ThrowIfNull(storedName);

        var prefix = RootPrefix(format, rootSurveyName);

        // Longer than the prefix, not merely starting with it: a stored name that is the root
        // survey's name and nothing else names no station, and cutting it would answer with the
        // empty string — a name the viewer can never resolve and nothing could ever be told apart.
        return prefix is not null
            && storedName.Length > prefix.Length
            && storedName.StartsWith(prefix, StringComparison.Ordinal)
                ? storedName[prefix.Length..]
                : storedName;
    }

    /// <summary>
    /// What the survey rows would call the station the viewer addresses as
    /// <paramref name="viewerName"/>.
    ///
    /// <para>
    /// The inverse of <see cref="ViewerName"/> and the same rule read backwards, so a caller that
    /// holds a name from the viewer can look for its row with one equality rather than by converting
    /// every row in the model. It is a candidate and not an assertion: it says what the row would be
    /// called <em>if</em> this name came from the viewer, and whether such a row exists is the
    /// caller's question to ask of the model.
    /// </para>
    /// </summary>
    public static string StoredName(SurveyModelFormat format, string? rootSurveyName, string viewerName)
    {
        ArgumentNullException.ThrowIfNull(viewerName);

        var prefix = RootPrefix(format, rootSurveyName);
        return prefix is null ? viewerName : prefix + viewerName;
    }

    /// <summary>
    /// The rows this model could hold for a station somebody named <paramref name="given"/>, best
    /// reading first — the whole of the rule for accepting a name from either side.
    ///
    /// <para>
    /// <b>Both spellings are accepted, and the viewer's is tried first.</b> A name pressed on the
    /// model arrives in the viewer's words; a name typed from a survey's own listing, or carried in
    /// by an import, arrives in the words the rows use. Neither is a mistake anybody made, and
    /// refusing one of them would refuse a station that demonstrably exists — a refusal with
    /// nothing the caller could do about it. The two readings can only name different rows of one
    /// model if a sub-survey is called exactly what the root is called, and in that case the
    /// viewer's reading wins, because what is stored afterwards is what the viewer will be asked to
    /// draw.
    /// </para>
    ///
    /// <para>
    /// One ordered list rather than a match function, because callers hold their model's names in
    /// different shapes — a set in memory here, a table to be queried there — and the thing that
    /// must not be written twice is the order, not the lookup.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> StoredCandidates(
        SurveyModelFormat format, string? rootSurveyName, string given)
    {
        ArgumentNullException.ThrowIfNull(given);

        var asStored = StoredName(format, rootSurveyName, given);
        return asStored == given ? [given] : [asStored, given];
    }

    /// <summary>
    /// How the viewer addresses the station <paramref name="given"/> names, or null when
    /// <paramref name="modelHolds"/> says the model has no row under either reading of it.
    /// </summary>
    /// <param name="modelHolds">Whether the model holds a station row of exactly that name.</param>
    public static string? ViewerNameOfMatch(
        SurveyModelFormat format, string? rootSurveyName, string given, Func<string, bool> modelHolds)
    {
        ArgumentNullException.ThrowIfNull(modelHolds);

        foreach (var candidate in StoredCandidates(format, rootSurveyName, given))
        {
            if (modelHolds(candidate))
            {
                return ViewerName(format, rootSurveyName, candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// The leading component the two spellings differ by, separator included, or null where they do
    /// not differ at all. Every rule above is this one answer read in one of two directions.
    /// </summary>
    private static string? RootPrefix(SurveyModelFormat format, string? rootSurveyName) =>
        format == SurveyModelFormat.Lox && !string.IsNullOrEmpty(rootSurveyName)
            ? rootSurveyName + Separator
            : null;

    /// <summary>The viewer's label for a station of a compiled Therion file that has no name.</summary>
    private static string ViewerNamelessLeaf(long fileStationId) =>
        "[" + fileStationId.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>
    /// The number alone, behind a hash sign: what a nameless station is called where the viewer
    /// has no label to share.
    /// </summary>
    private static string UnlabelledLeaf(long fileStationId) =>
        "#" + fileStationId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether <paramref name="storedName"/> is <paramref name="leaf"/> or ends in it as a whole
    /// last component.
    /// </summary>
    private static bool EndsInLeaf(string storedName, string leaf) =>
        storedName.Length == leaf.Length
            ? string.Equals(storedName, leaf, StringComparison.Ordinal)
            : storedName.Length > leaf.Length
                && storedName[storedName.Length - leaf.Length - 1] == Separator
                && storedName.EndsWith(leaf, StringComparison.Ordinal);
}
