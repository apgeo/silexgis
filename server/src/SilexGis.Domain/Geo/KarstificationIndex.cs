// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>How karstified an area reads, as one of five classes.</summary>
public enum KarstificationClass : short
{
    /// <summary>Nothing measured, or nothing to measure it over.</summary>
    Unknown = 0,
    VeryLow = 1,
    Low = 2,
    Moderate = 3,
    High = 4,
    VeryHigh = 5,
}

/// <summary>
/// One of the readings the karstification index is built from, kept separately so the payload can
/// say which ones existed. An index that quietly drops a missing component reads as a low score for
/// an area nobody has surveyed the depressions of, which is a different statement entirely.
/// </summary>
/// <param name="Name">A stable identifier for the component, so a client can label it.</param>
/// <param name="Value">The measured value in its own units, or null when it could not be measured.</param>
/// <param name="Normalised">
/// The value placed on a nought-to-one scale against the reference below, or null when the
/// component is absent.
/// </param>
/// <param name="Reference">
/// The value that scores one. It is a stated convention, not a measurement: an index is a ranking
/// device, and without a published reference two installations' numbers cannot be compared.
/// </param>
public sealed record KarstificationComponent(string Name, double? Value, double? Normalised, double Reference);

/// <summary>An area's karstification reading, with the figures it was derived from.</summary>
/// <param name="AreaKm2">The outline's ground area, or null when it encloses none.</param>
/// <param name="CavesPerKm2">Caves over that ground, or null without a denominator.</param>
/// <param name="DepressionAreaRatio">Share of the ground under mapped depressions, or null when
/// none are mapped.</param>
/// <param name="Components">The index's components, absent ones included.</param>
/// <param name="Score">The index, or null when no component could be measured.</param>
/// <param name="Class">The class the score falls in.</param>
public sealed record KarstificationReading(
    double? AreaKm2,
    double? CavesPerKm2,
    double? DepressionAreaRatio,
    IReadOnlyList<KarstificationComponent> Components,
    double? Score,
    KarstificationClass Class);

/// <summary>
/// A composite reading of how karstified an area is, combining what has actually been recorded
/// about it.
///
/// <para>
/// <b>It is a ranking, not a measurement.</b> There is no natural unit for karstification, so the
/// index is a mean of normalised components against published reference values, and every number it
/// produces is only meaningful next to another area's computed the same way. The reference values
/// are constants here rather than configuration precisely so that two installations rank alike.
/// </para>
/// <para>
/// <b>Missing components are reported, never assumed to be zero.</b> The depression component
/// requires depression outlines to have been drawn; most registries have caves long before they
/// have dolines. Scoring an unmapped area zero on that component would rank it below an area that
/// genuinely has no depressions, so an absent component is left out of the mean and named in the
/// payload as absent.
/// </para>
/// </summary>
public static class KarstificationIndex
{
    /// <summary>The cave density, in caves per square kilometre, that scores one.</summary>
    public const string CaveDensityComponent = "cave_density";

    /// <summary>
    /// Five caves to the square kilometre is dense karst by any registry's standard; areas above it
    /// score one rather than being ranked against each other, because past that point the index is
    /// no longer what distinguishes them.
    /// </summary>
    public const double CaveDensityReferencePerKm2 = 5d;

    /// <summary>The share of the area covered by mapped depressions that scores one.</summary>
    public const string DepressionAreaRatioComponent = "depression_area_ratio";

    /// <summary>
    /// A tenth of the ground under mapped depressions. Plateau karst reaches it; anything higher is
    /// already at the top of the scale.
    /// </summary>
    public const double DepressionAreaRatioReference = 0.10d;

    /// <summary>
    /// The components of an index, given what could be measured. A null value means the reading
    /// could not be taken at all — no depressions are stored, or the area encloses no ground —
    /// and is passed through as an absent component rather than a zero.
    /// </summary>
    public static IReadOnlyList<KarstificationComponent> Components(
        double? cavesPerKm2, double? depressionAreaRatio) =>
    [
        new KarstificationComponent(
            CaveDensityComponent,
            cavesPerKm2,
            Normalise(cavesPerKm2, CaveDensityReferencePerKm2),
            CaveDensityReferencePerKm2),
        new KarstificationComponent(
            DepressionAreaRatioComponent,
            depressionAreaRatio,
            Normalise(depressionAreaRatio, DepressionAreaRatioReference),
            DepressionAreaRatioReference),
    ];

    /// <summary>
    /// The index itself: the mean of the components that could be measured, or null when none
    /// could.
    /// </summary>
    public static double? Score(IReadOnlyList<KarstificationComponent> components)
    {
        var present = components.Where(c => c.Normalised is not null).ToList();

        return present.Count == 0 ? null : present.Average(c => c.Normalised!.Value);
    }

    /// <summary>
    /// The class a score falls in. The breaks are even fifths of the scale: the components are
    /// already normalised against their references, so the classing has no further judgment to add
    /// and an even split is the one a reader can reconstruct from the number.
    /// </summary>
    public static KarstificationClass Classify(double? score) => score switch
    {
        null => KarstificationClass.Unknown,
        < 0.2d => KarstificationClass.VeryLow,
        < 0.4d => KarstificationClass.Low,
        < 0.6d => KarstificationClass.Moderate,
        < 0.8d => KarstificationClass.High,
        _ => KarstificationClass.VeryHigh,
    };

    /// <summary>
    /// The whole reading for one area from its totals: the per-square-kilometre figures, the
    /// components, the score and the class. Every route that publishes an index derives it here, so
    /// an area read on its own page and the same area read in a map window cannot disagree.
    /// </summary>
    /// <param name="areaM2">The outline's ground area, or null when it has none.</param>
    /// <param name="caveCount">Caves counted in the area.</param>
    /// <param name="depressionCount">Depression outlines counted in it.</param>
    /// <param name="depressionAreaM2">Ground under those outlines, summed.</param>
    public static KarstificationReading Read(
        double? areaM2, int caveCount, int depressionCount, double? depressionAreaM2)
    {
        var areaKm2 = areaM2 is { } m2 && m2 > 0d ? m2 / 1_000_000d : (double?)null;

        // No depression outlines means the reading could not be taken, not that the reading is
        // nought. Most registries hold caves long before anybody draws a doline, and scoring an
        // unmapped area zero on this component would rank it below an area genuinely without
        // depressions. The two are indistinguishable from here, so the honest answer is silence.
        var depressionRatio = areaM2 is { } ground && ground > 0d && depressionCount > 0
            ? (depressionAreaM2 ?? 0d) / ground
            : (double?)null;

        var cavesPerKm2 = areaKm2 is { } km2 ? caveCount / km2 : (double?)null;
        var components = Components(cavesPerKm2, depressionRatio);
        var score = Score(components);

        return new KarstificationReading(
            areaKm2, cavesPerKm2, depressionRatio, components, score, Classify(score));
    }

    private static double? Normalise(double? value, double reference) =>
        value is null || !double.IsFinite(value.Value) || reference <= 0d
            ? null
            : Math.Clamp(value.Value / reference, 0d, 1d);
}
