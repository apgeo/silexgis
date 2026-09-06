// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What an object created from a photograph in a neighbouring library carries about where its
/// position came from.
///
/// <para>
/// The keys are pinned by name because they are stored data: they go into a jsonb column on every
/// object created that way, and a rename that only the code agrees with leaves every object already
/// written answering under the old name and nothing looking for it. Every value below is invented.
/// </para>
/// </summary>
public class PhotoLibraryProvenanceTests
{
    [Fact]
    public void The_bag_holds_the_library_and_its_own_name_for_the_photograph_and_nothing_else()
    {
        var bag = PhotoLibraryProvenance.Properties("photoprism", "aa11bb22cc33");

        bag.Count.ShouldBe(2);
        bag["photoLibrarySource"]!.GetValue<string>().ShouldBe("photoprism");
        bag["photoLibraryReference"]!.GetValue<string>().ShouldBe("aa11bb22cc33");
    }

    /// <summary>
    /// The keys stay flat rather than nesting under a container. Flat is what makes "everything that
    /// came from that library" one containment test on the column instead of a query nobody will
    /// write, and it is also what keeps a later integration from colliding — the prefix does that
    /// job, not a level of nesting.
    /// </summary>
    [Fact]
    public void The_keys_are_flat_and_prefixed()
    {
        PhotoLibraryProvenance.Keys.Source.ShouldBe("photoLibrarySource");
        PhotoLibraryProvenance.Keys.Reference.ShouldBe("photoLibraryReference");

        using var written = JsonDocument.Parse(
            PhotoLibraryProvenance.Properties("immich", "11111111-1111-4111-8111-111111111111").ToJsonString());
        foreach (var property in written.RootElement.EnumerateObject())
        {
            property.Value.ValueKind.ShouldBe(JsonValueKind.String);
        }
    }

    /// <summary>
    /// The library's own name for the photograph is stored exactly as it arrived. One product names
    /// a photograph and the picture of it with the same string and the other does not, so anything
    /// re-derived here would be a guess about somebody else's scheme — and the value's whole purpose
    /// is that somebody can go back and look it up on the far side.
    /// </summary>
    [Fact]
    public void The_reference_is_stored_verbatim()
    {
        var asset = "11111111-1111-4111-8111-111111111111";

        PhotoLibraryProvenance.Properties("immich", asset)["photoLibraryReference"]!
            .GetValue<string>().ShouldBe(asset);
    }
}
