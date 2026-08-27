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
    public void Deciding_who_may_read_a_row_never_asks_what_state_it_is_in()
    {
        // Where an activity has got to and who may read it are two questions, and only one of
        // them is a permission. A draft is not a security boundary: a trip or a camp marked
        // public is public while it is still being written, and one marked private stays shut
        // after it is announced — visibility and the access entries answer readability on their
        // own. The moment a second rule could answer it, the two are free to disagree, and the
        // disagreement would be a disclosure rather than a bug somebody notices.
        //
        // So the lifecycle vocabulary may not be reachable from the code that decides access at
        // all — not the evaluator, not its query twin, not the ruleset that feeds them. This
        // makes a reference somebody adds fail the build rather than review.
        //
        // How settled a trip's preparation is answers to the same rule and is named here for the
        // same reason. It is a reading of rows, not a state, and it must never become a second
        // thing deciding who sees a plan: a trip whose party has ticked nothing is exactly as
        // visible as one that has ticked everything, to exactly the same people.
        var domain = Types.InAssembly(typeof(Visibility).Assembly)
            .That()
            .ResideInNamespaceStartingWith("SilexGis.Domain.Access")
            .Or()
            .ResideInNamespaceStartingWith("SilexGis.Domain.Permissions")
            .ShouldNot()
            .HaveDependencyOnAny(StateVocabulary)
            .GetResult();

        domain.IsSuccessful.ShouldBeTrue(FailureMessage(domain));

        var infrastructure = Types.InAssembly(typeof(InfrastructureMarker).Assembly)
            .That()
            .ResideInNamespaceStartingWith("SilexGis.Infrastructure.Permissions")
            .ShouldNot()
            .HaveDependencyOnAny(StateVocabulary)
            .GetResult();

        infrastructure.IsSuccessful.ShouldBeTrue(FailureMessage(infrastructure));
    }

    /// <summary>
    /// What a row is in the middle of, and how far through its preparation it is. Neither is an
    /// answer to who may read it, and neither may be reachable from the code that decides that.
    /// </summary>
    private static readonly string[] StateVocabulary =
    [
        "SilexGis.Domain.Entities.ActivityState",
        "SilexGis.Domain.Entities.TripChecklistTick",
        "SilexGis.Domain.Trips.TripReadiness",
    ];

    private static string FailureMessage(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Violating types: {string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}";
}
