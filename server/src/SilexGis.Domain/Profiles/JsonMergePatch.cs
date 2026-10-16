// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;

namespace SilexGis.Domain.Profiles;

/// <summary>
/// JSON Merge Patch (RFC 7396): what a stored JSON object becomes when a partial one is written
/// over it. A member the patch names replaces the stored one, an object member is merged member by
/// member, a null removes the member, and everything the patch does not name is kept.
/// </summary>
/// <remarks>
/// This is what lets two independent writers share one stored document — the appearance settings
/// and any later account-level setting — without the second write destroying the first. A patch
/// that is not an object replaces the whole document, as the RFC says; the preferences route
/// refuses one before it gets here.
/// </remarks>
public static class JsonMergePatch
{
    public static JsonNode? Apply(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch?.DeepClone();
        }

        var result = target is JsonObject targetObject ? (JsonObject)targetObject.DeepClone() : [];
        foreach (var (name, value) in patchObject)
        {
            if (value is null)
            {
                result.Remove(name);
            }
            else
            {
                result[name] = Apply(result[name], value);
            }
        }

        return result;
    }
}
