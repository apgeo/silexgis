// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain.Export;
using SilexGis.Infrastructure.Flybys;

namespace SilexGis.Api.Features.Flybys;

/// <summary>The movie a bundle renders: its frame, its rate and how carefully each frame is rendered.</summary>
/// <param name="Fps">
/// Frames a second. A whole number: a render bundle cannot be played at a rate with a fraction, so
/// twelve and a half is refused by name rather than rounded.
/// </param>
public sealed record FlybyBundleOutputRequest(int Width, int Height, double Fps, FlybyBundleQuality? Quality);

/// <summary>
/// The place the scene is laid out about — the middle of the flyby's first subject. It is the zero
/// of the bundle's frame and is written nowhere in the bundle.
/// </summary>
public sealed record FlybyBundleOriginRequest(double Longitude, double Latitude, double HeightM);

/// <summary>
/// How the 3D scene the flyby was played in turns a surveyed altitude into a drawn height: where
/// it was surveyed, raised by an offset, when the ground has relief; hung from the surface by the
/// cave's highest point when it has none.
/// </summary>
public sealed record FlybyBundlePlacementRequest(bool Absolute, double OffsetM);

/// <summary>
/// One cave of the scene.
/// </summary>
/// <param name="Walls">Whether its walls are wanted; without them, or where it has none, its survey lines are drawn.</param>
/// <param name="SurveyTopM">
/// The surveyed altitude the scene hung this cave from, when it told one. A height and nothing
/// else: it moves the cave up or down and cannot place it.
/// </param>
/// <param name="GroundM">The height of the ground at the cave in the scene, for lines that carry no altitudes.</param>
public sealed record FlybyBundleCaveRequest(Guid CaveId, bool Walls, double? SurveyTopM, double? GroundM);

/// <summary>A colour survey lines take from a depth below their cave's highest point downwards.</summary>
public sealed record FlybyBundleDepthBandRequest(double FromM, string Colour);

/// <summary>
/// The look of the scene at the clip's first frame.
/// </summary>
/// <param name="Lines">Whether survey lines are drawn at any frame of the clip.</param>
/// <param name="Sky">What stands behind everything, <c>#rrggbb</c>; left out, black.</param>
/// <param name="LineColour">One colour for every line; left out, the colours by depth.</param>
public sealed record FlybyBundleLookRequest(
    bool Lines,
    string? Sky,
    string? LineColour,
    IReadOnlyList<FlybyBundleDepthBandRequest>? DepthBands);

/// <summary>
/// A number at a frame of the clip, counted from 0. A frame may lie after the clip's last — the
/// clip then ends on the way to the key — and before its first, below 0: the clip then begins on
/// the way from the key, part of the way through a move. Up to three keys may stand on one frame:
/// the first is where the number was going, the second, which jumps, what it is on that frame and
/// from it on, and a third, a jump too, the number the way to the next key starts from, which no
/// frame shows.
/// </summary>
/// <param name="Easing">How the number gets here from the key before; left out, evenly.</param>
public sealed record FlybyBundleNumberKeyRequest(int Frame, double Value, FlybyBundleEasing? Easing);

/// <summary>
/// A colour, <c>#rrggbb</c>, at a frame of the clip. On the way from the key before it the colour
/// is mixed as a screen mixes two colours.
/// </summary>
public sealed record FlybyBundleColourKeyRequest(int Frame, string Colour, FlybyBundleEasing? Easing);

/// <summary>A colour laid over something by a strength from 0 to 1, at a frame of the clip.</summary>
public sealed record FlybyBundleTintKeyRequest(int Frame, string Colour, double Strength, FlybyBundleEasing? Easing);

/// <summary>
/// From this frame on a layer is shown, or hidden. Each key says the opposite of the one before it.
/// A layer that fades may have a key before the clip's first frame, below 0, whose fade reaches
/// into the clip: the clip then begins part of the way through it.
/// </summary>
/// <param name="FadeFrames">
/// How many frames, from this one on, the layer takes to appear, or to go before it is hidden,
/// eased in and out; left out or 0, it is cut.
/// </param>
public sealed record FlybyBundleShownKeyRequest(int Frame, bool Shown, int? FadeFrames = null);

