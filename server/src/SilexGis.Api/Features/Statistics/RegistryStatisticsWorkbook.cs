// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Statistics;

namespace SilexGis.Api.Features.Statistics;

/// <summary>
/// The registry answers written out as sheet rows.
/// </summary>
/// <remarks>
/// <para>
/// Every method here takes an answer that has already been computed and asks the database nothing.
/// That is the mechanical reason a saved copy cannot state what the screen refuses: there is no
/// second path to the figures, only a second rendering of the same object, so a rule added to the
/// answering method is a rule the file gets too, and one removed from it is removed from both.
/// </para>
/// <para>
/// Labels are English. The application's translations live with the pages that use them; the server
/// holds no catalogue of its own, and a file that carried one would have to guess which language its
/// eventual reader speaks.
/// </para>
/// </remarks>
internal static class RegistryStatisticsWorkbook
{
    internal const string SheetName = "Registry statistics";

    internal static IReadOnlyList<IReadOnlyList<SheetCell>> DistributionRows(
        RegistryDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(distribution);

        var rows = new List<IReadOnlyList<SheetCell>>();
        rows.Add([SheetCell.Title("Measure"), SheetCell.Of(Label(distribution.Measure))]);
        rows.Add(Figure("Caves in scope", distribution.CaveCount));
        rows.Add(Figure("Caves recording it", distribution.MeasuredCount));
        rows.Add(Figure("Smallest recorded", distribution.Minimum));
        rows.Add(Figure("Largest recorded", distribution.Maximum));
        rows.Add([]);
        rows.Add(
        [
            SheetCell.Title("From"),
            SheetCell.Title("To"),
            SheetCell.Title("Caves"),
            SheetCell.Title("Joined sectors"),
        ]);

        foreach (var bin in distribution.Bins)
        {
            rows.Add(
            [
                SheetCell.Of(bin.LowerBound),
                SheetCell.Of(bin.UpperBound),
                SheetCell.Of(bin.Count),

                // Stated in the file as it is on the screen. A joined sector is a wider sector, and
                // a reader who cannot see which ones were joined would read the wide ones as a
                // property of the registry rather than of what may be published about it.
                SheetCell.Of(bin.Merged ? "joined" : string.Empty),
            ]);
        }

        rows.Add([]);
        rows.Add([SheetCell.Title("Quantile"), SheetCell.Title("Value")]);
        foreach (var percentile in distribution.Percentiles)
        {
            rows.Add([SheetCell.Of(percentile.Fraction), SheetCell.Of(percentile.Value)]);
        }

        if (distribution.Lognormal is { } lognormal)
        {
            rows.Add([]);
            rows.Add([SheetCell.Title("Lognormal fit")]);
            rows.Add(Figure("Mean of logarithms", lognormal.Mu));
            rows.Add(Figure("Deviation of logarithms", lognormal.Sigma));
            rows.Add(Figure("Fitted median", lognormal.Median));
            rows.Add(Figure("Fitted mean", lognormal.Mean));
            rows.Add(Figure("Values fitted", lognormal.Count));
        }

        if (distribution.ParetoTail is { } tail)
        {
            rows.Add([]);
            rows.Add([SheetCell.Title("Power-law tail")]);
            rows.Add(Figure("Exponent", tail.Alpha));
            rows.Add(Figure("Exponent standard error", tail.AlphaStandardError));
            rows.Add(Figure("Tail begins at", tail.LowerBound));
            rows.Add(Figure("Values in the tail", tail.TailCount));
            rows.Add(Figure("Largest gap from the fit", tail.KolmogorovSmirnov));
        }

        rows.Add([]);
        rows.Add([SheetCell.Of(distribution.Basis)]);
        return rows;
    }

    internal static IReadOnlyList<IReadOnlyList<SheetCell>> CorrelationRows(
        RegistryCorrelationDto correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        return
        [
            [SheetCell.Title("Horizontal measure"), SheetCell.Of(Label(correlation.X))],
            [SheetCell.Title("Vertical measure"), SheetCell.Of(Label(correlation.Y))],
            [
                SheetCell.Of("Axes"),
                SheetCell.Of(correlation.Logarithmic ? "logarithmic" : "linear"),
            ],
            Figure("Caves recording both", correlation.Count),
            Figure("Slope", correlation.Slope),
            Figure("Intercept", correlation.Intercept),
            Figure("Variation accounted for", correlation.RSquared),
            Figure("Correlation", correlation.Correlation),
            [],
            [SheetCell.Of(correlation.Basis)],
        ];
    }

    internal static IReadOnlyList<IReadOnlyList<SheetCell>> RegionRows(
        RegistryRegionBreakdownDto breakdown)
    {
        ArgumentNullException.ThrowIfNull(breakdown);

        var rows = new List<IReadOnlyList<SheetCell>>();
        rows.Add(Figure("Caves in scope", breakdown.CaveCount));
        rows.Add([]);
        rows.Add([SheetCell.Title("Region"), SheetCell.Title("Caves")]);

        foreach (var region in breakdown.Regions)
        {
            // An unrecorded region is a row of its own and is labelled as one, because how much of
            // a registry has been placed at all is part of what the breakdown says.
            rows.Add(
            [
                SheetCell.Of(region.Region ?? "(no region recorded)"),
                SheetCell.Of(region.CaveCount),
            ]);
        }

        rows.Add([]);
        rows.Add([SheetCell.Of(breakdown.Basis)]);
        return rows;
    }

    private static IReadOnlyList<SheetCell> Figure(string label, double? value) =>
        [SheetCell.Of(label), SheetCell.Of(value)];

    /// <summary>The English name of a measure, exhaustively, so a new one cannot ship unlabelled.</summary>
    private static string Label(RegistryMeasure measure) => measure switch
    {
        RegistryMeasure.SurveyedLength => "Surveyed length (m)",
        RegistryMeasure.EstimatedLength => "Estimated length (m)",
        RegistryMeasure.Depth => "Depth (m)",
        RegistryMeasure.PositiveDepth => "Height above the entrance (m)",
        RegistryMeasure.NegativeDepth => "Depth below the entrance (m)",
        RegistryMeasure.RealExtension => "Real extension (m)",
        RegistryMeasure.ProjectedExtension => "Projected extension (m)",
        RegistryMeasure.Volume => "Volume (m³)",
        RegistryMeasure.Area => "Area (m²)",
        RegistryMeasure.RamificationIndex => "Ramification index",
        _ => throw new ArgumentOutOfRangeException(
            nameof(measure), measure, "no label is declared for this measure."),
    };
}
