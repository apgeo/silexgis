// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Trips;

/// <summary>What one line of a report template asks for.</summary>
public enum ReportTemplateDirective
{
    /// <summary>The document's own title.</summary>
    Title = 0,

    /// <summary>A part heading. A heading nothing came out under is left out of the document.</summary>
    Heading = 1,

    /// <summary>A paragraph of prose.</summary>
    Text = 2,

    /// <summary>A quieter aside.</summary>
    Note = 3,

    /// <summary>A labelled value on one line.</summary>
    Field = 4,

    /// <summary>One entry of a list.</summary>
    Bullet = 5,

    /// <summary>Everybody who was on the trip, one line each.</summary>
    Roster = 6,

    /// <summary>The pictures filed against the trip.</summary>
    Photographs = 7,

    /// <summary>Every answer recorded in one part of the trip's form.</summary>
    Section = 8,

    /// <summary>The trips a camp gathered, one line each.</summary>
    Trips = 9,

    /// <summary>What ran on each day of a camp, day by day.</summary>
    Days = 10,

    /// <summary>Who was at a camp in each role, one line per role.</summary>
    Teams = 11,
}

/// <summary>
/// Which kind of thing a layout writes up.
/// </summary>
/// <remarks>
/// One language, two vocabularies. A camp is not a long trip: a fortnight has a day-by-day shape
/// and a set of teams, and neither has anything a single afternoon underground could answer. So
/// the words a line may begin with and the names that may stand between braces are chosen per
/// kind, while the grammar — a word, a colon, and what to print — is the same file a club already
/// knows how to edit. Stored as smallint and append-only.
/// </remarks>
public enum ReportTemplateKind
{
    /// <summary>One trip, written up.</summary>
    Trip = 0,

    /// <summary>A camp, written up over the trips it gathered.</summary>
    Expedition = 1,
}

/// <summary>One instruction out of a template, already understood.</summary>
/// <param name="Directive">What this line asks the document to do.</param>
/// <param name="Label">The name of a labelled value; null for every other directive.</param>
/// <param name="Text">
/// The words to print, still carrying its braces — for <see cref="ReportTemplateDirective.Section"/>,
/// which part of the form is wanted.
/// </param>
/// <param name="Line">Which line of the template this came from, so a refusal can point at it.</param>
public sealed record ReportTemplatePart(
    ReportTemplateDirective Directive, string? Label, string Text, int Line);

/// <summary>One piece of a line: either words that were written, or a name to fill in.</summary>
/// <param name="IsPlaceholder">Whether <paramref name="Text"/> is a name rather than words.</param>
/// <param name="Text">The words, or the name without its braces.</param>
public readonly record struct ReportTemplateToken(bool IsPlaceholder, string Text);