/// <summary>
/// What changes of one layer of the scene — the walls, or the survey lines — while the clip runs.
/// A member left out does not change.
/// </summary>
/// <param name="Shown">
/// When the layer is shown and hidden, and how long it takes to come and to go; before the first
/// key it is the other way round.
/// </param>
/// <param name="Opacity">
/// How much of it is there, from 0 to 1, besides its coming and going; one key is that much throughout.
/// </param>
/// <param name="Tint">
/// For walls, the colour mixed into their own by its strength. For survey lines, the one colour
/// they are drawn in (strength 1) or their colours by depth (strength 0).
/// </param>
public sealed record FlybyBundleLayerChangesRequest(
    IReadOnlyList<FlybyBundleShownKeyRequest>? Shown,
    IReadOnlyList<FlybyBundleNumberKeyRequest>? Opacity,
    IReadOnlyList<FlybyBundleTintKeyRequest>? Tint);

/// <summary>
/// One cave the flyby colours apart from the rest: what stands in for the colour of every cave's
/// walls, and of every cave's lines, on this one.
/// </summary>
public sealed record FlybyBundleCaveChangesRequest(
    Guid CaveId,
    IReadOnlyList<FlybyBundleTintKeyRequest>? Walls,
    IReadOnlyList<FlybyBundleTintKeyRequest>? Lines);

/// <summary>
/// What the flyby changes about the look while the clip runs, as keys on the clip's frames. Between
/// two keys a value moves as the later key's easing says; before its first key it has that key's
/// value and after its last it keeps it.
/// </summary>
/// <param name="Relief">
/// How many times taller than true every height is drawn. The camera's heights are multiplied by
/// what these keys give at each frame, and the caves are stretched by the same keys.
/// </param>
/// <param name="Sky">What stands behind everything; black for no sky.</param>
/// <param name="Daylight">
/// How much of the daylight is there, from 0 to 1: the sun and the sky of the render are that
/// strong. The browser, which has the ground the flyby was played over, lets it go while the
/// camera is under that ground; left out, all of it throughout.
/// </param>
public sealed record FlybyBundleChangesRequest(
    IReadOnlyList<FlybyBundleNumberKeyRequest>? Relief,
    IReadOnlyList<FlybyBundleColourKeyRequest>? Sky,
    FlybyBundleLayerChangesRequest? Walls,
    FlybyBundleLayerChangesRequest? Lines,
    IReadOnlyList<FlybyBundleCaveChangesRequest>? Caves,
    IReadOnlyList<FlybyBundleNumberKeyRequest>? Daylight = null);

/// <summary>A card of words over the picture.</summary>
/// <param name="Seconds">
/// For the title, how long after the clip's first frame it has gone; for the end card, how long
/// before its last frame it comes.
/// </param>
public sealed record FlybyBundleCardRequest(string Text, string? Subtitle, double Seconds);

/// <summary>Words shown between two moments of the clip, in seconds from its first frame.</summary>
public sealed record FlybyBundleCaptionRequest(string Text, double StartS, double EndS);

/// <summary>
/// One entrance or feature the scene points out, by id and nothing else: where it is, what it is
/// called and whether the asker may have either are read by the route.
/// </summary>
/// <param name="GroundM">
/// The true height of the ground the scene drew it on. A height and nothing else: it moves the
/// marker up or down and cannot place it.
/// </param>
/// <param name="Named">Whether its name is written beside it.</param>
public sealed record FlybyBundleMarkerRequest(Guid FeatureId, double GroundM, bool Named = false);

/// <summary>
/// The markers of one layer of the 3D view: its colour, what it holds, and when the flyby shows it.
/// </summary>
/// <param name="Kind">Which layer it is; what its markers are called in the plan follows from it.</param>
/// <param name="Colour">The colour its markers are drawn in, <c>#rrggbb</c>.</param>
/// <param name="Shown">
/// When the layer is shown and hidden, and how long it takes to come and to go; before the first
/// key it is the other way round. Left out, it is shown throughout.
/// </param>
/// <param name="Opacity">How much of it is there, from 0 to 1, besides its coming and going.</param>
public sealed record FlybyBundleMarkerLayerRequest(
    FlybyBundleMarkerKind? Kind,
    string Colour,
    IReadOnlyList<FlybyBundleMarkerRequest>? Markers,
    IReadOnlyList<FlybyBundleShownKeyRequest>? Shown = null,
    IReadOnlyList<FlybyBundleNumberKeyRequest>? Opacity = null);

