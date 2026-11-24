// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Flybys;

/// <summary>
/// The camera of a flyby at one frame, as the 3D scene had it: a place on the globe at its true
/// height, a compass heading, a tilt and a roll in degrees, how far away what it looks at is, and
/// its lens in degrees across the wider side of the picture.
/// </summary>
public sealed record FlybyBundlePose(
    double Longitude,
    double Latitude,
    double HeightM,
    double HeadingDeg,
    double PitchDeg,
    double RollDeg,
    double FocusM,
    double LensDeg);

/// <summary>
/// A mesh of one cave: triangles in metres east, north and up about a point given as a place on
/// the globe and a surveyed altitude.
/// </summary>
public sealed record FlybyBundleMesh(
    IndexedMesh Mesh,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorAltitudeM);

/// <summary>One leg of a cave's survey, between two places at their surveyed altitudes.</summary>
public sealed record FlybyBundleLeg(
    double FromLongitude,
    double FromLatitude,
    double FromAltitudeM,
    double ToLongitude,
    double ToLatitude,
    double ToAltitudeM);

/// <summary>
/// What is drawn of one cave.
/// </summary>
/// <param name="Walls">Its walls, when they are drawn.</param>
/// <param name="Estimated">
/// Tubes of a size nobody measured around passages the survey gives no size for, kept apart from
/// the walls so that they are drawn in a colour of their own.
/// </param>
/// <param name="EstimatedColor">That colour, linear RGB.</param>
/// <param name="Legs">Its survey legs, when its lines are drawn.</param>
/// <param name="AltitudeShiftM">
/// Metres added to every surveyed altitude of this cave to put it where the scene drew it.
/// </param>
public sealed record FlybyBundleCave(
    FlybyBundleMesh? Walls,
    FlybyBundleMesh? Estimated,
    (double R, double G, double B)? EstimatedColor,
    IReadOnlyList<FlybyBundleLeg> Legs,
    double AltitudeShiftM)
{
    /// <summary>
    /// The colour mixed into this cave's walls over the clip, where the flyby colours it apart from
    /// the rest; null where it takes the colour of every cave.
    /// </summary>
    public IReadOnlyList<FlybyBundleTintKey>? WallsTint { get; init; }

    /// <summary>The colour of this cave's survey lines over the clip, where it has one of its own.</summary>
    public IReadOnlyList<FlybyBundleTintKey>? LinesTint { get; init; }

    /// <summary>
    /// Whether this cave's lines stand in for walls it has none of: they are then shown, hidden and
    /// faded as the walls are, since they are all there is to see of it.
    /// </summary>
    public bool LinesStandIn { get; init; }
}

/// <summary>How a value gets to a key from the key before it.</summary>
public enum FlybyBundleEasing
{
    /// <summary>Evenly.</summary>
    Linear = 0,

    /// <summary>Eased in and out: 3x² − 2x³ of the way at the share x of the time.</summary>
    Smooth = 1,

    /// <summary>Not at all: the value before holds until the key's frame, and jumps there.</summary>
    Constant = 2,
}

/// <summary>A number at a frame of the clip, counted from 0.</summary>
public sealed record FlybyBundleNumberKey(int Frame, double Value, FlybyBundleEasing Easing = FlybyBundleEasing.Linear);

/// <summary>A colour, <c>#rrggbb</c>, at a frame of the clip.</summary>
public sealed record FlybyBundleColourKey(int Frame, string Colour, FlybyBundleEasing Easing = FlybyBundleEasing.Linear);

/// <summary>A colour laid over something by a strength from 0 to 1, at a frame of the clip.</summary>
public sealed record FlybyBundleTintKey(int Frame, string Colour, double Strength, FlybyBundleEasing Easing = FlybyBundleEasing.Linear);

/// <summary>From this frame on something is shown, or hidden. Each key says the opposite of the one before it.</summary>
public sealed record FlybyBundleShownKey(int Frame, bool Shown);

/// <summary>
/// What changes of one layer of the scene — the walls, or the survey lines — while the clip runs.
/// An empty list is a thing that does not change.
/// </summary>
/// <param name="Shown">When the layer is shown and hidden; before the first key it is the other way round.</param>
/// <param name="Opacity">How much of it is there, from 0 to 1; one key is that much throughout.</param>
/// <param name="Tint">
/// For walls, the colour mixed into their own. For survey lines, the one colour they are drawn in
/// (strength 1) or their colours by depth (strength 0).
/// </param>
public sealed record FlybyBundleLayerChanges(
    IReadOnlyList<FlybyBundleShownKey> Shown,
    IReadOnlyList<FlybyBundleNumberKey> Opacity,
    IReadOnlyList<FlybyBundleTintKey> Tint)
{
    public static FlybyBundleLayerChanges None { get; } = new([], [], []);
}

