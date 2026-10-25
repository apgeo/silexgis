// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Surveys;

/// <summary>
/// The outputs a survey upload's run can be asked to produce, and which ones a given upload
/// produces.
/// </summary>
/// <remarks>
/// <para>
/// What a run produces is the union of three lists: what the upload asked for (or the
/// installation's defaults when it asked for nothing), what the uploader's account always wants,
/// and what the installation always wants. A union rather than an override, so neither the
/// account holder nor the administrator can be overruled by a phone's settings, and a phone that
/// asks for little still gets what the people who set the installation up want kept.
/// </para>
/// <para>
/// Names are the catalogue's, compared without regard to case and written lower-case. A request
/// naming something outside the catalogue is refused by the caller (see <see cref="Unknown"/>);
/// a stored preference or a setting naming one has it ignored instead, so a catalogue that loses a
/// member never breaks anybody's uploads.
/// </para>
/// </remarks>
public static class SurveyOutputs
{
    /// <summary>Every output a run can be asked for, in the order they are listed and produced.</summary>
    public static readonly IReadOnlyList<string> Catalogue =
    [
        "lox", "3d", "plan-pdf", "extended-pdf", "plan-svg", "extended-svg", "kml", "dxf", "sql",
        "csv", "survey-list-html", "continuation-list-html", "shp",
    ];

    /// <summary>What a run produces when the upload asks for nothing and nothing is configured.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["lox", "3d", "plan-pdf", "extended-pdf"];

    /// <summary>The most outputs one list may name — the whole catalogue.</summary>
    public static int MaxCount => Catalogue.Count;

    public static bool IsKnown(string? name) =>
        name is not null && Catalogue.Contains(name.Trim().ToLowerInvariant());

    /// <summary>The names in <paramref name="names"/> the catalogue does not have, as written.</summary>
    public static IReadOnlyList<string> Unknown(IEnumerable<string?> names) =>
        names.Where(n => !IsKnown(n)).Select(n => n ?? "").Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// A comma-separated setting read as a list: blanks dropped, names trimmed and lower-cased.
    /// Unknown names are kept here — <see cref="Effective"/> drops them — so a setting can be
    /// reported back as it was written.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? commaSeparated) =>
        (commaSeparated ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The outputs one upload's run produces: <paramref name="requested"/> (or
    /// <paramref name="defaults"/> when it names none) ∪ <paramref name="account"/> ∪
    /// <paramref name="installation"/>, each named once, catalogue members only, in catalogue order.
    /// </summary>
    public static IReadOnlyList<string> Effective(
        IReadOnlyCollection<string>? requested,
        IEnumerable<string> defaults,
        IEnumerable<string> account,
        IEnumerable<string> installation)
    {
        var asked = requested is { Count: > 0 } ? requested : defaults;
        var wanted = asked.Concat(account).Concat(installation)
            .Select(n => n.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        return Catalogue.Where(wanted.Contains).ToList();
    }
}
