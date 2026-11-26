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
    double ToAltitudeM)
{
    /// <summary>
    /// The station the leg starts at, as the survey names it, where it is known: two legs that
    /// name one station meet there, whatever the last digit of their coordinates says.
    /// </summary>
    public string? FromStation { get; init; }

    /// <summary>The station the leg ends at.</summary>
    public string? ToStation { get; init; }

    /// <summary>The day the survey says the leg was surveyed, where it says.</summary>
    public DateOnly? SurveyedOn { get; init; }
}

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

    /// <summary>
    /// The cave's survey legs for working out in what order it appears where the scene reveals it
    /// — every passage leg, whether or not the cave's lines are drawn. Empty for a cave that is
    /// not revealed, or has no survey: it is then there from the first frame.
    /// </summary>
    public IReadOnlyList<FlybyBundleLeg> SurveyLegs { get; init; } = [];

    /// <summary>The stations of the survey that are entrances, by name: a reveal starts from them.</summary>
    public IReadOnlyList<string> EntranceStations { get; init; } = [];

    /// <summary>
    /// The one date the survey these legs came from was given as a whole, where it has one: what a
    /// reveal by date shows the cave on when none of its legs has a day of its own.
    /// </summary>
    public DateOnly? SurveyDate { get; init; }
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

/// <summary>
/// A number at a frame of the clip, counted from 0. A frame may lie after the clip's last: the clip
/// then ends on the way to the key. And before its first, below 0: the clip then begins on the way
/// from the key. Up to three keys of one track may stand on one frame: the first is where the value
/// was going, the second — <see cref="FlybyBundleEasing.Constant"/> — what it is on that frame and
/// from it on, and a third, constant too, the value the way to the next key starts from, which no
/// frame shows.
/// </summary>
public sealed record FlybyBundleNumberKey(int Frame, double Value, FlybyBundleEasing Easing = FlybyBundleEasing.Linear);

/// <summary>A colour, <c>#rrggbb</c>, at a frame of the clip.</summary>
public sealed record FlybyBundleColourKey(int Frame, string Colour, FlybyBundleEasing Easing = FlybyBundleEasing.Linear);

/// <summary>A colour laid over something by a strength from 0 to 1, at a frame of the clip.</summary>
public sealed record FlybyBundleTintKey(int Frame, string Colour, double Strength, FlybyBundleEasing Easing = FlybyBundleEasing.Linear);

/// <summary>
/// From this frame on something is shown, or hidden. Each key says the opposite of the one before it.
/// A key before the clip's first frame, below 0, is a coming or a going the clip begins part of
/// the way through.
/// </summary>
/// <param name="FadeFrames">
/// How many frames, from this one on, the thing takes to appear, or to go before it is hidden —
/// eased in and out; 0 is a cut.
/// </param>
public sealed record FlybyBundleShownKey(int Frame, bool Shown, int FadeFrames = 0);

/// <summary>
/// What changes of one layer of the scene — the walls, or the survey lines — while the clip runs.
/// An empty list is a thing that does not change.
/// </summary>
/// <param name="Shown">
/// When the layer is shown and hidden, and how long it takes to come and to go; before the first
/// key it is the other way round.
/// </param>
/// <param name="Opacity">
/// How much of it is there, from 0 to 1, besides its coming and going; one key is that much throughout.
/// </param>
/// <param name="Tint">
/// For walls, the colour mixed into their own. For survey lines, the one colour they are drawn in
/// (strength 1) or their colours by depth (strength 0). A colour crosses into another as a screen
/// mixes the two.
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

    /// <summary>
    /// How much of the daylight is there, from 0 to 1: the sun and the sky are that strong. It goes
    /// while the camera is under the ground of the scene the flyby was played in — which whoever
    /// played it knows, and nobody here does. Empty: all of it throughout.
    /// </summary>
    public IReadOnlyList<FlybyBundleNumberKey> Daylight { get; init; } = [];
}

/// <summary>
/// Something pointed out on the ground of the scene the flyby was played in: an entrance, or a
/// feature of the surface.
/// </summary>
/// <param name="HeightM">
/// The true height of the ground the scene drew it on. A bundle has no ground: the marker stands
/// where the scene's ground had it.
/// </param>
/// <param name="Name">What is written beside it; null for a marker alone.</param>
public sealed record FlybyBundleMarker(double Longitude, double Latitude, double HeightM, string? Name);

