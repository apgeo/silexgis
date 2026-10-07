// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Map;

/// <summary>
/// What an administrator of an installation says about copying one map background into the
/// documents it produces: follow the catalogue, or yes, or no.
/// </summary>
/// <remarks>
/// Three answers rather than two, because "I have not decided" has to stay distinguishable from
/// "I decided the same as the catalogue". Somebody who has decided nothing goes on receiving the
/// catalogue's corrections — a source whose terms changed is unmarked for them by the next
/// release — and somebody who has decided is left alone by it.
/// </remarks>
public enum MapLayerDocumentChoice : short
{
    /// <summary>Nothing decided here: the catalogue's answer stands, and follows the catalogue.</summary>
    Default = 0,

    /// <summary>This installation copies the source into documents, whatever the catalogue says.</summary>
    On = 1,

    /// <summary>This installation does not, whatever the catalogue says.</summary>
    Off = 2,
}

/// <summary>Why a source cannot carry the mark that lets a document copy it.</summary>
public enum MapLayerDocumentObstacle
{
    /// <summary>A document's picture is drawn over one background, and an overlay is not one.</summary>
    NotABackground,

    /// <summary>
    /// Terms that allow a copy allow it with the credit beside the picture, and a credit nobody
    /// wrote down cannot be put there.
    /// </summary>
    NoAttribution,
}

/// <summary>
/// Which map backgrounds a document may copy: what may carry the mark at all, and how the
/// catalogue's answer and an administrator's combine.
/// </summary>
/// <remarks>
/// <para>
/// Showing a tile on a screen and putting it into a file that is then mailed and printed are
/// different uses, and a provider's terms decide the second separately. Nothing about a tile
/// address says which terms stand behind it, so the answer is declared per source by somebody who
/// read them: the catalogue file says what ships, and an administrator may say otherwise for
/// their own installation — its sources, its agreements with their providers, and its documents
/// are theirs to answer for.
/// </para>
/// <para>
/// The two conditions here are not about terms and hold whoever is asking, the catalogue or an
/// administrator: without them the mark could only ever produce a picture with no background, or
/// one with nothing written under it.
/// </para>
/// </remarks>
public static class MapLayerDocumentRules
{
    /// <summary>Refusal: the layer is an overlay, and a document's picture takes one background.</summary>
    public const string NotABackgroundCode = "map_layer.not_a_background";

    /// <summary>Refusal: the source has no credit to write under a picture.</summary>
    public const string AttributionRequiredCode = "map_layer.attribution_required";

    /// <summary>
    /// What stands in the way of marking a source as one a document may copy, or null when
    /// nothing does.
    /// </summary>
    public static MapLayerDocumentObstacle? ObstacleTo(bool isBase, string? attribution)
    {
        if (!isBase)
        {
            return MapLayerDocumentObstacle.NotABackground;
        }

        return string.IsNullOrWhiteSpace(attribution) ? MapLayerDocumentObstacle.NoAttribution : null;
    }

    /// <summary>
    /// Whether a document may copy the source: the administrator's answer where they gave one,
    /// the catalogue's otherwise.
    /// </summary>
    public static bool Effective(bool catalogue, bool? administrator) => administrator ?? catalogue;

    /// <summary>The stored form of a choice: null while the catalogue is followed.</summary>
    public static bool? Stored(MapLayerDocumentChoice choice) => choice switch
    {
        MapLayerDocumentChoice.On => true,
        MapLayerDocumentChoice.Off => false,
        _ => null,
    };

    /// <summary>The choice a stored value stands for.</summary>
    public static MapLayerDocumentChoice ChoiceOf(bool? stored) => stored switch
    {
        true => MapLayerDocumentChoice.On,
        false => MapLayerDocumentChoice.Off,
        null => MapLayerDocumentChoice.Default,
    };
}
