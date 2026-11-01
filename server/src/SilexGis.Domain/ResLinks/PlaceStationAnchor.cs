// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Domain.ResLinks;

/// <summary>
/// The payload a place-to-station mapping keeps on its survey member: the station, in the spelling
/// the 3D viewer gives it, and — for the far end of a splay rather than the station itself — the
/// offset from that station in metres east, north and up. Plain data: nothing works the splay end
/// out into a model position.
/// </summary>
public static class PlaceStationAnchor
{
    /// <summary>The field holding the station's name.</summary>
    public const string StationField = "station";

    /// <summary>The field holding the splay end's offset from the station, <c>[east, north, up]</c>.</summary>
    public const string SplayField = "splay";

    /// <summary>The longest station name kept — the bound every survey station name has.</summary>
    public const int StationMaxLength = 400;

    /// <summary>The largest offset a splay end may have on any axis, in metres.</summary>
    public const double SplayMaxMetres = 10_000;

    /// <summary>A parsed payload.</summary>
    public readonly record struct Parsed(string Station, double[]? Splay);

    /// <summary>What is wrong with a station and splay as a phone sends them, or null.</summary>
    public static string? Problem(string? station, IReadOnlyList<double>? splay)
    {
        if (string.IsNullOrWhiteSpace(station))
        {
            return "a station is required";
        }

        if (station.Length > StationMaxLength)
        {
            return $"a station is at most {StationMaxLength} characters";
        }

        if (splay is null)
        {
            return null;
        }

        return splay.Count != 3 || splay.Any(v => !double.IsFinite(v) || Math.Abs(v) > SplayMaxMetres)
            ? $"a splay is three finite numbers of metres, east, north and up, each within {SplayMaxMetres:0}"
            : null;
    }

    /// <summary>The payload for a station, or for the far end of a splay from it.</summary>
    public static string Write(string station, IReadOnlyList<double>? splay)
    {
        ArgumentNullException.ThrowIfNull(station);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString(StationField, station);
            if (splay is not null)
            {
                json.WriteStartArray(SplayField);
                foreach (var v in splay)
                {
                    json.WriteNumberValue(v);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>A payload read back, or null when it is not one.</summary>
    public static Parsed? Read(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(anchor);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(StationField, out var station)
                || station.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            double[]? splay = null;
            if (root.TryGetProperty(SplayField, out var offsets))
            {
                if (offsets.ValueKind != JsonValueKind.Array
                    || offsets.GetArrayLength() != 3
                    || offsets.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                {
                    return null;
                }

                splay = [.. offsets.EnumerateArray().Select(v => v.GetDouble())];
            }

            return new Parsed(station.GetString()!, splay);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether two splays are the same offset — both absent, or equal on every axis.</summary>
    public static bool SameSplay(IReadOnlyList<double>? a, IReadOnlyList<double>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);
}
