// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Wire vocabulary of polymorphic target types: "feature" (any feature id, whatever its
/// kind) plus the camelCase non-feature entity names ("tripLog", "cavingGroup", "geofile",
/// "georeferencedMap", "mapView", "storedFile", "expedition"). Parsed case-insensitively so
/// query-string values behave like the camelCase JSON enum convention. The underlying
/// enum is shared with other polymorphic-pair consumers (resource links speak a wider
/// set); attachments and taggings accept only the names above — anything else, later
/// enum values included, reads as unknown here.
/// </summary>
public static class AttachmentTargets
{
    public const string FeatureName = "feature";

    /// <summary>On success a null <paramref name="entityType"/> means the feature world.</summary>
    public static bool TryParse(string? value, out AttachedEntityType? entityType)
    {
        entityType = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, FeatureName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Enum.TryParse<AttachedEntityType>(value, ignoreCase: true, out var parsed) && IsAttachable(parsed))
        {
            entityType = parsed;
            return true;
        }

        return false;
    }

    // Deliberately an allow-set rather than Enum.IsDefined: appending a value to the
    // shared enum must never quietly widen what attachments and taggings accept.
    private static bool IsAttachable(AttachedEntityType type) => type switch
    {
        AttachedEntityType.TripLog or AttachedEntityType.CavingGroup or AttachedEntityType.Geofile
            or AttachedEntityType.GeoreferencedMap or AttachedEntityType.MapView
            or AttachedEntityType.StoredFile or AttachedEntityType.Expedition => true,
        _ => false,
    };

    /// <summary>Wire name of a stored target row (feature FK XOR polymorphic pair).</summary>
    public static string NameOf(Guid? featureId, AttachedEntityType? entityType) =>
        featureId is not null
            ? FeatureName
            : JsonNamingPolicy.CamelCase.ConvertName(entityType!.Value.ToString());
}
