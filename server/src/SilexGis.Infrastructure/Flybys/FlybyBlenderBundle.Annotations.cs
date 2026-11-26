// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Geo;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Cave3D;

namespace SilexGis.Infrastructure.Flybys;

// What a bundle points out, and how a cave appears part by part.
//
// The 3D view draws an entrance, a feature of the surface and the place a photograph was taken as
// an icon of one size on the screen, however far the camera is. A render has no such thing: what
// stands in a scene has a size in metres. So each marker is given the size that reads at the
// nearest the camera comes to it — the one moment its size matters most — and a photograph, which
// the view shows only as an icon to click, is shown itself, while the camera is near enough to
// read it.
//
// Everything is pinned to a point of a cave's model, in that model's own metres, never to a place
// in the scene: the model is stretched when the relief changes, and what is pinned to it rides
// along. It also keeps the bundle saying what it has always said and no more — how things lie to
// one another.
public static partial class FlybyBlenderBundle
{
    /// <summary>What every photograph is called in the plan, for whoever wants to hide or change them all.</summary>
    public const string PhotoRole = "station-photo";

    /// <summary>The folder inside a bundle its photographs lie in.</summary>
    public const string PhotosDirectory = "photos";

    /// <summary>
    /// A marker's radius as a share of the nearest the camera comes to it: across, a little under a
    /// fiftieth of the distance, which is what an icon of the 3D view takes of a picture's width.
    /// </summary>
    public const double MarkerShare = 0.008;

    /// <summary>The smallest and the largest a marker is made, in metres of radius.</summary>
    public const double SmallestMarkerM = 0.1;

    /// <inheritdoc cref="SmallestMarkerM"/>
    public const double LargestMarkerM = 100;

    /// <summary>The height of the letters of a name, in radii of its marker.</summary>
    public const double NameLetters = 2.5;

    /// <summary>How far above its marker a name stands, in letter heights — the survey library's own lift of a callout.</summary>
    public const double NameLift = 4;

    /// <summary>
    /// A photograph's width as a share of the distance it is made to be read from: about a sixth
    /// of the picture's width there.
    /// </summary>
    public const double PhotoShare = 0.2;

    /// <summary>
    /// How long a photograph is in the picture at the least, in seconds. A camera that flies past
    /// a station is near it for a few frames; nobody reads a picture in a fifth of a second.
    /// </summary>
    public const double PhotoLeastSeconds = 2;

    /// <summary>The narrowest and the widest a photograph is made, in metres.</summary>
    public const double NarrowestPhotoM = 0.3;

    /// <inheritdoc cref="NarrowestPhotoM"/>
    public const double WidestPhotoM = 150;

    /// <summary>
    /// How near the camera is while a photograph is shown, in widths of the photograph: two and a
    /// half times the distance it is made to be read from, where it still takes a fifteenth of
    /// the picture.
    /// </summary>
    public const double PhotoNearWidths = 12.5;

    /// <summary>How long a photograph takes to appear and to go, in seconds.</summary>
    public const double PhotoFadeSeconds = 0.5;

    /// <summary>The line from a photograph to its station and the border about it, as shares of its width — the survey library's own.</summary>
    private const double PhotoLineShare = 0.01;

    /// <inheritdoc cref="PhotoLineShare"/>
    private const double PhotoFrameShare = 0.02;

    /// <summary>How many photographs one bundle shows: the ones the camera comes nearest to.</summary>
    public const int MaxPhotos = 40;

    /// <param name="PhotosShown">Which of the scene's photographs are in the plan, counted from 0.</param>
    private sealed record Pointed(IReadOnlyList<PlanAnnotation> Annotations, IReadOnlyList<PlanChange> Changes, IReadOnlyList<int> PhotosShown);

