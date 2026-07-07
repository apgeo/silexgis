// SPDX-License-Identifier: AGPL-3.0-or-later
using NetArchTest.Rules;
using Shouldly;
using SilexGis.Domain;
using SilexGis.Infrastructure;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// Dependency rules from 01-architecture.md §2. These tests are a hard quality floor —
/// never weaken or delete them to make a build pass (.claude/CLAUDE.md).
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Domain_depends_on_no_other_layer_and_no_web_or_data_framework()
    {
        var result = Types.InAssembly(typeof(Visibility).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "SilexGis.Infrastructure",
                "SilexGis.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(typeof(InfrastructureMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("SilexGis.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    private static string FailureMessage(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Violating types: {string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}";
}
