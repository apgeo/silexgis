// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Documents;

/// <summary>What a file is being hung on, in the terms the role it takes there depends on.</summary>
public enum LinkTargetNature
{
    /// <summary>A cave entrance.</summary>
    Entrance,

    /// <summary>A place or an area inside a cave.</summary>
    CaveInterior,

    /// <summary>A cave as a whole.</summary>
    Cave,

    /// <summary>A trip or a point recorded along one.</summary>
    Recording,

    /// <summary>Anything else a file can hang on — a surface area, a centerline.</summary>
    Other,
}

/// <summary>
/// The role a file takes on the object it is hung on when whoever hung it named none — which is
/// how a phone sends most of its links, because the app keeps no such distinction.
/// </summary>
/// <remarks>
/// A picture says what it shows by where it hangs: one on an entrance is a picture of the
/// entrance, one on a place or an area underground is a picture of the inside of the cave. On a
/// cave as a whole, a trip or a point, where it hangs says nothing about what it shows, so it is
/// filed as other rather than guessed. Every file that is not a picture is a document wherever
/// it hangs.
/// </remarks>
public static class LinkRoleDefaults
{
    /// <summary>The feature-type codes of the places and areas inside a cave.</summary>
    public static readonly IReadOnlySet<string> InteriorTypeCodes =
        new HashSet<string>(StringComparer.Ordinal) { "cave_place", "cave_area" };

    public static AttachmentRole For(FileKind fileKind, LinkTargetNature target)
    {
        if (fileKind != FileKind.Image)
        {
            return AttachmentRole.Document;
        }

        return target switch
        {
            LinkTargetNature.Entrance => AttachmentRole.PhotoEntrance,
            LinkTargetNature.CaveInterior => AttachmentRole.PhotoInterior,
            _ => AttachmentRole.Other,
        };
    }

    /// <summary>What a feature is, for the role a file hung on it takes.</summary>
    /// <param name="featureTypeCode">The code of a generic feature's type; ignored for the other kinds.</param>
    public static LinkTargetNature NatureOf(FeatureKind kind, string? featureTypeCode) => kind switch
    {
        FeatureKind.CaveEntrance => LinkTargetNature.Entrance,
        FeatureKind.Cave => LinkTargetNature.Cave,
        FeatureKind.Generic when featureTypeCode is not null && InteriorTypeCodes.Contains(featureTypeCode)
            => LinkTargetNature.CaveInterior,
        _ => LinkTargetNature.Other,
    };
}
