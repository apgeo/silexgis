// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Statistics;

/// <summary>
/// The statements behind every registry-wide statistic: how many caves, how they are distributed,
/// where their quantiles fall, and how two of their measures move together.
/// </summary>
/// <remarks>
/// <para>
/// <b>The caller's access walk is composed into every statement, never applied to its results.</b>
/// This is the whole design and not a precaution. A histogram assembled from all caves and then
/// filtered would already have counted rows the caller may not read, and the shape of what remains
/// still carries them: a bin boundary moves, a total is one larger than the list beneath it, and a
/// caller who can ask the same question twice with different scopes reads a private cave out of the
/// difference. Worse, the two statistics that matter most here do not survive being filtered
/// afterwards at all — a quantile of a filtered set is not a filter of the quantiles, and a
/// regression over a filtered set is not the regression restricted. So the walk rides inside the
/// <c>WHERE</c> of the statement that does the arithmetic, and a row the caller may not read is
/// never summed, never bucketed and never ordered.
/// </para>
/// <para>
/// <b>Two access rules, and which statement gets which.</b> A cave the caller may read but may not
/// place exactly is a full member of the registry: it is counted in totals, it falls in a histogram
/// bin, and it contributes to a quantile and to a regression. Leaving it out would make every total
/// one smaller for each protected cave, and a caller comparing that total against the list of caves
/// they can see would learn exactly how many are hidden from them. But the moment a statement
/// groups by a place or is scoped to one, that same cave must not appear: a count under a named area
/// or a named region including it would say where it is, which is the one thing its protection
/// exists to withhold. So statements scoped to an area or grouped by region carry the exact-view
/// fragment as well as the visibility one, and every other statement carries only the visibility
/// fragment. Both halves of that rule are load-bearing — implementing one without the other is
/// either a disclosure or a wrong total.
/// </para>
/// <para>
/// <b>Nothing is ordered by a computed total.</b> The one breakdown here orders by its grouping key.
/// An ordering over quantities that were withheld or merged away publishes their relative sizes even
/// when the quantities themselves are not printed, which is the ordinary way an aggregate gives up
/// what it declined to state.
/// </para>
/// <para>
/// <b>Membership in an area is declared, never spatial</b> — the containment chain somebody entered.
/// Written as array containment with the column on the left, because the index over that column
/// serves the containment operators in that spelling and does not serve a scalar compared against
/// <c>ANY</c> of it; the two read alike and plan nothing alike.
/// </para>
/// </remarks>
public static class RegistryStatisticsSql
{
    /// <summary>
    /// The cave column each measure reads. Never a caller-supplied string: a column name cannot be
    /// a parameter, so the only safe way to interpolate one is for it never to have come from
    /// outside, and the enumeration is what guarantees that.
    /// </summary>
    public static string ColumnOf(RegistryMeasure measure) => measure switch
    {
        RegistryMeasure.SurveyedLength => "surveyed_length",
        RegistryMeasure.EstimatedLength => "estimated_length",
        RegistryMeasure.Depth => "depth",
        RegistryMeasure.PositiveDepth => "positive_depth",
        RegistryMeasure.NegativeDepth => "negative_depth",
        RegistryMeasure.RealExtension => "real_extension",
        RegistryMeasure.ProjectedExtension => "projected_extension",
        RegistryMeasure.Volume => "volume",
        RegistryMeasure.Area => "area",
        RegistryMeasure.RamificationIndex => "ramification_index",
        _ => throw new ArgumentOutOfRangeException(
            nameof(measure), measure, "no cave column is declared for this measure."),
    };

    /// <summary>
    /// The scope as a <c>WHERE</c> body, with the caller's walk inside it, and the parameters it
    /// needs.
    /// </summary>
    /// <param name="spatial">
    /// Whether the statement being built says something about where caves are. When it does, the
    /// exact-view fragment joins the visibility one and a cave the caller may read but may not place
    /// leaves the answer entirely — because a count under a place, including it, would say it is
    /// there. When it does not, only visibility applies and that cave is counted like any other.
    /// </param>
    public static (string Where, DynamicParameters Parameters) BuildScope(
        AccessContext ctx, RegistryStatisticsScope scope, bool spatial)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");

        // The soft-delete guard is written out: the visibility fragment does not carry one, because
        // the query side of this codebase gets it from a filter the raw statements never see.
        var clauses = new List<string>
        {
            "f.deleted_at IS NULL",
            $"f.kind = {(short)FeatureKind.Cave}",
            visibleSql,
        };

        if (spatial)
        {
            clauses.Add(exactSql);
        }

        if (scope.AreaId is { } areaId)
        {
            parameters.Add("rs_area_id", areaId);

            // The area is its own ancestor in the stored closure, so it is excluded explicitly or a
            // karst area would count as one of the caves inside itself.
            clauses.Add("f.ancestor_ids @> ARRAY[@rs_area_id]::uuid[]");
            clauses.Add("f.id <> @rs_area_id");
        }