/// <summary>
/// The photographs taken at stations of caves, asked for by cave: which photographs there are,
/// where their stations lie and whether the asker may see them are read by the route.
/// </summary>
/// <param name="Caves">The caves whose photographs are wanted, each one of the caves the request names.</param>
/// <param name="Shown">
/// When the layer of photographs is shown and hidden — at once; before the first key it is the
/// other way round. Left out, it is shown throughout.
/// </param>
public sealed record FlybyBundlePhotosRequest(
    IReadOnlyList<Guid>? Caves,
    IReadOnlyList<FlybyBundleShownKeyRequest>? Shown = null);

/// <summary>
/// The caves appear part by part while the clip runs, from their entrances inwards.
/// </summary>
/// <param name="By">What they appear by. Only the distance along the passages: no leg of a stored survey has a date.</param>
/// <param name="StartS">When the first of each cave appears, in seconds from the clip's first frame.</param>
/// <param name="EndS">When all of every cave is there.</param>
public sealed record FlybyBundleRevealRequest(FlybyBundleRevealBy? By, double StartS, double EndS);

/// <summary>
/// What the flyby writes over its picture, already in words and already on the clip's own clock.
/// </summary>
public sealed record FlybyBundleTextsRequest(
    FlybyBundleCardRequest? Title,
    FlybyBundleCardRequest? End,
    IReadOnlyList<FlybyBundleCaptionRequest>? Captions,
    string? Corner,
    IReadOnlyList<FlybyBundleCaptionRequest>? CornerCaptions,
    double FadeInS,
    double FadeOutS,
    bool NorthArrow);

/// <summary>
/// A flyby as the browser played it, to be handed back as a render bundle.
///
/// <para>
/// The browser sends what only it knows: where the camera was at every frame — the path was flown
/// over the ground that browser had loaded — the look its viewer had, and the words in the
/// viewer's language. It names caves by id and sends nothing of their shape. Every mesh in the
/// bundle is read here, for the account that asks, under the rules that account reads a cave's
/// survey by.
/// </para>
/// </summary>
/// <param name="Frames">
/// One camera per frame, each eight numbers: longitude, latitude, height in metres, heading, pitch
/// and roll in degrees, the distance in metres to what the camera looks at, and the lens in degrees
/// across the wider side of the picture.
/// </param>
/// <param name="Relief">
/// How many times taller than true the scene draws every height, at the clip's first frame — and
/// throughout, where <paramref name="Changes"/> gives the relief no keys.
/// </param>
/// <param name="LeftOut">What the flyby shows that a bundle cannot carry, by name.</param>
/// <param name="Changes">What changes about the look while the clip runs; left out, nothing.</param>
/// <param name="Language">
/// The language the asker reads the application in, as a language tag ("ro", "en-GB"): the bundle's
/// own notes are written in it where they exist in it — in Romanian — and in English otherwise.
/// </param>
/// <param name="Markers">The entrances and features the scene points out, layer by layer; left out, none.</param>
/// <param name="Photos">The photographs taken at stations of the caves named; left out, none.</param>
/// <param name="Reveal">How the caves appear part by part; left out, they are there from the first frame.</param>
public sealed record FlybyBlenderBundleRequest(
    string? Name,
    FlybyBundleOutputRequest? Output,
    FlybyBundleOriginRequest? Origin,
    FlybyBundlePlacementRequest? Placement,
    double Relief,
    IReadOnlyList<double[]>? Frames,
    IReadOnlyList<FlybyBundleCaveRequest>? Caves,
    FlybyBundleLookRequest? Look,
    FlybyBundleTextsRequest? Texts,
    IReadOnlyList<FlybyBundleOmission>? LeftOut,
    FlybyBundleChangesRequest? Changes = null,
    string? Language = null,
    IReadOnlyList<FlybyBundleMarkerLayerRequest>? Markers = null,
    FlybyBundlePhotosRequest? Photos = null,
    FlybyBundleRevealRequest? Reveal = null);