    /// <summary>
    /// Of some places, the ones the camera of a flyby comes nearest to, nearest first — by true
    /// distances on the globe, whatever relief the scene is drawn at. Which photographs go into a
    /// bundle is decided by it before any of them is opened.
    /// </summary>
    /// <param name="places">Each a longitude, a latitude and a true height.</param>
    /// <param name="most">How many are wanted at most.</param>
    /// <returns>Which of <paramref name="places"/>, counted from 0.</returns>
    public static IReadOnlyList<int> NearestToTheCamera(
        IReadOnlyList<FlybyBundlePose> poses,
        IReadOnlyList<(double Longitude, double Latitude, double HeightM)> places,
        int most)
    {
        ArgumentNullException.ThrowIfNull(poses);
        ArgumentNullException.ThrowIfNull(places);
        var eyes = poses.Select(pose => LocalTangentFrame.ToEcef(pose.Longitude, pose.Latitude, pose.HeightM)).ToList();
        return
        [
            .. places
                .Select((place, index) =>
                {
                    var there = LocalTangentFrame.ToEcef(place.Longitude, place.Latitude, place.HeightM);
                    return (Index: index, Nearest: eyes.Count == 0 ? 0 : eyes.Min(eye => eye.DistanceTo(there)));
                })
                .OrderBy(place => place.Nearest)
                .ThenBy(place => place.Index)
                .Take(Math.Max(most, 0))
                .Select(place => place.Index),
        ];
    }

