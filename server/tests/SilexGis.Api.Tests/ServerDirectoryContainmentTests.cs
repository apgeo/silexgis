// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Documents;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Tests;

/// <summary>
/// How a directory named for the server to read is judged against the operator's roots, on a
/// real disk with real links. No application host: the resolver is the whole of the question.
/// </summary>
public sealed class ServerDirectoryContainmentTests : IDisposable
{
    private readonly string scratch;
    private readonly string root;
    private readonly string outside;

    public ServerDirectoryContainmentTests()
    {
        scratch = Path.Combine(TestScratch.Root, $"silexgis-containment-{Guid.NewGuid():N}");
        root = Path.Combine(scratch, "archive");
        outside = Path.Combine(scratch, "elsewhere");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(outside, "private"));
    }

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private ServerDirectorySource Source(params string[] roots) =>
        new(Options.Create(new FilesOptions { ImportRoots = roots.ToList() }));

    /// <summary>
    /// Makes a directory link, or says the host refuses to (Windows without the privilege), in
    /// which case the calling test has nothing to prove and returns.
    /// </summary>
    private static bool Link(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine(
                $"Link resolution was NOT exercised: this host refuses to create a symbolic link. ({e.GetType().Name})");
            return false;
        }
    }

    [Fact]
    public void A_path_outside_the_roots_gets_one_answer_whether_or_not_it_exists()
    {
        var source = Source(root);

        // The route must not tell a caller which paths exist on the rest of the server.
        source.Resolve(outside).Code.ShouldBe(ServerImportPaths.OutsideRootsCode);
        source.Resolve(Path.Combine(outside, "no-such-directory")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
        source.Resolve(Path.Combine(root, "..", "elsewhere", "missing")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
    }

    [Fact]
    public void Inside_a_root_a_missing_directory_is_said_to_be_missing()
    {
        Source(root).Resolve(Path.Combine(root, "1987")).Code.ShouldBe(ServerImportPaths.NotFoundCode);
    }

    [Fact]
    public void A_directory_inside_a_root_resolves_to_its_real_location()
    {
        var year = Directory.CreateDirectory(Path.Combine(root, "1987")).FullName;

        var (resolved, code) = Source(root).Resolve(year);

        code.ShouldBeNull();
        resolved.ShouldBe(year);
    }

    [Fact]
    public void A_link_inside_a_root_that_leads_out_is_refused()
    {
        var link = Path.Combine(root, "shortcut");
        if (!Link(link, outside))
        {
            return;
        }


        Source(root).Resolve(link).Code.ShouldBe(ServerImportPaths.OutsideRootsCode);
    }

    [Fact]
    public void A_link_in_the_middle_of_the_path_is_followed_too()
    {
        // Only the last component used to be resolved, so root/shortcut/private read the
        // directory outside while being judged by its spelling.
        if (!Link(Path.Combine(root, "shortcut"), outside))
        {
            return;
        }


        Source(root).Resolve(Path.Combine(root, "shortcut", "private")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
    }

    [Fact]
    public void A_dangling_link_that_points_out_is_refused_as_outside_not_as_missing()
    {
        // Answering "missing" here would say that the target does not exist outside the roots.
        if (!Link(Path.Combine(root, "dangling"), Path.Combine(outside, "gone")))
        {
            return;
        }


        Source(root).Resolve(Path.Combine(root, "dangling")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
        Source(root).Resolve(Path.Combine(root, "dangling", "deeper")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
    }

    [Fact]
    public void A_link_between_two_places_inside_the_roots_is_followed()
    {
        var real = Directory.CreateDirectory(Path.Combine(root, "scans", "1987")).FullName;
        var link = Path.Combine(root, "latest");
        if (!Link(link, Path.Combine(root, "scans")))
        {
            return;
        }


        var (resolved, code) = Source(root).Resolve(Path.Combine(link, "1987"));

        code.ShouldBeNull();
        resolved.ShouldBe(real);
    }

    [Fact]
    public void A_root_that_is_itself_a_link_admits_paths_spelled_through_it()
    {
        var linkedRoot = Path.Combine(scratch, "archive-link");
        if (!Link(linkedRoot, root))
        {
            return;
        }

        var year = Directory.CreateDirectory(Path.Combine(root, "1990")).FullName;

        var (resolved, code) = Source(linkedRoot).Resolve(Path.Combine(linkedRoot, "1990"));

        code.ShouldBeNull();
        resolved.ShouldBe(year);
    }

    [Fact]
    public void A_root_under_a_linked_parent_still_contains_what_is_inside_it()
    {
        var linkedParent = Path.Combine(scratch, "mount");
        if (!Link(linkedParent, scratch))
        {
            return;
        }

        var year = Directory.CreateDirectory(Path.Combine(root, "1991")).FullName;
        var spelledRoot = Path.Combine(linkedParent, "archive");

        var (resolved, code) = Source(spelledRoot).Resolve(Path.Combine(spelledRoot, "1991"));

        code.ShouldBeNull();
        resolved.ShouldBe(year);
    }

    [Fact]
    public void A_link_towards_a_roots_parent_still_cannot_be_walked_out_of_the_root()
    {
        // Following a link that points above the root is allowed — it is how a root under a
        // linked parent resolves — but what is read is still judged by where it really is.
        if (!Link(Path.Combine(root, "up"), scratch))
        {
            return;
        }

        Source(root).Resolve(Path.Combine(root, "up", "elsewhere", "private")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
        Source(root).Resolve(Path.Combine(root, "up", "elsewhere", "missing")).Code
            .ShouldBe(ServerImportPaths.OutsideRootsCode);
    }

    [Fact]
    public void Links_that_go_round_in_a_circle_end_in_a_refusal()
    {
        var a = Path.Combine(root, "a");
        var b = Path.Combine(root, "b");
        if (!Link(a, b))
        {
            return;
        }

        if (!Link(b, a))
        {
            return;
        }


        Source(root).Resolve(Path.Combine(a, "x")).Code.ShouldBe(ServerImportPaths.NotFoundCode);
    }

    [Fact]
    public void No_roots_means_the_feature_is_off()
    {
        Source().Resolve(root).Code.ShouldBe(ServerImportPaths.NoRootsCode);
        Source(Path.Combine(scratch, "never-made")).Resolve(root).Code
            .ShouldBe(ServerImportPaths.NoRootsCode);
    }
}
