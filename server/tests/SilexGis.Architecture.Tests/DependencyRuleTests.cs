// SPDX-License-Identifier: AGPL-3.0-or-later
using NetArchTest.Rules;
using Shouldly;
using SilexGis.Api.Features.PhotoLibraries;
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
    public void Domain_does_not_depend_on_the_raster_library()
    {
        // Reading pixels out of a file is a native library's job and it belongs behind the
        // infrastructure seam. Domain says what a height means and what may be told to whom; it
        // must not know what a dataset handle is. The rule matters more than most here because the
        // library's handles fault the whole process rather than throwing when they are misused, so
        // a domain type holding one turns a rule into an abort — and because nothing else stops it:
        // adding the package to the domain project would compile perfectly well.
        var result = Types.InAssembly(typeof(Visibility).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("OSGeo", "MaxRev")
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
        //
        // The two modules have to be named differently, and that difference is the whole reason
        // this list is worth reading carefully. In the first, the parsers and the process-starting
        // code sit in separate namespaces, so a namespace is enough. In the second — the one
        // holding the reader for what a compilation printed — everything shares a single flat
        // namespace: the reader, a type that locates an executable on the host and runs it, a type
        // that compiles by starting the compiler, and a type that hands a path to the desktop shell
        // to open. Denying that namespace would deny the reader this application actually uses, so
        // the dangerous types are denied one by one. The cost of that granularity is that a type
        // added upstream is not covered until somebody adds it here; the alternative was covering
        // nothing.
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
                    "Therion.Blender.Sources",
                    "Therion.Build.TherionCompiler",
                    "Therion.Build.ExternalToolLocator",
                    "Therion.Build.ShellOpener",
                    "Therion.Build.IShellOpener",
                    "Therion.Build.CompileGate",
                    "Therion.Build.ICompileGate",
                    "Therion.Build.JsonOutputArtifactCache",
                    "Therion.Build.OutputArtifactCollector",
                    "Therion.Processing.Abstractions.ITherionCompiler",
                    "Therion.Processing.Abstractions.IExternalToolLocator",
                    "Therion.Processing.Abstractions.IExternalToolPathOverrides")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"{assembly.GetName().Name}: {FailureMessage(result)}");
        }
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
    /// Every way of naming a neighbouring photo library, so that a rule about "reaching one" cannot
    /// be walked around by naming the product instead of the contract.
    /// </summary>
    private static readonly string[] PhotoLibraryTypes =
    [
        "SilexGis.Infrastructure.PhotoLibraries.IPhotoLibrary",
        "SilexGis.Infrastructure.PhotoLibraries.ImmichClient",
        "SilexGis.Infrastructure.PhotoLibraries.PhotoPrismClient",
    ];

    [Fact]
    public void Only_the_photo_library_slice_reaches_a_photo_library_at_all()
    {
        // Whether this installation is talking to a photo library is one decision, and it is taken
        // in one slice. A route somewhere else that resolved a library for itself would be outside
        // every surface that knows the decision exists, and the failure is silent: requests keep
        // going to a library somebody stopped, which looks exactly like a library that is working,
        // and the only symptom is traffic at a neighbour's container that nobody is watching.
        //
        // Both the shared contract and the two products are named, because a handler that asks for
        // a product by name reaches a library exactly as completely as one that asks for the
        // contract, and both are resolvable.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .DoNotResideInNamespaceStartingWith("SilexGis.Api.Features.PhotoLibraries")
            .ShouldNot()
            .HaveDependencyOnAny(PhotoLibraryTypes)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Nothing_reaches_a_neighbouring_photo_library_without_asking_whether_it_may()
    {
        // Inside the slice, a type that can reach a library must also hold the object that decides
        // whether it may, so that a new route which forgets fails the build rather than review.
        //
        // What this rule can and cannot see is worth stating, because it is easy to read it as more
        // than it is. It constrains types, not methods: it proves that a class reaching a library
        // also names the gate, and cannot prove that every handler inside that class went through
        // it. That gap is closed underneath rather than here — each client asks the same question
        // for itself in the one method its outgoing calls already go through, so a handler added to
        // an existing class and never given the gate is refused by the library client instead of
        // quietly succeeding. This rule is the earlier and louder of the two signals, not the only
        // one.
        const string gate = "SilexGis.Api.Features.PhotoLibraries.PhotoLibraryGate";

        var reachers = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("SilexGis.Api.Features.PhotoLibraries")
            .And()
            .DoNotHaveName(nameof(PhotoLibraryGate))
            .And()
            .HaveDependencyOnAny(PhotoLibraryTypes);

        // Asserted before the rule, because a rule over an empty set passes: a renamed namespace or
        // a slice that stopped naming the contract would otherwise turn this into a test that
        // proves nothing while staying green.
        reachers.GetTypes().ShouldNotBeEmpty(
            "No type in the photo-library slice reaches a library any more — check this rule still "
            + "describes the code before trusting it.");

        var result = reachers.Should().HaveDependencyOn(gate).GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
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