/// <summary>
/// What a flyby changes about the look while the clip runs, as keys on the clip's frames: between
/// two keys a value moves as the later key's easing says, before its first key it has that key's
/// value and after its last it keeps it.
/// </summary>
/// <param name="Relief">
/// How many times taller than true every height is drawn. Empty: the scene's one factor throughout.
/// </param>
/// <param name="Sky">What stands behind everything. Empty: the look's one colour throughout.</param>
public sealed record FlybyBundleChanges(
    IReadOnlyList<FlybyBundleNumberKey> Relief,
    IReadOnlyList<FlybyBundleColourKey> Sky,
    FlybyBundleLayerChanges Walls,
    FlybyBundleLayerChanges Lines)
{
    public static FlybyBundleChanges None { get; } = new([], [], FlybyBundleLayerChanges.None, FlybyBundleLayerChanges.None);
}

/// <summary>A card of words over the picture.</summary>
/// <param name="Seconds">
/// For a title, how long after the first frame it has gone; for an end card, how long before the
/// last frame it comes.
/// </param>
public sealed record FlybyBundleCard(string Text, string? Subtitle, double Seconds);

/// <summary>Words shown from one moment of the clip to another, in seconds from its first frame.</summary>
public sealed record FlybyBundleCaption(string Text, double StartS, double EndS);

/// <summary>What a flyby writes over its picture, on the clock of the clip.</summary>
public sealed record FlybyBundleTexts(
    FlybyBundleCard? Title,
    FlybyBundleCard? End,
    IReadOnlyList<FlybyBundleCaption> Captions,
    string? Corner,
    IReadOnlyList<FlybyBundleCaption> CornerCaptions,
    double FadeInS,
    double FadeOutS,
    bool NorthArrow)
{
    public static FlybyBundleTexts None { get; } = new(null, null, [], null, [], 0, 0, false);
}

/// <summary>A colour survey lines take from a depth below their cave's highest point downwards.</summary>
public sealed record FlybyBundleDepthBand(double FromM, string Colour);

/// <summary>
/// How the scene looks.
/// </summary>
/// <param name="SkyColour">What stands behind everything, <c>#rrggbb</c>; null for black.</param>
/// <param name="LineColour">One colour for every survey line; null to colour them by depth.</param>
/// <param name="DepthBands">The colours by depth, shallowest first.</param>
public sealed record FlybyBundleLook(
    string? SkyColour,
    string? LineColour,
    IReadOnlyList<FlybyBundleDepthBand> DepthBands);

/// <summary>How long a render may take a frame: the more samples, the cleaner the picture.</summary>
public enum FlybyBundleQuality
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// A flyby, resolved: every frame's camera, the caves drawn and where the scene drew them, the
/// look and the texts — everything a render bundle is written from, and nothing left to look up.
/// </summary>
/// <param name="Name">What the flyby is called; the bundle's folder and movie are named after it.</param>
/// <param name="OriginLongitude">
/// The place the scene is laid out about. It becomes the zero of the bundle's frame and is written
/// nowhere in it.
/// </param>
/// <param name="Relief">
/// How many times taller than true every height is drawn — at the first frame, and throughout
/// where <see cref="Changes"/> says nothing of the relief.
/// </param>
/// <param name="Poses">One camera per frame, in order.</param>
/// <param name="LeftOut">Sentences saying what the flyby shows that the bundle does not.</param>
public sealed record FlybyBundleScene(
    string Name,
    int Width,
    int Height,
    int Fps,
    FlybyBundleQuality Quality,
    double OriginLongitude,
    double OriginLatitude,
    double OriginHeightM,
    double Relief,
    IReadOnlyList<FlybyBundlePose> Poses,
    IReadOnlyList<FlybyBundleCave> Caves,
    FlybyBundleLook Look,
    FlybyBundleTexts Texts,
    IReadOnlyList<string> LeftOut)
{
    /// <summary>What changes about the look while the clip runs.</summary>
    public FlybyBundleChanges Changes { get; init; } = FlybyBundleChanges.None;
}