/// <summary>What reading a template produced: its instructions, or what is wrong with it.</summary>
/// <param name="Parts">The instructions, in the order they were written. Empty when refused.</param>
/// <param name="Errors">
/// One sentence per fault, each naming the line it is on. Empty when the template is usable.
/// </param>
public sealed record ReportTemplateParse(
    IReadOnlyList<ReportTemplatePart> Parts, IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// The small language a club writes its own trip write-up in.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately tiny, and deliberately not a programming language: the person editing it is a
/// club secretary with a text editor, so every line is one word, a colon and what to print, and
/// the whole vocabulary is listed in the comments at the top of the template the system hands
/// out. A template nobody can read is worse than a fixed layout.
/// </para>
/// <para>
/// What a template may ask for is closed, and that is a rule about disclosure rather than about
/// tidiness: every name here is answered out of the reading of the trip its producer already
/// has, so a template can never become a way of asking for a field its producer may not be
/// shown. A name that comes back empty — because this trip does not record it, or because this
/// reader is not given it — takes its whole line out of the document, so a template may ask
/// freely and the document simply does not carry what there was no answer for.
/// </para>
/// </remarks>
public static class ReportTemplateFormat
{
    /// <summary>
    /// The longest a template may be. Generous for a document layout, short of somewhere to
    /// park a megabyte.
    /// </summary>
    public const int MaxLength = 20_000;

    /// <summary>Everything that may stand between braces, other than one answer out of a form.</summary>
    public static readonly IReadOnlyList<string> Placeholders =
    [
        "title", "purpose", "dates", "hours", "club", "location", "caves", "weather",
        "incident", "published", "description", "results", "depth", "length", "stations",
        "rope", "people", "sketch",
    ];

    /// <summary>
    /// The three parts of a trip's form, each of which may also be asked for one answer at a
    /// time as <c>{observations.name}</c> and the like.
    /// </summary>
    public static readonly IReadOnlyList<string> Sections = ["observations", "logistics", "safety"];

    /// <summary>
    /// Everything that may stand between braces in a camp's layout.
    /// </summary>
    /// <remarks>
    /// Its own list, not the trip's with words crossed out. A camp has a working area rather than
    /// a sketch of where somebody went, a count of the trips it gathered, and totals that are the
    /// sum of what this reader may see rather than a figure read off one record — so a name shared
    /// with the trip vocabulary would mean a different thing under it, which is exactly how a club
    /// comes to believe a document says something it does not.
    /// </remarks>
    public static readonly IReadOnlyList<string> ExpeditionPlaceholders =
    [
        "title", "description", "dates", "days", "club", "area", "trips", "caves", "people",
        "depth", "length", "stations", "rope", "hours", "published",
    ];

    private static readonly Regex PlaceholderPattern =
        new(@"\{([^{}]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every placeholder written into a piece of template text, in order, without its braces.</summary>
    public static IReadOnlyList<string> PlaceholdersIn(string text) =>
        [.. PlaceholderPattern.Matches(text ?? string.Empty).Select(m => m.Groups[1].Value.Trim())];

    /// <summary>
    /// A piece of template text broken into the words that were written and the names to fill in.
    /// </summary>
    /// <remarks>
    /// Filling in is done over these rather than by replacing text in place, because what a line
    /// should look like when one of its names has nothing behind it is a question about the
    /// pieces — the separator written between two names belongs to neither of them, and a line
    /// reading "Survey · 14 March ·" is what replacing in place produces.
    /// </remarks>
    public static IReadOnlyList<ReportTemplateToken> Tokens(string text)
    {
        var tokens = new List<ReportTemplateToken>();
        var at = 0;
        foreach (Match match in PlaceholderPattern.Matches(text ?? string.Empty))
        {
            if (match.Index > at)
            {
                tokens.Add(new ReportTemplateToken(false, text![at..match.Index]));
            }

            tokens.Add(new ReportTemplateToken(true, match.Groups[1].Value.Trim()));
            at = match.Index + match.Length;
        }

        if (text is { Length: > 0 } && at < text.Length)
        {
            tokens.Add(new ReportTemplateToken(false, text[at..]));
        }

        return tokens;
    }

    /// <summary>What may stand between braces in a layout of this kind.</summary>
    public static IReadOnlyList<string> PlaceholdersFor(ReportTemplateKind kind) =>
        kind == ReportTemplateKind.Expedition ? ExpeditionPlaceholders : Placeholders;

    /// <summary>Whether a name may stand between braces.</summary>
    public static bool IsKnownPlaceholder(string name, ReportTemplateKind kind = ReportTemplateKind.Trip)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (PlaceholdersFor(kind).Contains(trimmed, StringComparer.Ordinal))
        {
            return true;
        }

        // One answer out of one part of the form, named the way the form named it. The part must
        // be one of the three; the answer's own name is whatever the club called it, so it is not
        // checked here — a name no answer was recorded under simply comes back empty. A camp has
        // no form of its own, so this reach belongs to a trip's layout alone.
        var dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        return kind == ReportTemplateKind.Trip
            && dot > 0
            && dot < trimmed.Length - 1
            && Sections.Contains(trimmed[..dot], StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads a template, refusing it whole when any line is unusable.
    /// </summary>
    /// <remarks>
    /// Everything wrong with it is reported at once and each fault names its line, because the
    /// person fixing it is editing a text file and a refusal that surfaces one fault per attempt
    /// is how a five-minute edit becomes an afternoon.
    /// </remarks>
    public static ReportTemplateParse Parse(
        string? body, ReportTemplateKind kind = ReportTemplateKind.Trip)
    {
        var parts = new List<ReportTemplatePart>();
        var errors = new List<string>();
        var directives = DirectivesFor(kind);

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ReportTemplateParse([], ["The template is empty."]);
        }

        if (body.Length > MaxLength)
        {
            return new ReportTemplateParse(
                [], [$"The template is longer than {MaxLength} characters."]);
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var word = (colon < 0 ? line : line[..colon]).Trim();
            var rest = colon < 0 ? string.Empty : line[(colon + 1)..].Trim();

            if (!directives.TryGetValue(word, out var directive))
            {
                errors.Add(
                    $"Line {number}: '{word}' is not something a template can ask for. "
                    + $"The words a line may begin with are: {string.Join(", ", directives.Keys)}.");
                continue;
            }

            switch (directive)
            {
                case ReportTemplateDirective.Roster:
                case ReportTemplateDirective.Photographs:
                case ReportTemplateDirective.Trips:
                case ReportTemplateDirective.Days:
                case ReportTemplateDirective.Teams:
                    if (rest.Length > 0)
                    {
                        errors.Add($"Line {number}: '{word}' stands on its own and takes nothing after it.");
                        continue;
                    }

                    parts.Add(new ReportTemplatePart(directive, null, string.Empty, number));
                    continue;

                case ReportTemplateDirective.Section:
                    if (!Sections.Contains(rest, StringComparer.Ordinal))
                    {
                        errors.Add(
                            $"Line {number}: a trip's form has no part called '{rest}'. "
                            + $"The parts are: {string.Join(", ", Sections)}.");
                        continue;
                    }

                    parts.Add(new ReportTemplatePart(directive, null, rest, number));
                    continue;

                case ReportTemplateDirective.Field:
                    var equals = rest.IndexOf('=', StringComparison.Ordinal);
                    if (equals < 0)
                    {
                        errors.Add(
                            $"Line {number}: a labelled value is written "
                            + "'field: <name> = <what to print>', and this line has no '='.");
                        continue;
                    }

                    var label = rest[..equals].Trim();
                    var value = rest[(equals + 1)..].Trim();
                    if (label.Length == 0)
                    {
                        errors.Add($"Line {number}: the labelled value has no name before its '='.");
                        continue;
                    }

                    if (Check(value, number, kind, errors))
                    {
                        parts.Add(new ReportTemplatePart(directive, label, value, number));
                    }

                    continue;

                default:
                    if (rest.Length == 0)
                    {
                        errors.Add($"Line {number}: '{word}' has nothing to print after it.");
                        continue;
                    }

                    if (Check(rest, number, kind, errors))
                    {
                        parts.Add(new ReportTemplatePart(directive, null, rest, number));
                    }

                    continue;
            }
        }

        if (errors.Count == 0 && parts.Count == 0)
        {
            errors.Add("The template says nothing a document could be built from.");
        }

        return new ReportTemplateParse(errors.Count == 0 ? parts : [], errors);
    }

    private static bool Check(
        string text, int number, ReportTemplateKind kind, List<string> errors)
    {
        var ok = true;
        var subject = kind == ReportTemplateKind.Expedition ? "a camp" : "a trip";
        foreach (var name in PlaceholdersIn(text))
        {
            if (IsKnownPlaceholder(name, kind))
            {
                continue;
            }

            ok = false;
            errors.Add(
                $"Line {number}: nothing about {subject} is called '{name}'. "
                + $"What may go between braces: {string.Join(", ", PlaceholdersFor(kind))}"
                + (kind == ReportTemplateKind.Trip
                    ? $", and one answer out of a part of the form written as "
                        + $"{string.Join(", ", Sections.Select(s => $"{{{s}.name}}"))}."
                    : "."));
        }

        return ok;
    }

    /// <summary>The words a line may begin with, for a layout of this kind.</summary>
    private static Dictionary<string, ReportTemplateDirective> DirectivesFor(ReportTemplateKind kind) =>
        kind == ReportTemplateKind.Expedition ? ExpeditionDirectives : Directives;

    private static readonly Dictionary<string, ReportTemplateDirective> Directives =
        new(StringComparer.Ordinal)
        {
            ["title"] = ReportTemplateDirective.Title,
            ["heading"] = ReportTemplateDirective.Heading,
            ["text"] = ReportTemplateDirective.Text,
            ["note"] = ReportTemplateDirective.Note,
            ["field"] = ReportTemplateDirective.Field,
            ["bullet"] = ReportTemplateDirective.Bullet,
            ["roster"] = ReportTemplateDirective.Roster,
            ["photographs"] = ReportTemplateDirective.Photographs,
            ["section"] = ReportTemplateDirective.Section,
        };

    /// <remarks>
    /// The shared half is deliberately identical — a club that has learned to write 'field: Name =
    /// {title}' has learned it for both. What differs is only what a camp has and a trip has not:
    /// the trips it gathered, the shape of its days, and who was there in which role. 'section' is
    /// absent because a camp fills in no form, so a layout naming one is a mistake worth saying out
    /// loud rather than a line that quietly prints nothing.
    /// </remarks>
    private static readonly Dictionary<string, ReportTemplateDirective> ExpeditionDirectives =
        new(StringComparer.Ordinal)
        {
            ["title"] = ReportTemplateDirective.Title,
            ["heading"] = ReportTemplateDirective.Heading,
            ["text"] = ReportTemplateDirective.Text,
            ["note"] = ReportTemplateDirective.Note,
            ["field"] = ReportTemplateDirective.Field,
            ["bullet"] = ReportTemplateDirective.Bullet,
            ["roster"] = ReportTemplateDirective.Roster,
            ["photographs"] = ReportTemplateDirective.Photographs,
            ["trips"] = ReportTemplateDirective.Trips,
            ["days"] = ReportTemplateDirective.Days,
            ["teams"] = ReportTemplateDirective.Teams,
        };

    /// <summary>
    /// The template the system produces for a club to take away and edit.
    /// </summary>
    /// <remarks>
    /// It documents the language in its own comments, because the person who needs that
    /// documentation is the person holding this file, and a reference kept anywhere else is a
    /// reference they will not have open. The structural words are English: a club circulating a
    /// bulletin in another language rewrites them here, which is the point of handing out
    /// something people edit.
    /// </remarks>
    public static string Default { get; } = string.Join(
        "\n",
        "# The template a trip's write-up is built from. Edit it in any text editor and upload it",
        "# again. Lines beginning with # are notes to whoever is editing and are never printed.",
        "#",
        "# Every other line is one instruction: a word, a colon, and what to print.",
        "#",
        "#   title: <text>           the document's own title, once, at the top",
        "#   heading: <text>         a part heading; a part nothing came out under is left out",
        "#   text: <text>            a paragraph of prose",
        "#   note: <text>            a quieter aside",
        "#   field: <name> = <text>  a labelled value on one line",
        "#   bullet: <text>          one entry of a list",
        "#   roster                  everybody who was on the trip, one line each",
        "#   photographs             the pictures filed against the trip",
        "#   section: observations   every answer recorded in that part of the trip's form",
        "#   section: logistics",
        "#   section: safety",
        "#",
        "# Anything in braces is filled in from the trip:",
        "#",
        "#   {title} {purpose} {dates} {hours} {club} {location} {caves} {weather} {incident}",
        "#   {published} {description} {results} {depth} {length} {stations} {rope} {people}",
        "#   {sketch}",
        "#",
        "# and a single answer out of one part of the form, under the name the form gave it:",
        "#",
        "#   {observations.name}  {logistics.name}  {safety.name}",
        "#",
        "# A line whose braces all come back empty is left out. So a template may ask for",
        "# something a trip does not record — or something the person producing the document is",
        "# not shown, such as the account of what went wrong — and the document simply does not",
        "# carry it.",
        "",
        "title: {title}",
        "note: {purpose} · {dates} · {club}",
        "",
        "field: Date = {dates}",
        "field: Underground = {hours}",
        "field: Location = {location}",
        "field: Caves = {caves}",
        "field: Weather = {weather}",
        "field: Incident = {incident}",
        "field: Published = {published}",
        "",
        "heading: Account",
        "text: {description}",
        "field: Results = {results}",
        "",
        "heading: Who was there",
        "note: {people}",
        "roster",
        "",
        "heading: Where",
        "field: Sketch = {sketch}",
        "",
        "heading: Measured",
        "field: Depth reached = {depth}",
        "field: Length surveyed = {length}",
        "field: Survey stations = {stations}",
        "field: Rope = {rope}",
        "",
        "heading: Observations",
        "section: observations",
        "",
        "heading: Logistics",
        "section: logistics",
        "",
        "heading: Safety",
        "section: safety",
        "",
        "heading: Photographs",
        "photographs",
        "");

    /// <summary>The layout the system ships for a layout of this kind.</summary>
    public static string DefaultFor(ReportTemplateKind kind) =>
        kind == ReportTemplateKind.Expedition ? ExpeditionDefault : Default;

    /// <summary>
    /// The template the system produces for a club to take away and edit, for a camp.
    /// </summary>
    /// <remarks>
    /// The same file in the same language, documenting its own vocabulary in its own comments for
    /// the same reason the trip's does. What it is not is a trip layout with a few words changed:
    /// a fortnight is read day by day and team by team, and a document that simply concatenated
    /// twenty write-ups would be the thing this whole feature exists to stop somebody producing.
    /// </remarks>
    public static string ExpeditionDefault { get; } = string.Join(
        "\n",
        "# The template a camp's write-up is built from. Edit it in any text editor and upload it",
        "# again. Lines beginning with # are notes to whoever is editing and are never printed.",
        "#",
        "# Every other line is one instruction: a word, a colon, and what to print.",
        "#",
        "#   title: <text>           the document's own title, once, at the top",
        "#   heading: <text>         a part heading; a part nothing came out under is left out",
        "#   text: <text>            a paragraph of prose",
        "#   note: <text>            a quieter aside",
        "#   field: <name> = <text>  a labelled value on one line",
        "#   bullet: <text>          one entry of a list",
        "#   trips                   the trips gathered into the camp, one line each",
        "#   days                    what ran on each day of the camp, day by day",
        "#   teams                   who was there in each role",
        "#   roster                  everybody recorded as having been there, one line each",
        "#   photographs             the pictures filed against the camp's trips",
        "#",
        "# Anything in braces is filled in from the camp:",
        "#",
        "#   {title} {description} {dates} {days} {club} {area} {trips} {caves} {people}",
        "#   {depth} {length} {stations} {rope} {hours} {published}",
        "#",
        "# A line whose braces all come back empty is left out. Every total is the sum of what the",
        "# person producing the document may read — a trip they may not open contributes nothing to",
        "# it, and the document says so where it matters, so two people producing the same write-up",
        "# and getting different figures is expected rather than a fault in either of them.",
        "",
        "title: {title}",
        "note: {dates} · {club}",
        "",
        "field: Dates = {dates}",
        "field: Days = {days}",
        "field: Working area = {area}",
        "field: Trips = {trips}",
        "field: People = {people}",
        "field: Caves = {caves}",
        "field: Published = {published}",
        "",
        "heading: Account",
        "text: {description}",
        "",
        "heading: Measured",
        "field: Underground = {hours}",
        "field: Deepest reached = {depth}",
        "field: Length surveyed = {length}",
        "field: Survey stations = {stations}",
        "field: Rope = {rope}",
        "",
        "heading: Day by day",
        "days",
        "",
        "heading: The trips",
        "trips",
        "",
        "heading: Who was there",
        "note: {people}",
        "teams",
        "roster",
        "",
        "heading: Photographs",
        "photographs",
        "");
}
