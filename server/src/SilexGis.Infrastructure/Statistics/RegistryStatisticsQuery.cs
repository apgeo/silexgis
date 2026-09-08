// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Statistics;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Statistics;

/// <summary>
/// The registry read as a dataset: what a measure looks like over the caves a caller may see, and
/// how two measures move together.
/// </summary>
/// <remarks>
/// <para>
/// It sits beside the statements that fill it rather than in the surface that returns it, because
/// more than one surface returns it — a screen and a saved file — and the answer has to be the same
/// object each time. A file assembled by its own path is how a saved copy comes to state what the
/// screen refused.
/// </para>
/// <para>
/// <b>The caller's visibility walk is composed into every statement, never applied to its results.</b>
/// The reasoning is written out where the statements are; the short version is that a quantile of a
/// filtered set is not a filter of the quantiles, so an aggregate computed first and cut afterwards
/// is not a smaller true answer — it is a different, wrong one that happens to have been computed
/// over rows the caller was not allowed to see.
/// </para>
/// <para>
/// <b>Every answer states the basis it was computed on.</b> Two people with different access get
/// different figures for the same registry and both are right; a figure with no basis beside it
/// invites the reader to take it for the whole truth and to treat somebody else's copy as a
/// contradiction.
/// </para>
/// </remarks>
public static class RegistryStatisticsQuery
{
    /// <summary>What the figures were computed over, when the answer is about measurements.</summary>
    public const string ReadableBasis =
        "Computed over the caves you may read. Somebody with different access sees different "
        + "figures for the same registry, and both are right.";

    /// <summary>
    /// What the figures were computed over, when the answer is broken down by place but is not
    /// itself narrowed to one: the total is every cave in scope the caller may read, and the rows
    /// beneath it only those they may also place.
    /// </summary>
    public const string PlaceableBasis =
        "Computed over the caves you may read and may place exactly. A cave whose position is "
        + "closed to you is counted in the registry's totals and appears under no place, because a "
        + "count under a place would say where it is.";

    /// <summary>
    /// What the figures were computed over when the question itself names a place. It is a
    /// different sentence from the one above and not a variation of it: narrowing to an area takes
    /// a cave the caller may not place out of the <b>total</b> as well, so a reader told only that
    /// such a cave "is counted in the totals" would add the rows up, find they reconcile, and
    /// conclude that nothing had been withheld.
    /// </summary>
    public const string AreaBasis =
        "Computed over the caves you may read and may place exactly, inside the area you named. A "
        + "cave whose position is closed to you is absent from these figures altogether, totals "
        + "included, because a count inside a named area would say it is there.";

    /// <summary>
    /// A measure summarised over a scope: its extent, its histogram, its quantiles and the two
    /// distribution fits, in one pass.
    /// </summary>
    /// <param name="binCount">Sectors the histogram is asked for, before the publication rule joins
    /// any that hold too few caves. Bounded by the request's own validation.</param>
    /// <param name="minimumBinCaveCount">The floor a published sector must clear.</param>
    public static async Task<RegistryDistribution> DistributionAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        RegistryStatisticsScope scope,
        RegistryMeasure measure,
        int binCount,
        int minimumBinCaveCount,
        IReadOnlyList<double> fractions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(scope);

        var basis = scope.IsSpatiallyScoped ? AreaBasis : ReadableBasis;
        var connection = db.Database.GetDbConnection();

        var (boundsSql, boundsParameters) = RegistryStatisticsSql.BuildBounds(ctx, scope, measure);
        var bounds = await connection.QuerySingleAsync<RegistryMeasureBoundsRow>(
            new CommandDefinition(boundsSql, boundsParameters, cancellationToken: ct));

        // Nothing recorded, or every cave recording the same value: there is no range to divide, and
        // a histogram over a range of zero width is an arithmetic accident rather than an answer.
        if (bounds.MeasuredCount == 0
            || bounds.Minimum is not { } minimum
            || bounds.Maximum is not { } maximum
            || minimum >= maximum)
        {
            return new RegistryDistribution(
                measure,
                bounds.CaveCount,
                bounds.MeasuredCount,
                bounds.Minimum,
                bounds.Maximum,
                [],
                [],
                null,
                null,
                basis);
        }

