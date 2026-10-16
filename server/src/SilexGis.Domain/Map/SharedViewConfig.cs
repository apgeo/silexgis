// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Domain.Map;

/// <summary>
/// What a saved view's own document says about what a shared link to it may draw: which
/// background was chosen, and which of the two data overlays a link can show were switched on.
/// </summary>
/// <remarks>
/// <para>
/// The document belongs to the client and is versioned there; the server reads exactly these
/// three things from it and nothing else, and reads each defensively — a missing or mistyped value
/// means "not switched on", never "on". A link is a narrower reading of the view, so anything this
/// cannot read is something the link does not show.
/// </para>
/// <para>
/// Only the entrance layer and the surface-feature layer can be drawn through a link. Everything
/// else a view can switch on — survey lines, photographs, trips, imported files, georeferenced
/// maps, the density layer — stays behind sign-in whatever the view says.
/// </para>
/// </remarks>
public sealed record SharedViewConfig(long? BaseLayerId, bool EntrancesVisible, bool SurfaceFeaturesVisible)
{
    /// <summary>Nothing chosen and nothing switched on.</summary>
    public static readonly SharedViewConfig Nothing = new(null, false, false);

    /// <summary>Reads the three things a link may draw from a stored view document.</summary>
    public static SharedViewConfig Read(string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return Nothing;
        }

        try
        {
            using var parsed = JsonDocument.Parse(document);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Nothing;
            }

            return new SharedViewConfig(
                root.TryGetProperty("baseLayerId", out var baseLayer)
                    && baseLayer.ValueKind == JsonValueKind.Number
                    && baseLayer.TryGetInt64(out var baseLayerId)
                    ? baseLayerId
                    : null,
                IsOn(root, "entrancesVisible"),
                IsOn(root, "surfaceFeaturesVisible"));
        }
        catch (JsonException)
        {
            return Nothing;
        }
    }

    private static bool IsOn(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
