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
/// <param name="Lines">Whether survey lines are drawn at all.</param>
/// <param name="Sky">What stands behind everything, <c>#rrggbb</c>; left out, black.</param>
/// <param name="LineColour">One colour for every line; left out, the colours by depth.</param>
public sealed record FlybyBundleLookRequest(
    bool Lines,
    string? Sky,
    string? LineColour,
    IReadOnlyList<FlybyBundleDepthBandRequest>? DepthBands);

/// <summary>A card of words over the picture.</summary>
/// <param name="Seconds">
/// For the title, how long after the clip's first frame it has gone; for the end card, how long
/// before its last frame it comes.
/// </param>
public sealed record FlybyBundleCardRequest(string Text, string? Subtitle, double Seconds);

/// <summary>Words shown between two moments of the clip, in seconds from its first frame.</summary>
public sealed record FlybyBundleCaptionRequest(string Text, double StartS, double EndS);

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
/// <param name="Relief">How many times taller than true the scene draws every height.</param>
/// <param name="LeftOut">What the flyby shows that a bundle cannot carry, by name.</param>
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
    IReadOnlyList<FlybyBundleOmission>? LeftOut);

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

    private const int NumbersAFrame = 8;
    private const int MaxTextLength = 500;
    private const int MaxCaptions = 500;

    public FlybyBlenderBundleRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(120);

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

        RuleFor(r => r.LeftOut!.Count).LessThanOrEqualTo(32).When(r => r.LeftOut is not null);
        RuleForEach(r => r.LeftOut!).IsInEnum().When(r => r.LeftOut is not null);
    }

    private static bool Even(int pixels) => pixels % 2 == 0;

    private static bool Colour(string value) => value is not null && ColourPattern().IsMatch(value);

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
