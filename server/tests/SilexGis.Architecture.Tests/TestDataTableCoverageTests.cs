// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// The test-data generator promises to reach every table of the schema, or to say why not. These
/// tests hold it to the schema as the schema grows: a new table fails here until somebody decides
/// how test data reaches it, and a table the generator writes cannot go unmeasured or unremovable.
/// </summary>
public class TestDataTableCoverageTests
{
    [Fact]
    public void Every_table_of_the_model_is_classified_and_nothing_else_is()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var tables = context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        // The fixture half: the model really is being read.
        tables.ShouldContain("caves");
        tables.Count.ShouldBeGreaterThan(100);

        tables.Where(t => !TestDataTables.All.ContainsKey(t)).Order().ShouldBeEmpty(
            "these tables are not in TestDataTables: say whether the generator writes them, when, or why never");
        TestDataTables.All.Keys.Where(t => !tables.Contains(t)).Order().ShouldBeEmpty(
            "TestDataTables names tables the model no longer has");
    }

    [Fact]
    public void Every_table_generated_so_far_is_measured_by_the_coverage_report()
    {
        var measured = TestDataCoverageSql.Scopes.Select(s => s.Table).ToHashSet(StringComparer.Ordinal);
        TestDataTables.GeneratedNow().Where(t => !measured.Contains(t)).Order().ShouldBeEmpty(
            "generated tables with no scope in TestDataCoverageSql.Scopes, so their coverage is never reported");
    }

    [Fact]
    public void Every_registry_table_the_report_hangs_rows_from_can_be_removed()
    {
        TestDataCoverageSql.Scopes.Select(s => s.RegistryTable).Distinct()
            .Where(t => !TestDataRemoval.RemovableTables.Contains(t)).ShouldBeEmpty(
                "a run would list rows that removal refuses, and the run could never be removed");
    }
}
