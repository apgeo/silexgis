// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Catalogue;

/// <summary>
/// One hydrographic basin of the Romanian catalogue.
/// </summary>
/// <param name="Id">The catalogue's identifier — what a cave record's <c>bazinHidroId</c> points at.</param>
/// <param name="ParentId">The basin this one sits inside, or null at the top of the tree.</param>
/// <param name="Name">The name exactly as the catalogue writes it, code and all.</param>
/// <param name="Code">The cadastral code the name begins with, when it begins with one.</param>
/// <param name="Label">The name without that code — what a person would call the place.</param>
/// <param name="Depth">How far down the tree it sits; the mountain groups are 1.</param>
public sealed record SpeologieBasin(
    int Id,
    int? ParentId,
    string Name,
    string? Code,
    string Label,
    int Depth);

/// <summary>
/// Turns a cave record's basin identifier into something a person can read.
///
/// <para>
/// The catalogue's programmatic interface answers <c>bazinHidroId: 605</c> and offers no way at all
/// to find out what 605 is — so without this every imported cave would carry an integer nobody can
/// act on, and the cave's basin field would stay empty while the information was sitting in public
/// on the catalogue's own site. The tree is carried in
/// <see cref="SpeologieBasinData"/> and refreshed by a script.
/// </para>
/// <para>
/// The tree is worth more than the single name. 605 is not merely "Bazinul Padiş": it is
/// <c>Munţii Apuseni › Munţii Bihorului › Bazinele închise şi platourile înalte › Bazinul Padiş</c>,
/// which places a cave in the country for somebody who has never heard of the basin — and which is
/// what makes choosing a massif and getting every basin under it a useful thing to offer.
/// </para>
/// </summary>
public static partial class SpeologieBasins
{
    private static readonly Lazy<IReadOnlyDictionary<int, SpeologieBasin>> ByIdLazy = new(Build);

    private static readonly Lazy<IReadOnlyList<SpeologieBasin>> AllLazy = new(() =>
        [.. ByIdLazy.Value.Values.OrderBy(b => b.Depth).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)]);

    /// <summary>Every basin, by identifier.</summary>
    public static IReadOnlyDictionary<int, SpeologieBasin> ById => ByIdLazy.Value;

    /// <summary>
    /// Every basin, outermost level first and alphabetical within a level — the order a picker
    /// wants, so the fifteen mountain groups are reachable before the four hundred valleys.
    /// </summary>
    public static IReadOnlyList<SpeologieBasin> All => AllLazy.Value;

    /// <summary>The basin with this identifier, or null when the catalogue names one this table does not hold.</summary>
    public static SpeologieBasin? Find(int? id) =>
        id is { } value && ById.TryGetValue(value, out var basin) ? basin : null;

    /// <summary>
    /// The basin and everything above it, outermost first — the sentence that places a cave.
    /// Empty when the identifier is unknown.
    /// </summary>
    public static IReadOnlyList<SpeologieBasin> Ancestry(int? id)
    {
        var basin = Find(id);
        if (basin is null)
        {
            return [];
        }

        var chain = new List<SpeologieBasin>();
        var seen = new HashSet<int>();

        while (basin is not null && seen.Add(basin.Id))
        {
            chain.Add(basin);
            basin = Find(basin.ParentId);
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// The readable path to a basin, outermost first, using each level's label rather than its
    /// coded name. Null when the identifier is unknown, so a caller can leave a field unset rather
    /// than write a sentence about nothing.
    /// </summary>
    public static string? PathOf(int? id, string separator = " › ")
    {
        var chain = Ancestry(id);
        return chain.Count == 0 ? null : string.Join(separator, chain.Select(b => b.Label));
    }

    /// <summary>
    /// Whether <paramref name="id"/> is <paramref name="rootId"/> or sits anywhere beneath it.
    ///
    /// <para>
    /// This is what makes the tree a filter rather than a list: choosing a massif and being shown
    /// only the one basin named after it, while every basin inside it is excluded, would be a
    /// filter that looks broken.
    /// </para>
    /// </summary>
    public static bool IsWithin(int? id, int rootId)
    {
        var basin = Find(id);
        var seen = new HashSet<int>();

        while (basin is not null && seen.Add(basin.Id))
        {
            if (basin.Id == rootId)
            {
                return true;
            }

            basin = Find(basin.ParentId);
        }

        return false;
    }

    private static IReadOnlyDictionary<int, SpeologieBasin> Build()
    {
        var raw = SpeologieBasinData.Rows.ToDictionary(r => r.Id);

        return raw.Values.ToDictionary(
            row => row.Id,
            row =>
            {
                var (code, label) = Split(row.Name);
                return new SpeologieBasin(row.Id, row.ParentId, row.Name, code, label, DepthOf(row.Id));
            });

        // Walked rather than recursed, with a visited set: this table is scraped from somebody
        // else's page, and a cycle in it should come out as a wrong depth rather than as a stack
        // overflow the first time a cave is imported.
        int DepthOf(int id)
        {
            var depth = 0;
            var seen = new HashSet<int>();
            int? cursor = id;

            while (cursor is { } value && raw.TryGetValue(value, out var row) && seen.Add(value))
            {
                depth++;
                cursor = row.ParentId;
            }

            return depth;
        }
    }

    /// <summary>
    /// Splits <c>"3440 - Bazinul Padiş"</c> into its cadastral code and its name.
    ///
    /// <para>
    /// Nearly every entry is written that way, and a handful are not — one is the bare number
    /// <c>3811</c>, another writes its code without the spaced dash. Anything the pattern does not
    /// recognise keeps its whole name as the label and reports no code, because a name shown in
    /// full is right and a name split in the wrong place is not.
    /// </para>
    /// </summary>
    private static (string? Code, string Label) Split(string name)
    {
        var match = CodedName().Match(name);

        return match.Success
            ? (match.Groups["code"].Value, match.Groups["label"].Value)
            : (null, name);
    }

    [GeneratedRegex(@"^(?<code>[0-9A-Za-z?\-]{1,10})\s+-\s+(?<label>\S.*)$", RegexOptions.ExplicitCapture)]
    private static partial Regex CodedName();
}
