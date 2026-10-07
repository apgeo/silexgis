// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Documents;

/// <summary>Which of a trip's three form parts a set of answers or field titles belongs to.</summary>
public enum TripSectionKey
{
    FieldData = 0,
    Logistics = 1,
    Safety = 2,
}

/// <summary>
/// How the answers on a trip's form are written into a document, whichever document it is.
/// </summary>
/// <remarks>
/// <para>
/// A trip's own write-up prints them, and so does the write-up of a camp the trip was gathered
/// into. How an answer reads — what a tick, a number or a list of choices becomes, what a question
/// is called, and that an answer saying nothing is not printed at all — is one thing, written
/// once here so the two documents cannot come to word the same answer differently.
/// </para>
/// <para>
/// Nothing here decides who may be told an answer. That was decided before the words arrived: a
/// part this reading is not given arrives as nothing, and nothing is what is written for it —
/// no heading, no empty line, no sentence saying something was left out.
/// </para>
/// </remarks>
public static class TripNarrativeComposition
{
    /// <summary>The part of the form a layout names by this word.</summary>
    public static TripSectionKey KeyOf(string section) => section switch
    {
        "logistics" => TripSectionKey.Logistics,
        "safety" => TripSectionKey.Safety,
        _ => TripSectionKey.FieldData,
    };

    /// <summary>
    /// Every answer recorded in one part of a trip's form, each under the name its purpose gave
    /// the question.
    /// </summary>
    /// <remarks>
    /// The account of what went wrong arrives as nothing at all for a reader who may not change
    /// the trip — not as an empty bag — so nothing at all is what this writes.
    /// </remarks>
    public static IEnumerable<DocumentBlock> Section(
        TripNarrative narrative,
        IReadOnlyDictionary<TripSectionKey, IReadOnlyDictionary<string, string>> sectionTitles,
        TripSectionKey key)
    {
        ArgumentNullException.ThrowIfNull(narrative);
        ArgumentNullException.ThrowIfNull(sectionTitles);

        if (BagOf(narrative, key) is not { ValueKind: JsonValueKind.Object } written)
        {
            yield break;
        }

        var titles = sectionTitles.GetValueOrDefault(key);
        foreach (var property in written.EnumerateObject())
        {
            if (Value(property.Value) is not { } text)
            {
                continue;
            }

            yield return DocumentBlock.Field(titles?.GetValueOrDefault(property.Name) ?? property.Name, text);
        }
    }

    /// <summary>
    /// One answer out of one part of the trip's form, by the name the form gave it.
    /// </summary>
    /// <remarks>
    /// Read out of the answer this reading was given and nowhere else, which is what makes a
    /// layout safe to let a club write: a part this reader is not given arrives as nothing, so
    /// naming one of its answers in a layout produces a document without that line rather than a
    /// way of asking for it.
    /// </remarks>
    /// <param name="name">A part and an answer, written <c>part.answer</c>.</param>
    public static string? Answer(TripNarrative narrative, string name)
    {
        ArgumentNullException.ThrowIfNull(narrative);
        ArgumentNullException.ThrowIfNull(name);

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot >= name.Length - 1)
        {
            return null;
        }

        var section = name[..dot];
        if (!ReportTemplateFormat.Sections.Contains(section, StringComparer.Ordinal))
        {
            return null;
        }

        return BagOf(narrative, KeyOf(section)) is { ValueKind: JsonValueKind.Object } bag
            && bag.TryGetProperty(name[(dot + 1)..], out var answer)
            ? Value(answer)
            : null;
    }

    /// <summary>
    /// What a purpose calls the questions in each of its three sections.
    /// </summary>
    /// <remarks>
    /// Read off the schema the answers were written against, so a document names a field the way
    /// the form that collected it did. A schema that says nothing about a key leaves the key
    /// itself, which is worse to read than a title and better than dropping the answer.
    /// </remarks>
    public static Dictionary<TripSectionKey, IReadOnlyDictionary<string, string>> SectionTitles(
        TripType? tripType)
    {
        var titles = new Dictionary<TripSectionKey, IReadOnlyDictionary<string, string>>();
        if (tripType is null)
        {
            return titles;
        }

        Add(TripSectionKey.FieldData, tripType.FieldDataSchema);
        Add(TripSectionKey.Logistics, tripType.LogisticsSchema);
        Add(TripSectionKey.Safety, tripType.SafetySchema);
        return titles;

        void Add(TripSectionKey key, string? schema)
        {
            if (string.IsNullOrWhiteSpace(schema))
            {
                return;
            }

            try
            {
                using var parsed = JsonDocument.Parse(schema);
                if (!parsed.RootElement.TryGetProperty("properties", out var properties)
                    || properties.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                var map = new Dictionary<string, string>();
                foreach (var property in properties.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object
                        && property.Value.TryGetProperty("title", out var title)
                        && title.ValueKind == JsonValueKind.String
                        && title.GetString() is { Length: > 0 } text)
                    {
                        map[property.Name] = text;
                    }
                }

                titles[key] = map;
            }
            catch (JsonException)
            {
                // A schema that will not parse names nothing; the keys stand in for its titles.
            }
        }
    }

    private static JsonElement? BagOf(TripNarrative narrative, TripSectionKey key) => key switch
    {
        TripSectionKey.FieldData => narrative.FieldData,
        TripSectionKey.Logistics => narrative.Logistics,
        _ => narrative.Safety,
    };

    /// <summary>How one answer in a section reads, or null when it says nothing.</summary>
    private static string? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Array => value.GetArrayLength() == 0
            ? null
            : string.Join(", ", value.EnumerateArray().Select(Value).Where(x => x is not null)),
        _ => null,
    };
}
