// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Filters;

namespace SilexGis.Domain.Map;

/// <summary>What a saved view's document may not hold.</summary>
public static class SavedViewRules
{
    /// <summary>The detail sent with the refusal of a view whose filter holds a position.</summary>
    public const string FilterHoldsPositionDetail = "A saved view never stores a place in its filter.";

    /// <summary>
    /// Whether the view's filter document carries a position of its own: a point or an outline
    /// written into a condition, or the place nearest first is measured from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A saved view is shown to whoever it is shared with, exactly as a saved filter is, so it keeps
    /// the saved filter's rule: a place somebody typed belongs to the request that ran with it and
    /// never to a stored question, which would hand it to everybody the question reaches. A filter
    /// naming an object, an area or another saved filter is kept — it is looked up afresh, under
    /// each reader's own access, every time it runs. A view naming a saved filter by id holds no
    /// document at all, and the saved filter was refused a position when it was saved.
    /// </para>
    /// <para>
    /// A document this version cannot read is left alone: nothing runs it, and a reader opens such
    /// a view without a filter.
    /// </para>
    /// </remarks>
    public static bool FilterHoldsPosition(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty("filterDocument", out var stored)
            || stored.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var document = FilterDocument.Deserialize(stored.GetRawText());
        return document is not null && (document.Anchor is not null || SpatialConditions.HoldsPosition(document));
    }
}
