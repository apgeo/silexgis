// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Statistics;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the registry statements say before any of them is run.
///
/// <para>
/// These are assertions about the text of the statements, and they are here because the properties
/// they pin are invisible in the results. An aggregate computed over rows the caller may not read
/// and cut afterwards returns a number that looks exactly like the correct one on a fixture where
/// everything is visible; an ordering by a computed total returns rows in a plausible order; a
/// containment predicate spelled the wrong way round returns the right caves after scanning every
/// feature in the installation. None of the three fails a test that only looks at what came back.
/// </para>
/// <para>
/// The caller here is built directly rather than through a login, because nothing is executed: what
/// is under test is how the statement is assembled, and it is assembled from the caller's rights
/// whoever they are.
/// </para>
/// </summary>
public class RegistryStatisticsSqlTests
{
    private static readonly AccessContext Caller =
        new(Guid.NewGuid(), isFullAdmin: false, [], []);

    private static readonly Guid AreaId = Guid.NewGuid();

    [Fact]
    public void The_access_walk_is_inside_every_statement_rather_than_around_its_results()
    {
        var scope = new RegistryStatisticsScope();

        foreach (var (name, sql) in EveryStatement(scope))
        {
            // The walk's own parameters are what identify it: a statement that carries them is one
            // whose WHERE decides which rows are aggregated, which is the property being pinned.
            sql.ShouldContain("@acc_user_id", customMessage: name);
            sql.ShouldContain("@acc_is_full_admin", customMessage: name);

            // Written out, because the raw statements do not inherit the soft-delete filter the
            // query side of this codebase gets for free.
            sql.ShouldContain("f.deleted_at IS NULL", customMessage: name);
        }
    }

    [Fact]
    public void A_statement_about_where_caves_are_asks_what_the_caller_may_place()
    {
        // A breakdown by region is a statement about places, so it carries the exact-view rule as
        // well as the readability one: a cave whose position is closed to the caller is counted in
        // the registry's totals and stands under no region.
        // The exact-view rule is recognisable by the protected root it looks for above the row;
        // the rest of it varies with what the caller happens to hold.
        const string exactViewMarker = "root.location_protected";

        var (regionSql, _) =
            RegistryStatisticsSql.BuildRegionBreakdown(Caller, new RegistryStatisticsScope());
        regionSql.ShouldContain(exactViewMarker);

        // Scoping to a karst area is the same kind of statement and gets the same treatment.
        var (scopedSql, _) = RegistryStatisticsSql.BuildBounds(
            Caller,
            new RegistryStatisticsScope(AreaId: AreaId),
            RegistryMeasure.SurveyedLength);
        scopedSql.ShouldContain(exactViewMarker);

        // And the same question asked of the whole registry does not, which is the other half of
        // the rule: dropping those caves from a total would make it one smaller for each cave
        // hidden from the caller, and the difference against the list they can see is the leak.
        var (registrySql, _) = RegistryStatisticsSql.BuildBounds(
            Caller, new RegistryStatisticsScope(), RegistryMeasure.SurveyedLength);
        registrySql.ShouldNotContain(exactViewMarker);
    }

    [Fact]
    public void Membership_of_an_area_is_the_declared_chain_spelled_the_way_the_index_serves()
    {
        var (sql, parameters) = RegistryStatisticsSql.BuildBounds(
            Caller,
            new RegistryStatisticsScope(AreaId: AreaId),
            RegistryMeasure.Depth);

        // Column on the left, which is the spelling the index over it serves; the other spelling
        // reads identically and scans every feature in the installation.
        sql.ShouldContain("f.ancestor_ids @> ARRAY[@rs_area_id]::uuid[]");
        sql.ShouldNotContain("@rs_area_id = ANY(f.ancestor_ids)");

        // The closure holds a row for the area itself, so it is excluded or a karst area counts as
        // one of the caves inside itself.
        sql.ShouldContain("f.id <> @rs_area_id");

        // Declared containment, never spatial: nothing here asks where the cave actually is.
        sql.ShouldNotContain("ST_Contains", Case.Insensitive);
        sql.ShouldNotContain("ST_Within", Case.Insensitive);

        parameters.ParameterNames.ShouldContain("rs_area_id");
    }

    [Fact]
    public void Nothing_is_ordered_by_a_quantity_the_answer_may_have_withheld()
    {
        var scope = new RegistryStatisticsScope();

        foreach (var (name, sql) in EveryStatement(scope))
        {
            // An ordering over counts publishes their relative sizes even where the counts
            // themselves were merged away, which is the ordinary way an aggregate gives up what it
            // declined to state. The breakdown orders by its grouping key instead.
            sql.ShouldNotContain("ORDER BY COUNT", Case.Insensitive, name);
            sql.ShouldNotContain("COUNT(*) DESC", Case.Insensitive, name);
        }

        var (regionSql, _) = RegistryStatisticsSql.BuildRegionBreakdown(Caller, scope);
        regionSql.ShouldContain("ORDER BY c.region NULLS LAST");
    }

    [Fact]
    public void Every_caller_supplied_value_arrives_as_a_parameter()
    {
        const string region = "'; DROP TABLE caves; --";
        var scope = new RegistryStatisticsScope(
            AreaId: AreaId, CaveTypeId: 7, RockTypeId: 9, Region: region);

        foreach (var (name, sql) in EveryStatement(scope))
        {
            sql.ShouldNotContain(region, customMessage: name);
            sql.ShouldNotContain(AreaId.ToString(), customMessage: name);
        }

        var (_, parameters) = RegistryStatisticsSql.BuildScope(Caller, scope, spatial: false);
        parameters.ParameterNames.ShouldContain("rs_region");
        parameters.ParameterNames.ShouldContain("rs_cave_type_id");
        parameters.ParameterNames.ShouldContain("rs_rock_type_id");
    }

