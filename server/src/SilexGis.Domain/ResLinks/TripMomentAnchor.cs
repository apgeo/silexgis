// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;

namespace SilexGis.Domain.ResLinks;

/// <summary>
/// The payload of a <see cref="Entities.AnchorKind.TripMoment"/> anchor, written and read in one
/// place.
/// </summary>
/// <remarks>
/// <para>
/// Small enough to inline and deliberately not inlined. The write path composes the payload, the
/// validator checks it, the tests assert it and a later publication fold will read it back — four
/// callers that have to agree on one field name and one spelling of an instant, and the cost of
/// them disagreeing is a picture that is stored, accepted, and never drawn.
/// </para>
/// <para>
/// Written in UTC with the round-trip format, so two anchors naming the same moment are the same
/// string: the host-link lookup treats equal moments as one moment, and it compares instants rather
/// than text, but a stable spelling is what keeps a stored payload legible to a person reading rows.
/// </para>
/// </remarks>
public static class TripMomentAnchor
{
    /// <summary>The payload's single field.</summary>
    public const string AtField = "at";

    /// <summary>The stored payload for one moment.</summary>
    public static string Payload(DateTimeOffset at) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [AtField] = at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        });

    /// <summary>
    /// The moment a stored payload names, or null when there is none to read — a payload that is
    /// absent (withheld from this caller, or an anchor of another kind), malformed, or carrying
    /// something that is not an instant. Every read of an anchor is a question: the column is
    /// free-form JSON and a row can have been written by a newer server.
    /// </summary>
    public static DateTimeOffset? Read(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(AtField, out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return DateTimeOffset.TryParse(
                element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
