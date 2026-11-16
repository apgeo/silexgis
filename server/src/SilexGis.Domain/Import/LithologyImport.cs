// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Import;

/// <summary>How a source value found its lithology class, or that it found none.</summary>
public enum LithologyMatchKind
{
    /// <summary>The reviewer said which class this value is.</summary>
    Mapped = 0,

    /// <summary>The value is a class's code or one of its common names, in English or Romanian.</summary>
    Recognised = 1,

    /// <summary>Nothing says which class this value is: the unit is created unclassified.</summary>
    Unmatched = 2,
}

/// <summary>What one source value of the lithology column becomes.</summary>
/// <param name="Code">The vocabulary code, or null when the value matched nothing.</param>
public sealed record LithologyMatch(string? Code, LithologyMatchKind Kind);

/// <summary>
/// Turns the rock-type column of an imported geology map into the lithology vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// A geology map's legend is written in whatever words its author used — "Calcare", "LIMESTONE",
/// "gresii", "Jurassic limestone with chert". The reviewer's own mapping is read first, because it
/// is an instruction. Without one, a value is recognised only when the whole of it, folded (case
/// and diacritics ignored, punctuation read as a space), is a class's code or one of its plain
/// names. Nothing is guessed from part of a value: "limestone with chert" could be mapped as
/// limestone by somebody who knows the map, and must not be by a matcher that does not, because
/// a wrong karstifiable class moves every outcrop-normalised density computed over that ground.
/// </para>
/// <para>
/// "Other" is recognised only by its code. A value nobody recognised is not "other" rock; it is
/// rock nobody has named yet, and is listed back to the reviewer as unmatched.
/// </para>
/// </remarks>
public static class LithologyImport
{
    private static readonly Dictionary<string, string[]> NamesByCode = new(StringComparer.Ordinal)
    {
        ["limestone"] = ["limestone", "limestones", "calcar", "calcare"],
        ["dolomite"] = ["dolomite", "dolomites", "dolomit", "dolomie", "dolomii"],
        ["marble"] = ["marble", "marbles", "marmura", "marmure"],
        ["gypsum_anhydrite"] = ["gypsum", "anhydrite", "gypsum anhydrite", "gips", "gipsuri", "anhidrit", "gips anhidrit"],
        ["salt"] = ["salt", "rock salt", "halite", "sare", "sare gema"],
        ["conglomerate_calcareous"] = ["calcareous conglomerate", "conglomerate calcareous", "conglomerat calcaros", "conglomerate calcaroase"],
        ["sandstone"] = ["sandstone", "sandstones", "gresie", "gresii"],
        ["shale_marl"] = ["shale", "marl", "shale marl", "marna", "marne", "sist argilos", "sist argilos marna"],
        ["granite"] = ["granite", "granit"],
        ["crystalline_schist"] = ["crystalline schist", "crystalline schists", "sist cristalin", "sisturi cristaline"],
        ["volcanic"] = ["volcanic", "volcanic rock", "volcanics", "roca vulcanica", "roci vulcanice"],
        ["unconsolidated"] = ["unconsolidated", "unconsolidated deposits", "alluvium", "depozite neconsolidate", "aluviuni"],
        ["other"] = [],
    };

    private static readonly Dictionary<string, string> CodeByName = BuildIndex();

    /// <summary>
    /// The class one source value becomes: the reviewer's mapping when it names this value,
    /// otherwise a whole-value recognition, otherwise nothing.
    /// </summary>
    /// <param name="value">The value as the file wrote it; null or blank matches nothing.</param>
    /// <param name="mapped">
    /// The reviewer's mapping, source value to vocabulary code. Keys are compared folded, so a
    /// mapping written for "Calcar" also answers for "CALCAR". A code outside the vocabulary is
    /// not a match: the request that carried it is refused before it gets here.
    /// </param>
    public static LithologyMatch Resolve(string? value, IReadOnlyDictionary<string, string>? mapped)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new LithologyMatch(null, LithologyMatchKind.Unmatched);
        }

        var key = Fold(value);
        if (mapped is not null)
        {
            foreach (var (source, code) in mapped)
            {
                if (Fold(source) == key && Lithology.Find(code) is not null)
                {
                    return new LithologyMatch(code, LithologyMatchKind.Mapped);
                }
            }
        }

        return CodeByName.TryGetValue(key, out var recognised)
            ? new LithologyMatch(recognised, LithologyMatchKind.Recognised)
            : new LithologyMatch(null, LithologyMatchKind.Unmatched);
    }

    /// <summary>
    /// A value folded for comparison: lower case, diacritics removed, every run of characters that
    /// is not a letter or a digit read as one space. "Gips / anhidrit" and "gips-anhidrit" fold
    /// alike, and so do the code "gypsum_anhydrite" and the words "gypsum anhydrite".
    /// </summary>
    public static string Fold(string value)
    {
        var folded = FoldedText.Of(value).Value;
        var builder = new StringBuilder(folded.Length);
        var space = false;
        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (space && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(ch);
                space = false;
            }
            else
            {
                space = true;
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> BuildIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cls in Lithology.Classes)
        {
            index.TryAdd(Fold(cls.Code), cls.Code);
            foreach (var name in NamesByCode.GetValueOrDefault(cls.Code) ?? [])
            {
                index.TryAdd(Fold(name), cls.Code);
            }
        }

        return index;
    }
}