    /// <summary>
    /// The markers, the names and the photographs of a scene as the plan's annotations, and the
    /// changes that show and hide them.
    /// </summary>
    /// <param name="stands">Where each model of the plan stands.</param>
    /// <param name="views">The camera of each frame.</param>
    /// <param name="reliefs">The relief of each frame.</param>
    private static Pointed Annotations(
        FlybyBundleScene scene,
        IReadOnlyList<Standing> stands,
        Layout layout,
        IReadOnlyList<SceneView> views,
        double[] reliefs,
        PlanTimeline timeline,
        Action<string, byte[]> writePhoto)
    {
        var annotations = new List<PlanAnnotation>();
        var changes = new List<PlanChange>();
        var photosShown = new List<int>();
        if (stands.Count == 0)
        {
            return new Pointed(annotations, changes, photosShown);
        }

        var fps = timeline.Fps;
        double Seconds(int index) => Rounded(index / (double)fps);

        // One model a cave to pin things to: the first the cave has.
        var ofCave = stands.GroupBy(stand => stand.Cave).ToDictionary(group => group.Key, group => group.First());

        // Where a point pinned to a model is at a frame: the model's own stretch, turn and place.
        SceneVector At(Standing stand, CaveVector3 pin, int frame)
        {
            var up = layout.Stretch is null ? 1 : reliefs[frame];
            return stand.Translation + Turned(new SceneVector(pin.X, pin.Y, pin.Z * up), stand.Axis, stand.Degrees);
        }

        double[] Distances(Standing stand, CaveVector3 pin)
        {
            var distances = new double[views.Count];
            for (var f = 0; f < distances.Length; f++)
            {
                distances[f] = (views[f].Eye - At(stand, pin, f)).Length;
            }

            return distances;
        }

        foreach (var layer in scene.MarkerLayers)
        {
            var colour = Linear(layer.Colour);
            var named = false;
            var count = 0;
            foreach (var marker in layer.Markers)
            {
                // Pinned to the cave it stands nearest to, which it then leans with.
                var stand = ofCave.Values.MinBy(each =>
                {
                    var (east, north, _) = each.About.ToLocal(marker.Longitude, marker.Latitude, marker.HeightM);
                    return (east * east) + (north * north);
                })!;
                var pin = Pin(stand, marker.Longitude, marker.Latitude, marker.HeightM);
                var radius = Rounded(Math.Clamp(Distances(stand, pin).Min() * MarkerShare, SmallestMarkerM, LargestMarkerM));
                var id = $"{layer.Role}-{(++count).ToString("000", CultureInfo.InvariantCulture)}";
                annotations.Add(new PlanAnnotation
                {
                    Id = id,
                    Role = layer.Role,
                    Kind = PlanAnnotationKind.Marker,
                    Model = stand.Id,
                    At = pin,
                    Radius = radius,
                    Color = colour,
                });
                if (!string.IsNullOrWhiteSpace(marker.Name))
                {
                    var letters = Rounded(radius * NameLetters);
                    named = true;
                    annotations.Add(new PlanAnnotation
                    {
                        Id = id + "-name",
                        Role = NameRole(layer.Role),
                        Kind = PlanAnnotationKind.Callout,
                        Model = stand.Id,
                        At = pin,
                        Text = marker.Name.Trim(),
                        Size = letters,
                        Offset = new CaveVector3(0, 0, Rounded(letters * NameLift)),
                    });
                }
            }

            // A layer is shown, hidden and faded whole: one change for its markers, one for its names.
            if (count > 0 && (layer.Shown.Count > 0 || layer.Opacity.Count > 0))
            {
                foreach (var role in named ? [layer.Role, NameRole(layer.Role)] : new[] { layer.Role })
                {
                    changes.Add(new PlanChange
                    {
                        Target = new PlanChangeTarget { Role = role },
                        Visible = layer.Shown.Count > 0
                            ? [
                                .. layer.Shown.Select(key => new PlanVisibleKey(
                                    Seconds(key.Frame), key.Shown, Seconds(key.FadeFrames), key.FadeFrames > 0 ? PlanEasing.Smooth : PlanEasing.Linear)),
                            ]
                            : null,
                        Opacity = layer.Opacity.Count > 0 ? Numbers(layer.Opacity, Seconds) : null,
                    });
                }
            }
        }

        // A photograph is in the picture while its layer is shown and the camera is near its station.
        var layerShown = ShownAtEachFrame(scene.Photos.Shown, views.Count);
        var number = 0;
        for (var p = 0; p < scene.Photos.Photos.Count && number < MaxPhotos; p++)
        {
            var photo = scene.Photos.Photos[p];
            if (!ofCave.TryGetValue(photo.Cave, out var stand) || photo.WidthPx <= 0 || photo.HeightPx <= 0)
            {
                continue;
            }

            var pin = Pin(stand, photo.Longitude, photo.Latitude, photo.AltitudeM + scene.Caves[photo.Cave].AltitudeShiftM);
            var distances = Distances(stand, pin);
            var width = Math.Clamp(ReadFrom(distances, fps) * PhotoShare, NarrowestPhotoM, WidestPhotoM);
            var near = width * PhotoNearWidths;
            var windows = Runs([.. distances.Select((distance, f) => layerShown[f] && distance <= near)], fps);
            if (windows.Count == 0)
            {
                // Never in the picture: its file stays out of the bundle with it.
                continue;
            }

            var id = $"photo-{(++number).ToString("000", CultureInfo.InvariantCulture)}";
            var path = $"{PhotosDirectory}/{id}.jpg";
            var height = width * photo.HeightPx / photo.WidthPx;
            annotations.Add(new PlanAnnotation
            {
                Id = id,
                Role = PhotoRole,
                Kind = PlanAnnotationKind.Photo,
                Model = stand.Id,
                At = pin,
                Image = path,
                Width = Rounded(width),

                // Above its station, clear of it by a quarter of its width, with a line down to it.
                Offset = new CaveVector3(0, 0, Rounded((height / 2) + (width / 4))),
                LineWidth = Rounded(width * PhotoLineShare),
                Frame = new PlanPhotoFrame { Width = Rounded(width * PhotoFrameShare) },
            });
            if (AnnotationPlanner.Shown(id, windows, timeline, PhotoFadeSeconds, PhotoFadeSeconds) is { } shown)
            {
                changes.Add(shown);
            }

            writePhoto(path, photo.Jpeg);
            photosShown.Add(p);
        }

        return new Pointed(annotations, changes, photosShown);
    }

    /// <summary>
    /// The distance a photograph is made to be read from: the nearest the camera comes to its
    /// station — or, where the camera passes quickly, two fifths of how far it is from the station
    /// half the least time before and after that moment. A photograph as wide as a fifth of this,
    /// shown while the camera is within two and a half times this, is in the picture for the
    /// least time at least, and never smaller than a fifteenth of the picture's width while it is.
    /// </summary>
    /// <param name="distances">From the camera to the station, frame by frame.</param>
    public static double ReadFrom(IReadOnlyList<double> distances, int fps)
    {
        ArgumentNullException.ThrowIfNull(distances);
        var nearestAt = 0;
        for (var f = 1; f < distances.Count; f++)
        {
            if (distances[f] < distances[nearestAt])
            {
                nearestAt = f;
            }
        }

        // Half the least time to either side, as far as the clip goes.
        var half = (int)Math.Round(PhotoLeastSeconds * fps / 2);
        var before = distances[Math.Max(nearestAt - half, 0)];
        var after = distances[Math.Min(nearestAt + half, distances.Count - 1)];

        // Near enough is the width times the widths near enough is counted in: two and a half
        // times what is returned here. So the frames half the least time away are near enough.
        return Math.Max(distances[nearestAt], Math.Max(before, after) / (PhotoShare * PhotoNearWidths));
    }