public sealed partial class FlybyBlenderBundleRequestValidator : AbstractValidator<FlybyBlenderBundleRequest>
{
    /// <summary>The frame rates a bundle is made at: the whole ones a movie of a flyby is offered at.</summary>
    public static readonly IReadOnlyList<int> FrameRates = [10, 15, 20, 24, 25, 30];

    /// <summary>
    /// How many frames one bundle holds: a little over thirteen minutes at twenty-five a second.
    /// The whole request is read into memory and every frame becomes a pose in the plan.
    /// </summary>
    public const int MaxFrames = 20_000;

    /// <summary>How many caves one bundle names.</summary>
    public const int MaxCaves = 100;

    /// <summary>
    /// How many keys all the tracks of one request hold together. A change is two keys where two
    /// can say it and a key a frame where they cannot, so a long flyby with many caves coloured
    /// apart runs to thousands; this is far past that and well short of what would make the plan
    /// a burden to read.
    /// </summary>
    public const int MaxKeys = 400_000;

    /// <summary>
    /// How far after the clip's last frame a key may lie, and how long a layer may take to come or
    /// to go, in frames: as long as the longest clip. A change that ends after the clip is said by
    /// its real end, which the clip stops on the way to.
    /// </summary>
    public const int MaxFramesPastTheEnd = MaxFrames;

    /// <summary>
    /// How far before the clip's first frame a key may lie, in frames: as long as the longest
    /// clip. A change the clip begins in the middle of is said by its real start, which the clip
    /// begins on the way from.
    /// </summary>
    public const int MaxFramesBeforeTheStart = MaxFrames;

    /// <summary>
    /// How many layers of markers one bundle names: the entrances, the features of the surface, and
    /// as many layers of chosen features as a view and a flyby hold between them.
    /// </summary>
    public const int MaxMarkerLayers = 26;

    /// <summary>How many markers one bundle names, all its layers together.</summary>
    public const int MaxMarkers = 1_000;

    private const int NumbersAFrame = 8;
    private const int MaxTextLength = 500;
    private const int MaxCaptions = 500;

    public FlybyBlenderBundleRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(120);
        RuleFor(r => r.Language!)
            .Must(language => LanguageTag().IsMatch(language))
            .When(r => r.Language is not null)
            .WithMessage("A language is a tag such as \"ro\" or \"en-GB\".");

        RuleFor(r => r.Output).NotNull();
        When(r => r.Output is not null, () =>
        {
            RuleFor(r => r.Output!.Width).InclusiveBetween(16, 8192).Must(Even).WithMessage("The width is an even number of pixels.");
            RuleFor(r => r.Output!.Height).InclusiveBetween(16, 8192).Must(Even).WithMessage("The height is an even number of pixels.");
            RuleFor(r => r.Output!.Fps)
                .Must(fps => FrameRates.Any(rate => rate == fps))
                .WithMessage($"A bundle is made at one of {string.Join(", ", FrameRates)} frames a second.")
                .WithProblemCode(FlybyBlenderBundleEndpoints.FrameRateCode);
            RuleFor(r => r.Output!.Quality).NotNull().IsInEnum();
        });

        RuleFor(r => r.Origin).NotNull();
        When(r => r.Origin is not null, () =>
        {
            RuleFor(r => r.Origin!.Longitude).InclusiveBetween(-180, 180);
            RuleFor(r => r.Origin!.Latitude).InclusiveBetween(-90, 90);
            RuleFor(r => r.Origin!.HeightM).InclusiveBetween(-12_000, 100_000);
        });

        RuleFor(r => r.Placement).NotNull();
        When(r => r.Placement is not null, () =>
            RuleFor(r => r.Placement!.OffsetM).InclusiveBetween(-1_000, 1_000));

        RuleFor(r => r.Relief).InclusiveBetween(0.5, 5);

        RuleFor(r => r.Frames).NotEmpty();
        RuleFor(r => r.Frames!.Count)
            .LessThanOrEqualTo(MaxFrames)
            .When(r => r.Frames is not null)
            .WithMessage($"A bundle holds at most {MaxFrames} frames.")
            .WithProblemCode(FlybyBlenderBundleEndpoints.TooLongCode);
        RuleFor(r => r.Frames!)
            .Must(frames => frames.All(Frame))
            .When(r => r.Frames is { Count: > 0 and <= MaxFrames })
            .WithMessage("Every frame is eight numbers: a place on the globe, a height, a heading, a tilt, a roll, a distance and a lens, each within its range.");

