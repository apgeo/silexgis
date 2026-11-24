// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Export;

/// <summary>
/// Something a flyby shows in the 3D scene that a render bundle of it does not carry.
///
/// <para>
/// A bundle is made anyway and says what it leaves out — a flyby is not refused because part of
/// its look has no counterpart yet. What a flyby changes while it plays is not on the list: the
/// relief, the colours of walls and lines, the layers a bundle draws shown, hidden and faded, and
/// the sky are carried, change by change. The list is closed: whoever asks for a bundle names what
/// applies to their flyby in these words, the page says it before the file is made and the file
/// says it again in its own notes, so the two cannot drift into saying different things.
/// </para>
/// </summary>
public enum FlybyBundleOmission
{
    /// <summary>The ground and the map drawn on it. Always so: a bundle holds caves and no terrain.</summary>
    Ground = 0,

    /// <summary>The opening cut in the ground over a cave.</summary>
    SurfaceOpening = 1,

    /// <summary>Entrance markers.</summary>
    Entrances = 2,

    /// <summary>Surface features and layers of features.</summary>
    SurfaceFeatures = 3,

    /// <summary>Point-cloud scans.</summary>
    Scans = 4,

    /// <summary>Photograph markers.</summary>
    Photos = 5,

    /// <summary>Tile overlays and georeferenced maps.</summary>
    MapLayers = 6,

    /// <summary>The lines of imported files.</summary>
    Tracks = 7,

    /// <summary>Walls the viewer chose not to download into the 3D view.</summary>
    WallsNotDownloaded = 8,

    /// <summary>Caves in view beyond the number a bundle takes.</summary>
    MoreCavesInView = 9,
}

/// <summary>What a bundle's own notes say about each thing it leaves out.</summary>
public static class FlybyBundleOmissions
{
    /// <summary>
    /// The sentence for one omission, as it stands in a list headed "the render does not show".
    /// In English, as the rest of a bundle's notes are.
    /// </summary>
    public static string Sentence(FlybyBundleOmission omission) => omission switch
    {
        FlybyBundleOmission.Ground =>
            "the ground and the map drawn on it (the caves stand in empty space, under a plain sky)",
        FlybyBundleOmission.SurfaceOpening =>
            "the opening cut in the ground over a cave",
        FlybyBundleOmission.Entrances =>
            "the entrance markers",
        FlybyBundleOmission.SurfaceFeatures =>
            "the surface features and the layers of features",
        FlybyBundleOmission.Scans =>
            "the point-cloud scans",
        FlybyBundleOmission.Photos =>
            "the photograph markers",
        FlybyBundleOmission.MapLayers =>
            "the map overlays and the georeferenced maps",
        FlybyBundleOmission.Tracks =>
            "the lines of imported files",
        FlybyBundleOmission.WallsNotDownloaded =>
            "the walls that were not downloaded into the 3D view (those caves are drawn by their survey lines)",
        FlybyBundleOmission.MoreCavesInView =>
            "some of the caves that were in view (a bundle takes only so many)",
        _ => throw new ArgumentOutOfRangeException(nameof(omission), omission, "Not something a bundle leaves out."),
    };

    /// <summary>
    /// Caves that were asked for and are not drawn. One count and no reason: a cave that does not
    /// exist, one the asker may not read, one they may not place exactly and one with no survey to
    /// draw are all the same sentence, because telling them apart would say which caves are being
    /// kept from whom.
    /// </summary>
    public static string CavesNotDrawn(int count) => count == 1
        ? "1 of the caves asked for (nothing of it is available to draw)"
        : $"{count} of the caves asked for (nothing of them is available to draw)";

    /// <summary>Caves whose walls were asked for and that have none to draw, so their lines stand in.</summary>
    public static string WallsNotAvailable(int count) => count == 1
        ? "the walls of 1 cave, which has none to draw (it is drawn by its survey lines)"
        : $"the walls of {count} caves, which have none to draw (they are drawn by their survey lines)";

    /// <summary>Caves whose walls would have taken the bundle past the size this installation allows.</summary>
    public static string WallsOverBudget(int count) => count == 1
        ? "the walls of 1 cave, which would take the bundle past the size this installation allows (it is drawn by its survey lines)"
        : $"the walls of {count} caves, which would take the bundle past the size this installation allows (they are drawn by their survey lines)";
}
