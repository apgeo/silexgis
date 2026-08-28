// SPDX-License-Identifier: AGPL-3.0-or-later
using NetArchTest.Rules;
using Shouldly;
using SilexGis.Domain;
using SilexGis.Infrastructure;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// Layer dependency rules: Domain depends on nothing, Infrastructure on Domain, Api on both.
/// These tests are a hard quality floor — if one fails, the design is wrong. Fix the design;
/// never weaken or delete a rule here to make a build pass.
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

    [Fact]
    public void Feature_slices_do_not_read_user_rows_directly()
    {
        // What a caller may see of another user is a rule, applied in one place. A slice that
        // projects a user column reaches the Identity namespace to do it, so this makes an
        // overlooked or newly added path fail the build instead of review.
        //
        // Two slices are legitimately exempt: Me owns the account holder's own data, and Users
        // owns the directory — both go through the shared resolver for anyone else's profile.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("SilexGis.Api.Features")
            .And()
            .DoNotResideInNamespaceStartingWith("SilexGis.Api.Features.Me")
            .And()
            .DoNotResideInNamespaceStartingWith("SilexGis.Api.Features.Users")
            .ShouldNot()
            .HaveDependencyOn("SilexGis.Infrastructure.Identity")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Domain_does_not_depend_on_the_survey_format_readers()
    {
        // The readers for the compiled survey formats parse untrusted uploaded bytes, which is
        // infrastructure work. Domain holds rules, not file formats, and a cave model coming
        // straight from a parser must be translated into this project's own types before any
        // rule sees it — otherwise the shape of somebody else's file format becomes the shape of
        // the domain.
        var result = Types.InAssembly(typeof(Visibility).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Therion")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Api_does_not_depend_on_the_survey_format_readers()
    {
        // Endpoints speak in this application's own types. Reading a survey file happens behind
        // the infrastructure seam that owns the job, so a handler cannot quietly grow a
        // dependency on a file format by accepting a parsed model as a parameter.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Therion")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Nothing_reaches_the_survey_library_code_that_starts_an_external_process()
    {
        // The referenced survey library is taken whole, and part of it exists to drive Blender:
        // it locates an executable on the host and runs it. Nothing here wants that, and a web
        // application that can be talked into starting a local process from an uploaded file is
        // a different kind of program than this one. Reading the parsers does not require it, so
        // assert the rest stays unreachable rather than trusting that nobody calls it.
        //
        // This reads compiled IL, so it catches use rather than reference — the project
        // reference itself is reviewed by eye. Never write this as an exclusion of the library
        // from the two rules above: an exclusion would hide exactly the violation worth catching.
        foreach (var assembly in new[]
                 {
                     typeof(Visibility).Assembly,
                     typeof(InfrastructureMarker).Assembly,
                     typeof(Program).Assembly,
                 })
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Therion.Blender.Execution",
                    "Therion.Blender.Sources")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"{assembly.GetName().Name}: {FailureMessage(result)}");
        }
    }

    private static string FailureMessage(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Violating types: {string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}";
}