        RuleFor(r => r.Caves).NotEmpty();
        When(r => r.Caves is not null, () =>
        {
            RuleFor(r => r.Caves!.Count).LessThanOrEqualTo(MaxCaves);
            RuleFor(r => r.Caves!)
                .Must(caves => caves.Select(c => c.CaveId).Distinct().Count() == caves.Count)
                .WithMessage("A cave is named once.");
            RuleForEach(r => r.Caves!).ChildRules(cave =>
            {
                cave.RuleFor(c => c.CaveId).NotEmpty();
                cave.RuleFor(c => c.SurveyTopM!.Value).InclusiveBetween(-12_000, 20_000).When(c => c.SurveyTopM is not null);
                cave.RuleFor(c => c.GroundM!.Value).InclusiveBetween(-12_000, 20_000).When(c => c.GroundM is not null);
            });
        });

        RuleFor(r => r.Look).NotNull();
        When(r => r.Look is not null, () =>
        {
            RuleFor(r => r.Look!.Sky!).Must(Colour).When(r => r.Look!.Sky is not null).WithMessage("A colour is written #rrggbb.");
            RuleFor(r => r.Look!.LineColour!).Must(Colour).When(r => r.Look!.LineColour is not null).WithMessage("A colour is written #rrggbb.");
            RuleFor(r => r.Look!.DepthBands!.Count).LessThanOrEqualTo(16).When(r => r.Look!.DepthBands is not null);
            RuleForEach(r => r.Look!.DepthBands!).ChildRules(band =>
            {
                band.RuleFor(b => b.FromM).InclusiveBetween(0, 20_000);
                band.RuleFor(b => b.Colour).NotNull().Must(Colour).WithMessage("A colour is written #rrggbb.");
            }).When(r => r.Look!.DepthBands is not null);
        });

        When(r => r.Texts is not null, () =>
        {
            RuleFor(r => r.Texts!.Title!).SetValidator(new CardValidator()).When(r => r.Texts!.Title is not null);
            RuleFor(r => r.Texts!.End!).SetValidator(new CardValidator()).When(r => r.Texts!.End is not null);
            RuleFor(r => r.Texts!.Corner).MaximumLength(MaxTextLength);
            RuleFor(r => r.Texts!.FadeInS).InclusiveBetween(0, 600);
            RuleFor(r => r.Texts!.FadeOutS).InclusiveBetween(0, 600);
            RuleFor(r => r.Texts!.Captions!.Count).LessThanOrEqualTo(MaxCaptions).When(r => r.Texts!.Captions is not null);
            RuleForEach(r => r.Texts!.Captions!).SetValidator(new CaptionValidator()).When(r => r.Texts!.Captions is not null);
            RuleFor(r => r.Texts!.CornerCaptions!.Count).LessThanOrEqualTo(MaxCaptions).When(r => r.Texts!.CornerCaptions is not null);
            RuleForEach(r => r.Texts!.CornerCaptions!).SetValidator(new CaptionValidator()).When(r => r.Texts!.CornerCaptions is not null);
        });