/// <summary>
/// The markers of one layer of the scene: one colour, one name to find them by, shown and hidden
/// together.
/// </summary>
/// <param name="Role">
/// What every marker of the layer is called in the plan, lower-case words joined by hyphens; the
/// names written beside them are called the same with "-name" after it.
/// </param>
/// <param name="Colour">The colour the markers glow in, <c>#rrggbb</c>.</param>
/// <param name="Shown">When the layer is shown and hidden; before its first key it is the other way round.</param>
/// <param name="Opacity">How much of it is there, from 0 to 1, besides its coming and going.</param>
public sealed record FlybyBundleMarkerLayer(
    string Role,
    string Colour,
    IReadOnlyList<FlybyBundleMarker> Markers,
    IReadOnlyList<FlybyBundleShownKey> Shown,
    IReadOnlyList<FlybyBundleNumberKey> Opacity);

/// <summary>
/// A photograph taken at a station of a cave's survey: the station's place, and the picture as
/// the bytes of a JPEG file.
/// </summary>
/// <param name="Cave">Which cave of the scene it belongs to, counted from 0 in the order the scene lists them.</param>
/// <param name="AltitudeM">The station's surveyed altitude; the cave's own shift puts it where the scene drew the cave.</param>
/// <param name="WidthPx">How wide the picture is, in pixels; with <paramref name="HeightPx"/>, its shape.</param>
public sealed record FlybyBundlePhoto(
    int Cave,
    double Longitude,
    double Latitude,
    double AltitudeM,
    byte[] Jpeg,
    int WidthPx,
    int HeightPx);

/// <summary>The photographs of a scene, and when their layer is shown.</summary>
/// <param name="Shown">
/// When the layer of photographs is shown and hidden — at once, as the scene switches it; before
/// its first key it is the other way round. A photograph is in the picture while its layer is
/// shown and the camera is near it.
/// </param>
public sealed record FlybyBundlePhotos(
    IReadOnlyList<FlybyBundlePhoto> Photos,
    IReadOnlyList<FlybyBundleShownKey> Shown)
{
    public static FlybyBundlePhotos None { get; } = new([], []);

    /// <summary>
    /// How many photographs there are at the stations of the caves drawn, in all — more than
    /// <see cref="Photos"/> where a station has several or a bundle would hold too many. What the
    /// bundle does not show of them is said in its notes, as a count.
    /// </summary>
    public int Found { get; init; }
}

/// <summary>
/// The caves appear part by part while the clip runs: each from its entrances inwards, along its
/// passages, by the distance from them; or all of them on one clock of years, each leg on the day
/// it was surveyed, with the year counted on the screen.
/// </summary>
/// <param name="StartS">When the first of each cave appears, in seconds from the clip's first frame.</param>
/// <param name="EndS">When all of every cave is there.</param>
/// <param name="By">What the caves appear by.</param>
public sealed record FlybyBundleReveal(double StartS, double EndS, FlybyBundleRevealBy By = FlybyBundleRevealBy.Distance);

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

/// <summary>Which layer of the 3D view a layer of markers is.</summary>
public enum FlybyBundleMarkerKind
{
    /// <summary>The entrances of caves.</summary>
    Entrances = 0,

    /// <summary>The features of the surface, every kind together.</summary>
    SurfaceFeatures = 1,

    /// <summary>A layer of features chosen by a filter or by their types, in a colour of its own.</summary>
    Features = 2,
}

/// <summary>What caves appear part by part by.</summary>
public enum FlybyBundleRevealBy
{
    /// <summary>The distance along the passages from the entrances.</summary>
    Distance = 0,

    /// <summary>
    /// The day each leg was surveyed: the history of the exploration. A cave none of whose legs
    /// has a date appears whole on the date of its survey; one with no date at all is there from
    /// the start.
    /// </summary>
    Date = 1,
}

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

    /// <summary>
    /// The language the bundle's own notes are written in, as a language tag ("ro", "en-GB"). The
    /// notes exist in English and in Romanian; any other language is given the English ones.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>The entrances and the features of the surface the scene points out, layer by layer.</summary>
    public IReadOnlyList<FlybyBundleMarkerLayer> MarkerLayers { get; init; } = [];

    /// <summary>The photographs taken at stations of the caves drawn.</summary>
    public FlybyBundlePhotos Photos { get; init; } = FlybyBundlePhotos.None;

    /// <summary>How the caves appear part by part; null for caves that are there from the first frame.</summary>
    public FlybyBundleReveal? Reveal { get; init; }
}
