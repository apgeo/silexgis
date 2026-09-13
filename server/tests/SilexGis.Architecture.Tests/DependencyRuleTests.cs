// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
        // The rule is stated the other way round — an allow-list — and that inversion is the
        // point. Naming the dangerous types one by one was the obvious way to write it, and it
        // covered only what somebody had thought of: the module holding the reader for what a
        // compilation printed keeps the reader, a type that locates an executable and runs it, a
        // type that compiles by starting the compiler, and a type that hands a path to the desktop
        // shell to open, all in one flat namespace. A deny-list can exclude those four. It cannot
        // exclude the fifth that arrives in the next upstream release, so a submodule bump could
        // re-open this seam with every test still green — and the seam is what keeps an uploaded
        // survey file from starting a process on the server.
        //
        // So instead: every Therion type any SilexGIS assembly reaches for must appear below.
        // A new one fails this test until somebody looks at it and says what it is. That turns a
        // silent widening into a deliberate one, which is the only version of this rule that
        // survives an upstream release nobody read.
        //
        // Read from the compiled IL's type-reference table rather than through the rule library,
        // because the question here is "what does this assembly name?" rather than "does it name
        // this?", and only the former can be checked against a closed set.
        foreach (var assembly in new[]
                 {
                     typeof(Visibility).Assembly,
                     typeof(InfrastructureMarker).Assembly,
                     typeof(Program).Assembly,
                 })
        {
            var reached = TherionTypesReferencedBy(assembly);
            var unexpected = reached.Except(AllowedTherionTypes, StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();

            unexpected.ShouldBeEmpty(
                $"{assembly.GetName().Name} reaches Therion types that no rule here has passed: "
                + string.Join(", ", unexpected)
                + ". If one of them reads or models survey data, add it to the allow-list. If it "
                + "locates an executable, starts a process or opens a shell, it does not belong in "
                + "a web application and the call is the thing to remove.");
        }
    }

    /// <summary>
    /// Every Therion type SilexGIS is allowed to name: the survey readers, the shapes they hand
    /// back, and the parser for what a compilation printed.
    /// </summary>
    /// <remarks>
    /// Nothing here starts anything. They are file readers, immutable records describing a cave,
    /// the centreline graph types derived from them, and a log parser that reads text. The list is
    /// expected to grow when the application reads more of the survey format, and each addition is
    /// somebody deciding that the type is a reader — which is exactly the review this rule exists
    /// to force.
    /// </remarks>
    private static readonly HashSet<string> AllowedTherionTypes = new(StringComparer.Ordinal)
    {
        // The parsed cave model and the shapes hanging off it.
        "Therion.Blender.CaveLrud",
        "Therion.Blender.CaveModel",
        "Therion.Blender.CavePassage",
        "Therion.Blender.CavePassageStation",
        "Therion.Blender.CaveShot",
        "Therion.Blender.CaveShotFlags",
        "Therion.Blender.CaveShotSection",
        "Therion.Blender.CaveStation",
        "Therion.Blender.CaveStationFlags",
        "Therion.Blender.CaveSurvey",
        "Therion.Blender.CaveVector3",

        // The centreline graph derived from a model.
        "Therion.Blender.Geometry.CenterlineBranch",
        "Therion.Blender.Geometry.CenterlineComponent",
        "Therion.Blender.Geometry.CenterlineEdgeGeometry",
        "Therion.Blender.Geometry.CenterlineGraph",
        "Therion.Blender.Geometry.CenterlineReducedGraph",

        // Reading a survey file, and the refusal when it is not one.
        "Therion.Blender.Parsing.CaveFileFormatException",
        "Therion.Blender.Parsing.CaveModelReader",

        // Reading what a compilation printed. Text in, records out — this starts no compiler.
        "Therion.Build.TherionLogDiagnostic",
        "Therion.Build.TherionLogLoopError",
        "Therion.Build.TherionLogOutcome",
        "Therion.Build.TherionLogParser",
        "Therion.Build.TherionLogSummary",
    };

    /// <summary>
    /// The Therion types an assembly names, read out of its type-reference table.
    /// </summary>
    /// <remarks>
    /// This reads the compiled file rather than loading types reflectively: a type reference is
    /// recorded whether or not the referencing code ever runs, and resolving it through reflection
    /// would need the referenced assembly to load, which is the thing being kept at arm's length.
    /// </remarks>
    private static HashSet<string> TherionTypesReferencedBy(Assembly assembly)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        foreach (var handle in metadata.TypeReferences)
        {
            var reference = metadata.GetTypeReference(handle);
            var space = metadata.GetString(reference.Namespace);
            var name = metadata.GetString(reference.Name);
            var full = string.IsNullOrEmpty(space) ? name : $"{space}.{name}";

            // "Therion" alone would also match a hypothetical "TherionSomething" assembly of our
            // own, so the boundary is the dot.
            if (full is "Therion" || full.StartsWith("Therion.", StringComparison.Ordinal))
            {
                names.Add(full);
            }
        }

        return names;
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