        When(r => r.Changes is not null, () =>
        {
            // A frame of the clip, when the clip's own length is known; else any frame a clip can have.
            static int Last(FlybyBlenderBundleRequest r) => r.Frames is { Count: > 0 and <= MaxFrames } frames ? frames.Count - 1 : MaxFrames - 1;
            const string order =
                "Keys are in rising order of frame; up to three may stand on one frame, each after the first of them a jump.";

            RuleFor(r => r.Changes!)
                .Must(changes => Keys(changes) <= MaxKeys)
                .WithMessage($"The changes of one bundle hold at most {MaxKeys} keys.");

            RuleFor(r => r.Changes!.Relief!)
                .Must((r, keys) => Ordered(keys.Select(k => (k?.Frame, k?.Easing)), Last(r))).WithMessage(order)
                .Must(keys => keys.All(k => k is not null && k.Value is >= 0.5 and <= 5 && Eased(k.Easing)))
                .WithMessage("A relief is a factor from 0.5 to 5.")
                .When(r => r.Changes!.Relief is not null);
            RuleFor(r => r.Changes!.Sky!)
                .Must((r, keys) => Ordered(keys.Select(k => (k?.Frame, k?.Easing)), Last(r))).WithMessage(order)
                .Must(keys => keys.All(k => k is not null && Colour(k.Colour) && Eased(k.Easing)))
                .WithMessage("A colour is written #rrggbb.")
                .When(r => r.Changes!.Sky is not null);
            RuleFor(r => r.Changes!.Daylight!)
                .Must((r, keys) => Ordered(keys.Select(k => (k?.Frame, k?.Easing)), Last(r))).WithMessage(order)
                .Must(keys => keys.All(k => k is not null && k.Value is >= 0 and <= 1 && Eased(k.Easing)))
                .WithMessage("How much of the daylight is there is a number from 0 to 1.")
                .When(r => r.Changes!.Daylight is not null);

            RuleFor(r => r.Changes!.Walls!).SetValidator(r => new LayerChangesValidator(Last(r))).When(r => r.Changes!.Walls is not null);
            RuleFor(r => r.Changes!.Lines!).SetValidator(r => new LayerChangesValidator(Last(r))).When(r => r.Changes!.Lines is not null);

            RuleFor(r => r.Changes!.Caves!.Count).LessThanOrEqualTo(MaxCaves).When(r => r.Changes!.Caves is not null);
            RuleFor(r => r.Changes!.Caves!)
                .Must((r, caves) =>
                    caves.All(c => c is not null)
                    && caves.Select(c => c.CaveId).Distinct().Count() == caves.Count
                    && caves.All(c => (r.Caves ?? []).Any(named => named is not null && named.CaveId == c.CaveId)))
                .WithMessage("A cave coloured apart is one of the caves the request names, named once.")
                .When(r => r.Changes!.Caves is not null);
            RuleForEach(r => r.Changes!.Caves!)
                .Must((r, cave) => cave is null
                    || ((cave.Walls is null || Tints(cave.Walls, Last(r))) && (cave.Lines is null || Tints(cave.Lines, Last(r)))))
                .WithMessage("A cave's colours are keys in rising order of frame, each a colour written #rrggbb and a strength from 0 to 1.")
                .When(r => r.Changes!.Caves is not null);
        });

        When(r => r.Markers is not null, () =>
        {
            static int Last(FlybyBlenderBundleRequest r) => r.Frames is { Count: > 0 and <= MaxFrames } frames ? frames.Count - 1 : MaxFrames - 1;

            RuleFor(r => r.Markers!.Count).LessThanOrEqualTo(MaxMarkerLayers);
            RuleFor(r => r.Markers!)
                .Must(layers => layers.All(l => l is not null)
                                && layers.Sum(l => l.Markers?.Count ?? 0) <= MaxMarkers
                                && layers.Count(l => l.Kind == FlybyBundleMarkerKind.Entrances) <= 1
                                && layers.Count(l => l.Kind == FlybyBundleMarkerKind.SurfaceFeatures) <= 1)
                .WithMessage(
                    $"A bundle holds at most {MaxMarkers} markers, in one layer of entrances, one of surface features and layers of chosen features.");
            RuleForEach(r => r.Markers!).ChildRules(layer =>
            {
                layer.RuleFor(l => l.Kind).NotNull().IsInEnum();
                layer.RuleFor(l => l.Colour).NotNull().Must(Colour).WithMessage("A colour is written #rrggbb.");
                layer.RuleFor(l => l.Markers).NotNull();
                layer.RuleFor(l => l.Markers!)
                    .Must(markers => markers.All(m => m is not null
                                                      && m.FeatureId != Guid.Empty
                                                      && double.IsFinite(m.GroundM)
                                                      && m.GroundM is >= -12_000 and <= 20_000)
                                     && markers.Select(m => m.FeatureId).Distinct().Count() == markers.Count)
                    .WithMessage("A marker is an entrance or a feature named once in its layer, with the height of the ground it stands on.")
                    .When(l => l.Markers is not null);
            }).When(r => r.Markers!.All(l => l is not null));
            RuleForEach(r => r.Markers!)
                .Must((r, layer) => layer is null
                    || ((layer.Shown is null || Switched(layer.Shown, Last(r), fades: true))
                        && (layer.Opacity is null
                            || (Ordered(layer.Opacity.Select(k => (k?.Frame, k?.Easing)), Last(r))
                                && layer.Opacity.All(k => k.Value is >= 0 and <= 1 && Eased(k.Easing))))))
                .WithMessage(
                    "A layer of markers is shown and hidden at keys on frames of the clip, in rising order, each the opposite of the one "
                    + "before it — one before the clip's first frame fading into the clip —, and is there by keys in rising order of frame, "
                    + "each from 0 to 1.");
        });

