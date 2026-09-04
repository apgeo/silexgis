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

    private static double? Normalise(double? value, double reference) =>
        value is null || !double.IsFinite(value.Value) || reference <= 0d
            ? null
            : Math.Clamp(value.Value / reference, 0d, 1d);
}
