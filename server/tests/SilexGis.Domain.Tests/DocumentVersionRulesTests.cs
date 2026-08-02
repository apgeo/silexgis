// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using static SilexGis.Domain.Documents.DocumentVersionRules;

namespace SilexGis.Domain.Tests;

public class DocumentVersionRulesTests
{
    private static readonly Guid V1 = Guid.CreateVersion7();
    private static readonly Guid V2 = Guid.CreateVersion7();
    private static readonly Guid V3 = Guid.CreateVersion7();

    [Fact]
    public void A_documents_first_version_is_number_one()
    {
        NextVersionNumber([]).ShouldBe(FirstVersionNumber);
        FirstVersionNumber.ShouldBe(1);
    }

    [Fact]
    public void Numbering_continues_past_the_highest_issued_even_after_a_deletion()
    {
        NextVersionNumber([Sup(V1, 1), Cur(V2, 2)]).ShouldBe(3);

        // Version 2 was purged because it held something that had to go. The next upload
        // must not reuse its number — the audit trail already refers to it.
        NextVersionNumber([Sup(V1, 1), Cur(V3, 3)]).ShouldBe(4);
    }

    [Fact]
    public void Exactly_one_version_is_current()
    {
        Current([Sup(V1, 1), Cur(V2, 2)]).ShouldBe(Cur(V2, 2));
        Current([]).ShouldBeNull();

        // Two claims is a corrupted document; guessing which to serve would hide that.
        Should.Throw<InvalidOperationException>(() => Current([Cur(V1, 1), Cur(V2, 2)]));
    }

    [Fact]
    public void Only_the_current_version_accepts_a_new_one_on_top()
    {
        MayStackOnto(Cur(V2, 2)).ShouldBeTrue();

        // Stacking onto a superseded version would silently discard what replaced it.
        MayStackOnto(Sup(V1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void A_well_formed_sequence_has_no_problems_and_gaps_are_allowed()
    {
        Validate([Sup(V1, 1), Cur(V2, 2)]).ShouldBeEmpty();

        // A purged middle version leaves a gap on purpose.
        Validate([Sup(V1, 1), Cur(V3, 3)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_document_with_no_versions_is_reported()
    {
        var problems = Validate([]);
        problems.ShouldHaveSingleItem().ShouldContain("no versions");
    }

    [Fact]
    public void Zero_or_several_current_versions_are_reported()
    {
        Validate([Sup(V1, 1), Sup(V2, 2)]).ShouldHaveSingleItem().ShouldContain("0 versions are current");
        Validate([Cur(V1, 1), Cur(V2, 2)]).ShouldHaveSingleItem().ShouldContain("2 versions are current");
    }

    [Fact]
    public void Repeated_and_out_of_range_version_numbers_are_reported()
    {
        Validate([Sup(V1, 2), Cur(V2, 2)]).ShouldHaveSingleItem().ShouldContain("repeat");
        Validate([Cur(V1, 0)]).ShouldHaveSingleItem().ShouldContain("below 1");
    }

    private static VersionState Cur(Guid id, int number) => new(id, number, IsCurrent: true);

    private static VersionState Sup(Guid id, int number) => new(id, number, IsCurrent: false);
}