        When(r => r.Photos is not null, () =>
        {
            RuleFor(r => r.Photos!.Caves).NotNull();
            RuleFor(r => r.Photos!.Caves!)
                .Must((r, caves) => caves.Distinct().Count() == caves.Count
                                    && caves.All(id => (r.Caves ?? []).Any(named => named is not null && named.CaveId == id)))
                .WithMessage("The caves whose photographs are wanted are caves the request names, each once.")
                .When(r => r.Photos!.Caves is not null);
            RuleFor(r => r.Photos!.Shown!)
                .Must((r, keys) => Switched(keys, r.Frames is { Count: > 0 and <= MaxFrames } frames ? frames.Count - 1 : MaxFrames - 1, fades: false))
                .WithMessage(
                    "The photographs are shown and hidden at keys on frames of the clip, in rising order, each the opposite of the one "
                    + "before it, at once.")
                .When(r => r.Photos!.Shown is not null);
        });

        When(r => r.Reveal is not null, () =>
        {
            RuleFor(r => r.Reveal!.By).NotNull().IsInEnum();
            RuleFor(r => r.Reveal!.StartS).InclusiveBetween(0, 86_400);
            RuleFor(r => r.Reveal!.EndS)
                .InclusiveBetween(0, 86_400)
                .GreaterThan(r => r.Reveal!.StartS)
                .WithMessage("A reveal ends after it starts.");
        });