    [Fact]
    public void A_height_above_sea_level_is_not_a_measure_that_can_be_asked_for()
    {
        // Every measure the enumeration declares has a column, so a request that names one is
        // answerable and the switch below cannot be reached by a legitimate value.
        foreach (var measure in Enum.GetValues<RegistryMeasure>())
        {
            RegistryStatisticsSql.ColumnOf(measure).ShouldNotBeNullOrWhiteSpace();
            RegistryStatisticsSql.ColumnOf(measure).ShouldNotBe("altitude");
        }

        // A value outside it is refused rather than interpolated, which is what keeps the column
        // name something that never came from outside.
        Should.Throw<ArgumentOutOfRangeException>(
            () => RegistryStatisticsSql.ColumnOf((RegistryMeasure)9999));
    }

    [Fact]
    public void A_histogram_is_refused_a_range_it_cannot_divide()
    {
        var scope = new RegistryStatisticsScope();

        Should.Throw<ArgumentOutOfRangeException>(() => RegistryStatisticsSql.BuildHistogram(
            Caller, scope, RegistryMeasure.Depth, lowerBound: 10, upperBound: 10, binCount: 5));
        Should.Throw<ArgumentOutOfRangeException>(() => RegistryStatisticsSql.BuildHistogram(
            Caller, scope, RegistryMeasure.Depth, lowerBound: 0, upperBound: 100, binCount: 0));

        // The bucketing function puts anything above the upper bound past the last sector, which
        // would drop the longest cave when the bounds are the observed range; it is clamped in.
        var (sql, _) = RegistryStatisticsSql.BuildHistogram(
            Caller, scope, RegistryMeasure.Depth, lowerBound: 0, upperBound: 100, binCount: 5);
        sql.ShouldContain("width_bucket");
        sql.ShouldContain("LEAST");
        sql.ShouldContain("GREATEST");
    }

    [Fact]
    public void The_grouping_statement_keeps_the_caves_that_record_nothing()
    {
        // The one statement here that must not filter on the columns it reads. Adding
        // "AND c.surveyed_length IS NOT NULL" would leave the groups unchanged and quietly destroy
        // the answer: the count of caves that were left out, and the count of caves each measure
        // alone cost, are computed from the gaps this statement returns. Without them a grouping
        // over the best-surveyed tenth of a register is indistinguishable from a grouping of the
        // register, which is the failure the whole surface exists to prevent.
        var (sql, _) = RegistryStatisticsSql.BuildMetricVectors(
            Caller,
            new RegistryStatisticsScope(),
            [RegistryMeasure.SurveyedLength, RegistryMeasure.Depth],
            100);

        // Named column by column rather than as a blanket search for "IS NOT NULL": the access
        // walk composed into every statement here contains that phrase for its own reasons, so a
        // blanket assertion fails on a statement that is perfectly correct and would be "fixed" by
        // deleting the check.
        sql.ShouldNotContain("c.surveyed_length IS NOT NULL");
        sql.ShouldNotContain("c.depth IS NOT NULL");

        // And nothing else filters on a measure either, whatever it is spelled as.
        foreach (var measure in Enum.GetValues<RegistryMeasure>())
        {
            sql.ShouldNotContain($"{RegistryStatisticsSql.ColumnOf(measure)} IS NOT NULL");
        }

        // Both measures are read, in the order they were named, so the grouping's own column labels
        // and the readings it takes distances over cannot fall out of step.
        sql.IndexOf("surveyed_length", StringComparison.Ordinal)
            .ShouldBeLessThan(sql.IndexOf("c.depth", StringComparison.Ordinal));

        // Bounded, because a grouping quietly taken over the first so many caves would wear the
        // whole scope's label with nothing in the answer recording it. The caller compares what
        // came back against the bound and refuses rather than answering.
        sql.ShouldContain("LIMIT @rs_limit");
    }

    private static IEnumerable<(string Name, string Sql)> EveryStatement(
        RegistryStatisticsScope scope)
    {
        yield return ("bounds",
            RegistryStatisticsSql.BuildBounds(Caller, scope, RegistryMeasure.SurveyedLength).Sql);
        yield return ("histogram", RegistryStatisticsSql.BuildHistogram(
            Caller, scope, RegistryMeasure.SurveyedLength, 0, 1000, 10).Sql);
        yield return ("percentiles", RegistryStatisticsSql.BuildPercentiles(
            Caller, scope, RegistryMeasure.SurveyedLength, [0.5]).Sql);
        yield return ("correlation", RegistryStatisticsSql.BuildCorrelation(
            Caller, scope, RegistryMeasure.SurveyedLength, RegistryMeasure.Depth, true).Sql);
        yield return ("regions", RegistryStatisticsSql.BuildRegionBreakdown(Caller, scope).Sql);
        yield return ("sample", RegistryStatisticsSql.BuildSample(
            Caller, scope, RegistryMeasure.SurveyedLength).Sql);
        yield return ("metric vectors", RegistryStatisticsSql.BuildMetricVectors(
            Caller, scope, [RegistryMeasure.SurveyedLength, RegistryMeasure.Depth], 100).Sql);
    }
}
