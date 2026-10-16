// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Domain.Map;

/// <summary>Where a saved view's camera stood: its centre in degrees, and its zoom.</summary>
public sealed record SharedViewCamera(double Longitude, double Latitude, double Zoom)
{
    /// <summary>The part of the world a link to this view answers about.</summary>
    public SharedViewReach Reach => SharedViewReach.Around(Longitude, Latitude, Zoom);
}

/// <summary>
/// What a saved view's own document says about what a shared link to it may draw: where its
/// camera stood, which background was chosen, which of the two data overlays a link can show were
/// switched on, and the tag the view was narrowed to.
/// </summary>
/// <remarks>
/// <para>
/// The document belongs to the client and is versioned there; the server reads exactly these
/// things from it and nothing else, and reads each defensively — a missing or mistyped value
/// means "not switched on", never "on". A link is a narrower reading of the view, so anything this
/// cannot read is something the link does not show. A view whose camera cannot be read shares no
/// place, and its link draws no data at all; a tag filter that is there but cannot be read
/// narrows to nothing rather than to everything.
/// </para>
/// <para>
/// Only the entrance layer and the surface-feature layer can be drawn through a link. Everything
/// else a view can switch on — survey lines, photographs, trips, imported files, georeferenced
/// maps, the density layer — stays behind sign-in whatever the view says.
/// </para>
/// </remarks>
/// <param name="TagFilter">The tag slug the view's data was narrowed to, or null for none.</param>
/// <param name="Camera">Where the view's camera stood, or null when the document does not say readably.</param>
public sealed record SharedViewConfig(
    long? BaseLayerId,
    bool EntrancesVisible,
    bool SurfaceFeaturesVisible,
    string? TagFilter,
    SharedViewCamera? Camera)
{
    /// <summary>Nothing chosen and nothing switched on.</summary>
    public static readonly SharedViewConfig Nothing = new(null, false, false, null, null);

    /// <summary>Reads what a link may draw from a stored view document.</summary>
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

            var baseLayerId = root.TryGetProperty("baseLayerId", out var baseLayer)
                && baseLayer.ValueKind == JsonValueKind.Number
                && baseLayer.TryGetInt64(out var id)
                ? id
                : (long?)null;
            var camera = ReadCamera(root);
            var (tagReadable, tag) = ReadTag(root);

            // Data is drawn only where the view says where it is and what it was narrowed to.
            var dataReadable = camera is not null && tagReadable;
            return new SharedViewConfig(
                baseLayerId,
                dataReadable && IsOn(root, "entrancesVisible"),
                dataReadable && IsOn(root, "surfaceFeaturesVisible"),
                tag,
                camera);
        }
        catch (JsonException)
        {
            return Nothing;
        }
    }

    private static SharedViewCamera? ReadCamera(JsonElement root)
    {
        if (!root.TryGetProperty("center", out var center)
            || center.ValueKind != JsonValueKind.Array
            || center.GetArrayLength() != 2
            || !root.TryGetProperty("zoom", out var zoomElement)
            || !TryFinite(center[0], out var longitude)
            || !TryFinite(center[1], out var latitude)
            || !TryFinite(zoomElement, out var zoom)
            || longitude is < -180 or > 180
            || latitude is < -90 or > 90
            || zoom < 0)
        {
            return null;
        }

        return new SharedViewCamera(longitude, latitude, Math.Min(zoom, MapZoom.Highest));
    }

    /// <summary>
    /// The tag filter: absent, null or blank is no filter; a string is that tag; anything else is
    /// unreadable — and unreadable must not be read as "no filter", which would widen the view.
    /// </summary>
    private static (bool Readable, string? Tag) ReadTag(JsonElement root)
    {
        if (!root.TryGetProperty("tagFilter", out var tag) || tag.ValueKind == JsonValueKind.Null)
        {
            return (true, null);
        }

        if (tag.ValueKind != JsonValueKind.String)
        {
            return (false, null);
        }

        var slug = tag.GetString()?.Trim();
        return (true, string.IsNullOrEmpty(slug) ? null : slug);
    }

    private static bool TryFinite(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool IsOn(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