        var bins = await HistogramAsync(
            connection, ctx, scope, measure, minimum, maximum, binCount, minimumBinCaveCount, ct);

        var (percentileSql, percentileParameters) =
            RegistryStatisticsSql.BuildPercentiles(ctx, scope, measure, fractions);
        var quantiles = await connection.QuerySingleOrDefaultAsync<double[]>(
            new CommandDefinition(percentileSql, percentileParameters, cancellationToken: ct));

        var percentiles = new List<RegistryPercentileRow>(fractions.Count);
        for (var i = 0; i < fractions.Count; i++)
        {
            percentiles.Add(new RegistryPercentileRow(
                fractions[i], quantiles is not null && i < quantiles.Length ? quantiles[i] : null));
        }

        var (sampleSql, sampleParameters) =
            RegistryStatisticsSql.BuildSample(ctx, scope, measure);
        var sample = (await connection.QueryAsync<double>(
            new CommandDefinition(sampleSql, sampleParameters, cancellationToken: ct))).ToList();

        return new RegistryDistribution(
            measure,
            bounds.CaveCount,
            bounds.MeasuredCount,
            minimum,
            maximum,
            bins,
            percentiles,
            DistributionFits.FitLognormal(sample),
            DistributionFits.FitParetoTail(sample),
            basis);
    }

    /// <summary>
    /// How two measures move together over the caves in scope that record both.
    /// </summary>
    public static async Task<RegistryCorrelationRow> CorrelationAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        RegistryStatisticsScope scope,
        RegistryMeasure x,
        RegistryMeasure y,
        bool logarithmic,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var (sql, parameters) = RegistryStatisticsSql.BuildCorrelation(ctx, scope, x, y, logarithmic);

        return await db.Database.GetDbConnection().QuerySingleAsync<RegistryCorrelationRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    /// <summary>
    /// How many caves are in scope at all — the total a breakdown by place is read against.
    /// </summary>
    public static async Task<int> CaveCountAsync(
        SilexGisDbContext db, AccessContext ctx, RegistryStatisticsScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var (sql, parameters) = RegistryStatisticsSql.BuildCaveCount(ctx, scope);

        return await db.Database.GetDbConnection().ExecuteScalarAsync<int>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    /// <summary>
    /// How many caves stand under each region in scope, over the caves the caller may place exactly.
    /// </summary>
    public static async Task<IReadOnlyList<RegistryRegionRow>> RegionsAsync(
        SilexGisDbContext db, AccessContext ctx, RegistryStatisticsScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var (sql, parameters) = RegistryStatisticsSql.BuildRegionBreakdown(ctx, scope);
        var rows = await db.Database.GetDbConnection().QueryAsync<RegistryRegionRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>
    /// The histogram, with the sectors the database had nothing to put in filled back in as zeroes
    /// and the publication rule applied to the result.
    /// </summary>
    /// <remarks>
    /// The zero-filling comes first and the joining second, deliberately. A sector the database
    /// omitted is empty, and an empty sector stands — it describes nothing and identifies nobody.
    /// Joining before filling would have made the empty stretches vanish into their neighbours and
    /// lost the shape the histogram was drawn for.
    /// </remarks>
    private static async Task<IReadOnlyList<DistributionBin>> HistogramAsync(
        System.Data.Common.DbConnection connection,
        AccessContext ctx,
        RegistryStatisticsScope scope,
        RegistryMeasure measure,
        double minimum,
        double maximum,
        int binCount,
        int minimumBinCaveCount,
        CancellationToken ct)
    {
        var (sql, parameters) = RegistryStatisticsSql.BuildHistogram(
            ctx, scope, measure, minimum, maximum, binCount);
        var counted = await connection.QueryAsync<RegistryBinRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        var byBucket = counted.ToDictionary(row => row.Bucket, row => row.Count);
        var width = (maximum - minimum) / binCount;
        var raw = new List<DistributionBin>(binCount);
        for (var bucket = 1; bucket <= binCount; bucket++)
        {
            raw.Add(new DistributionBin(
                minimum + ((bucket - 1) * width),
                bucket == binCount ? maximum : minimum + (bucket * width),
                byBucket.TryGetValue(bucket, out var count) ? count : 0,
                false));
        }

        return DistributionBins.Merge(raw, minimumBinCaveCount);
    }
}