    /// <summary>What the names of a layer's markers are called in the plan.</summary>
    public static string NameRole(string role) => role + "-name";

    /// <summary>
    /// A place of the globe, drawn at a true height, as a point of a model in the model's own
    /// metres — raised by what the model's mesh was raised by.
    /// </summary>
    private static CaveVector3 Pin(Standing stand, double longitude, double latitude, double drawnHeightM)
    {
        var (east, north, up) = stand.About.ToLocal(longitude, latitude, drawnHeightM);
        return new CaveVector3(Rounded(east), Rounded(north), Rounded(up + stand.LiftM));
    }

    /// <summary>A vector turned about an axis by an angle, right-handed, as the plan turns a model.</summary>
    private static SceneVector Turned(SceneVector v, SceneVector axis, double degrees)
    {
        if (degrees == 0 || axis.Length == 0)
        {
            return v;
        }

        var a = axis.Normalized();
        var angle = degrees * Math.PI / 180;
        var (sin, cos) = Math.SinCos(angle);
        return (v * cos) + (a.Cross(v) * sin) + (a * (a.Dot(v) * (1 - cos)));
    }

    /// <summary>
    /// Whether a layer that is switched at once is shown, frame by frame: before its first key it
    /// is the other way round, and with no key it is shown throughout.
    /// </summary>
    private static bool[] ShownAtEachFrame(IReadOnlyList<FlybyBundleShownKey> keys, int frames)
    {
        var shown = new bool[frames];
        var on = keys.Count == 0 || !keys[0].Shown;
        var next = 0;
        for (var f = 0; f < frames; f++)
        {
            while (next < keys.Count && keys[next].Frame <= f)
            {
                on = keys[next].Shown;
                next++;
            }

            shown[f] = on;
        }

        return shown;
    }

    /// <summary>The runs of frames that are true, as spans of the clip in seconds; the last runs to the end.</summary>
    private static List<PlanWindow> Runs(bool[] frames, int fps)
    {
        var windows = new List<PlanWindow>();
        var start = -1;
        for (var f = 0; f <= frames.Length; f++)
        {
            var on = f < frames.Length && frames[f];
            if (on && start < 0)
            {
                start = f;
            }
            else if (!on && start >= 0)
            {
                windows.Add(new PlanWindow(Rounded(start / (double)fps), f < frames.Length ? Rounded(f / (double)fps) : null));
                start = -1;
            }
        }

        return windows;
    }

    /// <summary>
    /// The threshold a model appears by, and how its edge looks: the survey library's rule for a
    /// reveal's keys — from the lowest number, where nothing is there, to past the highest by the
    /// edge and the glow, where all of it is and nothing glows any more — with the softness of the
    /// edge and the colour of the glow the library gives a reveal of its own.
    /// </summary>
    /// <param name="range">The lowest and the highest number the threshold runs over: the cave's
    /// own, or, by date, those of every cave of the scene.</param>
    private static (IReadOnlyList<PlanNumberKey>? Threshold, PlanRevealFront? Front) Revealing(
        PlanChangeTarget target, (double Min, double Max) range, double fromSeconds, double toSeconds, int fps)
    {
        var library = new RevealSpec();
        var change = RevealPlanner.Change(
            target, range.Min, range.Max, fromSeconds, toSeconds, library.Edge, glow: true, library.GlowColor, fps);
        return (change.Reveal, change.RevealFront);
    }

    private static PlanChangeTarget PlanTarget(string id) => new() { Id = id };
}