        RuleFor(r => r.LeftOut!.Count).LessThanOrEqualTo(32).When(r => r.LeftOut is not null);
        RuleForEach(r => r.LeftOut!).IsInEnum().When(r => r.LeftOut is not null);
    }

    private static bool Even(int pixels) => pixels % 2 == 0;

    /// <summary>Whether frames rise, each on its own, from <paramref name="first"/> to the clip's last; at least one.</summary>
    private static bool Rising(IEnumerable<int?> frames, int last, int first = 0)
    {
        var before = (long)first - 1;
        var any = false;
        foreach (var frame in frames)
        {
            if (frame is not { } at || at <= before || at > last)
            {
                return false;
            }

            before = at;
            any = true;
        }

        return any;
    }

    /// <summary>
    /// Whether a layer's keys switch it in order: on frames of the clip, rising, each saying the
    /// opposite of the one before it, and taking no fewer than no frames to do it — none at all
    /// where the layer does not fade. A layer that fades may be switched before the clip's first
    /// frame, by a key whose fade is still going on that frame.
    /// </summary>
    private static bool Switched(IReadOnlyList<FlybyBundleShownKeyRequest> keys, int last, bool fades) =>
        keys.All(k => k is not null)
        && Rising(keys.Select(k => (int?)k.Frame), last, fades ? -MaxFramesBeforeTheStart : 0)
        && keys.Zip(keys.Skip(1)).All(pair => pair.First.Shown != pair.Second.Shown)
        && keys.All(k => k.FadeFrames is null or 0 || (fades && k.FadeFrames is > 0 and <= MaxFramesPastTheEnd))
        && keys.All(k => k.Frame >= 0 || (long)k.Frame + (k.FadeFrames ?? 0) > 0);

    /// <summary>
    /// Whether the keys of one track are in order: at least one, none further before the clip's
    /// first frame or past its last than a key may lie, never going back — and where several stand
    /// on one frame, no more than three, each after the first a jump.
    /// </summary>
    private static bool Ordered(IEnumerable<(int? Frame, FlybyBundleEasing? Easing)> keys, int last)
    {
        var before = int.MinValue;
        var onIt = 0;
        var any = false;
        foreach (var (frame, easing) in keys)
        {
            if (frame is not { } at || at < before || at < -MaxFramesBeforeTheStart || at > (long)last + MaxFramesPastTheEnd)
            {
                return false;
            }

            onIt = at == before ? onIt + 1 : 1;
            if (onIt > 3 || (onIt > 1 && easing != FlybyBundleEasing.Constant))
            {
                return false;
            }

            before = at;
            any = true;
        }

        return any;
    }

    private static bool Eased(FlybyBundleEasing? easing) => easing is null || Enum.IsDefined(easing.Value);

    private static bool Tints(IReadOnlyList<FlybyBundleTintKeyRequest> keys, int last) =>
        Ordered(keys.Select(k => (k?.Frame, k?.Easing)), last)
        && keys.All(k => k is not null && Colour(k.Colour) && k.Strength is >= 0 and <= 1 && Eased(k.Easing));

    private static int Keys(FlybyBundleChangesRequest changes)
    {
        static int Of(FlybyBundleLayerChangesRequest? layer) =>
            (layer?.Shown?.Count ?? 0) + (layer?.Opacity?.Count ?? 0) + (layer?.Tint?.Count ?? 0);
        return (changes.Relief?.Count ?? 0) + (changes.Sky?.Count ?? 0) + (changes.Daylight?.Count ?? 0) + Of(changes.Walls) + Of(changes.Lines)
               + (changes.Caves ?? []).Sum(c => (c?.Walls?.Count ?? 0) + (c?.Lines?.Count ?? 0));
    }

    private static bool Colour(string? value) => value is not null && ColourPattern().IsMatch(value);

    /// <summary>
    /// One frame's eight numbers, each finite and in the range its meaning has. The heights reach
    /// from the deepest cave to a camera that starts a flight from far above a country.
    /// </summary>
    private static bool Frame(double[]? frame) =>
        frame is { Length: NumbersAFrame }
        && frame.All(double.IsFinite)
        && frame[0] is >= -180 and <= 180
        && frame[1] is >= -90 and <= 90
        && frame[2] is >= -12_000 and <= 50_000_000
        && frame[3] is >= -360 and <= 720
        && frame[4] is >= -90 and <= 90
        && frame[5] is >= -180 and <= 180
        && frame[6] is > 0 and <= 100_000_000
        && frame[7] is >= 1 and <= 170;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColourPattern();

    [GeneratedRegex("^[A-Za-z]{2,3}([-_][A-Za-z0-9]{1,8}){0,3}$")]
    private static partial Regex LanguageTag();

    private sealed class LayerChangesValidator : AbstractValidator<FlybyBundleLayerChangesRequest>
    {
        public LayerChangesValidator(int last)
        {
            RuleFor(l => l.Shown!)
                .Must(keys => Switched(keys, last, fades: true))
                .WithMessage(
                    "A layer is shown and hidden at keys on frames of the clip, in rising order, each saying the opposite of the one "
                    + "before it and taking no fewer than no frames to do it; one before the clip's first frame fades into the clip.")
                .When(l => l.Shown is not null);
            RuleFor(l => l.Opacity!)
                .Must(keys => Ordered(keys.Select(k => (k?.Frame, k?.Easing)), last)
                              && keys.All(k => k.Value is >= 0 and <= 1 && Eased(k.Easing)))
                .WithMessage("How much of a layer is there is keys in rising order of frame, each from 0 to 1.")
                .When(l => l.Opacity is not null);
            RuleFor(l => l.Tint!)
                .Must(keys => Tints(keys, last))
                .WithMessage("A layer's colour is keys in rising order of frame, each a colour written #rrggbb and a strength from 0 to 1.")
                .When(l => l.Tint is not null);
        }
    }

    private sealed class CardValidator : AbstractValidator<FlybyBundleCardRequest>
    {
        public CardValidator()
        {
            RuleFor(c => c.Text).NotNull().MaximumLength(MaxTextLength);
            RuleFor(c => c.Subtitle).MaximumLength(MaxTextLength);
            RuleFor(c => c.Seconds).InclusiveBetween(0, 600);
        }
    }

    private sealed class CaptionValidator : AbstractValidator<FlybyBundleCaptionRequest>
    {
        public CaptionValidator()
        {
            RuleFor(c => c.Text).NotNull().MaximumLength(MaxTextLength);
            RuleFor(c => c.StartS).InclusiveBetween(0, 86_400);
            RuleFor(c => c.EndS).InclusiveBetween(0, 86_400);
        }
    }
}