        if (scope.CaveTypeId is { } caveTypeId)
        {
            parameters.Add("rs_cave_type_id", caveTypeId);
            clauses.Add("c.cave_type_id = @rs_cave_type_id");
        }

        if (scope.RockTypeId is { } rockTypeId)
        {
            parameters.Add("rs_rock_type_id", rockTypeId);
            clauses.Add("c.rock_type_id = @rs_rock_type_id");
        }

        if (scope.Region is { } region)
        {
            parameters.Add("rs_region", region);
            clauses.Add("c.region = @rs_region");
        }

        return (string.Join("\n  AND ", clauses), parameters);
    }

    /// <summary>
    /// How many caves are in scope, how many of them record the measure, and the range it covers.
    /// </summary>
    /// <remarks>
    /// The count of caves and the count that recorded something are both published, because the
    /// difference between them is the honest answer to "how much of this registry is measured" and
    /// a reader given only the second would take a total over forty caves for a total over four
    /// hundred.
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildBounds(
        AccessContext ctx, RegistryStatisticsScope scope, RegistryMeasure measure)
    {
        var column = ColumnOf(measure);
        var (where, parameters) = BuildScope(ctx, scope, scope.IsSpatiallyScoped);

        var sql = $"""
            SELECT COUNT(*)::int AS "CaveCount",
                   COUNT(c.{column})::int AS "MeasuredCount",
                   MIN(c.{column})::double precision AS "Minimum",
                   MAX(c.{column})::double precision AS "Maximum"
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The histogram: how many caves fall in each of <paramref name="binCount"/> equal sectors
    /// between two bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bucketing is done by the database rather than by reading every value back and counting
    /// them here, so the arithmetic happens where the access walk already is. An empty sector is
    /// absent from the result — the database has nothing to group — and the caller fills it back in
    /// as a zero, because a histogram with holes in its axis is not a histogram.
    /// </para>
    /// <para>
    /// The bucketing function puts anything above the upper bound in a sector of its own past the
    /// last, which would silently drop the largest cave when the bounds are the observed range. It
    /// is clamped into the topmost sector instead, closing that edge; the lower edge needs no such
    /// help because it is already closed.
    /// </para>
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildHistogram(
        AccessContext ctx,
        RegistryStatisticsScope scope,
        RegistryMeasure measure,
        double lowerBound,
        double upperBound,
        int binCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(binCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lowerBound, upperBound);

        var column = ColumnOf(measure);
        var (where, parameters) = BuildScope(ctx, scope, scope.IsSpatiallyScoped);
        parameters.Add("rs_lower", lowerBound);
        parameters.Add("rs_upper", upperBound);
        parameters.Add("rs_bins", binCount);

        var sql = $"""
            SELECT LEAST(
                       GREATEST(
                           width_bucket(
                               c.{column}::double precision, @rs_lower, @rs_upper, @rs_bins),
                           1),
                       @rs_bins) AS "Bucket",
                   COUNT(*)::int AS "Count"
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
              AND c.{column} IS NOT NULL
            GROUP BY 1
            ORDER BY 1
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The measure's quantiles, interpolated between the two observations each fraction falls
    /// between rather than snapped to one of them.
    /// </summary>
    /// <remarks>
    /// Asked for as one array in one statement, so the whole set of quantiles is taken over one
    /// ordering of one access-filtered set. Asking for them one at a time would order the same rows
    /// once per fraction, and — worse — would leave open the possibility of two of them being taken
    /// over sets that had changed between statements, so a published median could sit outside its
    /// own published quartiles.
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildPercentiles(
        AccessContext ctx,
        RegistryStatisticsScope scope,
        RegistryMeasure measure,
        IReadOnlyList<double> fractions)
    {
        ArgumentNullException.ThrowIfNull(fractions);
        if (fractions.Count == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fractions), "a quantile statement with no fractions asks nothing.");
        }

        var column = ColumnOf(measure);
        var (where, parameters) = BuildScope(ctx, scope, scope.IsSpatiallyScoped);
        parameters.Add("rs_fractions", fractions.ToArray());

        var sql = $"""
            SELECT percentile_cont(@rs_fractions::double precision[])
                       WITHIN GROUP (ORDER BY c.{column}::double precision)
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
              AND c.{column} IS NOT NULL
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// How two measures move together, over the caves that record both.
    /// </summary>
    /// <param name="logarithmic">
    /// When true the fit is taken over the logarithms of both measures, which is the form the
    /// relationship between a cave's length and its depth actually takes: on linear axes it is a
    /// curve and a straight line through it means nothing, while on logarithmic ones it is a line
    /// whose slope is the figure worth comparing between regions. Only strictly positive values have
    /// a logarithm, so a cave recorded as zero on either axis contributes to nothing rather than
    /// being nudged to a small number, and the published pair count says how many were left.
    /// </param>
    public static (string Sql, DynamicParameters Parameters) BuildCorrelation(
        AccessContext ctx,
        RegistryStatisticsScope scope,
        RegistryMeasure x,
        RegistryMeasure y,
        bool logarithmic)
    {
        var xColumn = ColumnOf(x);
        var yColumn = ColumnOf(y);
        var (where, parameters) = BuildScope(ctx, scope, scope.IsSpatiallyScoped);

        var xExpression = logarithmic
            ? $"ln(c.{xColumn}::double precision)"
            : $"c.{xColumn}::double precision";
        var yExpression = logarithmic
            ? $"ln(c.{yColumn}::double precision)"
            : $"c.{yColumn}::double precision";
        var positive = logarithmic
            ? $"AND c.{xColumn} > 0 AND c.{yColumn} > 0"
            : string.Empty;

        // The regression aggregates take the dependent value first, which is the opposite of how
        // the pair reads in the request, and getting it the wrong way round yields a slope that is
        // wrong by the ratio of the two variances rather than an error anybody would notice.
        var sql = $"""
            SELECT COUNT(*)::int AS "Count",
                   regr_slope({yExpression}, {xExpression}) AS "Slope",
                   regr_intercept({yExpression}, {xExpression}) AS "Intercept",
                   regr_r2({yExpression}, {xExpression}) AS "RSquared",
                   corr({yExpression}, {xExpression}) AS "Correlation",
                   {(logarithmic ? "true" : "false")} AS "Logarithmic"
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
              AND c.{xColumn} IS NOT NULL
              AND c.{yColumn} IS NOT NULL
              {positive}
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// How many caves stand under each region in the scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A breakdown by place, so it is computed <b>only over caves the caller may place exactly</b>.
    /// A cave whose position is closed to them is counted in every total on this page and appears
    /// under no region, because a region is a place and a count under one is a statement about where
    /// the cave is.
    /// </para>
    /// <para>
    /// Ordered by the region, never by the count. An ordering by a computed total would rank the
    /// regions by quantities the merge rule may have joined away, so a reader could recover the
    /// relative sizes of things the answer declined to state.
    /// </para>
    /// <para>
    /// Caves with no region recorded make a row of their own rather than being dropped: how much of
    /// a registry has been placed at all is part of what a breakdown says.
    /// </para>
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildRegionBreakdown(
        AccessContext ctx, RegistryStatisticsScope scope)
    {
        var (where, parameters) = BuildScope(ctx, scope, spatial: true);

        var sql = $"""
            SELECT c.region AS "Region",
                   COUNT(*)::int AS "CaveCount"
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
            GROUP BY c.region
            ORDER BY c.region NULLS LAST
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// How many caves are in scope, without reference to any measure.
    /// </summary>
    /// <remarks>
    /// Counted under readability alone whenever the scope names no place, so it is the total a
    /// breakdown by place is read against: the rows of that breakdown are taken over the caves the
    /// caller may place, and the difference between their sum and this figure is how many caves in
    /// scope stand under no place. Publishing both is deliberate — a breakdown whose rows quietly
    /// summed to less than the total would read as a counting error.
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildCaveCount(
        AccessContext ctx, RegistryStatisticsScope scope)
    {
        var (where, parameters) = BuildScope(ctx, scope, scope.IsSpatiallyScoped);

        var sql = $"""
            SELECT COUNT(*)::int
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The recorded values themselves, in ascending order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only statement here that reads rows rather than a summary of them, and it is read for
    /// the two distribution fits, which are maximum-likelihood estimates no database offers as an
    /// aggregate. The lognormal could be assembled from sums of logarithms, but the power-law tail
    /// could not: choosing where the tail begins means scoring candidate lower bounds against the
    /// sample's own cumulative distribution, which is an ordering problem over the values and not a
    /// quantity that can be summed.
    /// </para>
    /// <para>
    /// It is affordable because it is one column of one type over the caves of one registry, and
    /// safe because it is the same statement as the others with the same walk inside it: what comes
    /// back is a bag of numbers with no identity attached, cut to what the caller may read before
    /// the database sent it. Nothing here associates a value with a cave, and nothing may — the
    /// fits need the multiset and an ordered list of lengths beside a list of names would be the
    /// registry itself.
    /// </para>
    /// </remarks>
    public static (string Sql, DynamicParameters Parameters) BuildSample(
        AccessContext ctx, RegistryStatisticsScope scope, RegistryMeasure measure)
    {
        var column = ColumnOf(measure);
        var (where, parameters) = BuildScope(ctx, scope, scope.IsSpatiallyScoped);

        var sql = $"""
            SELECT c.{column}::double precision
            FROM features f
            JOIN caves c ON c.id = f.id
            WHERE {where}
              AND c.{column} IS NOT NULL
            ORDER BY 1
            """;

        return (sql, parameters);
    }
}
