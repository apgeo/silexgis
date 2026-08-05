// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What makes an anchor into part of a document well formed. The kind decides whether a
/// payload exists at all, so a row that got this wrong could not be interpreted at read
/// time — which is why the same rule is also a check constraint.
/// </summary>
public class DocumentAnchorRulesTests
{
    [Fact]
    public void The_whole_document_carries_no_payload_and_is_never_pinned()
    {
        DocumentAnchorRules.Validate(DocumentAnchorKind.Whole, null, null).ShouldBeEmpty();
        DocumentAnchorRules.Validate(DocumentAnchorKind.Whole, "{\"page\":2}", null).ShouldNotBeEmpty();
        DocumentAnchorRules.Validate(DocumentAnchorKind.Whole, null, Guid.CreateVersion7()).ShouldNotBeEmpty();
    }

    [Fact]
    public void Naming_a_part_needs_a_payload()
    {
        DocumentAnchorRules.Validate(DocumentAnchorKind.Page, "{\"page\":2}", null).ShouldBeEmpty();
        DocumentAnchorRules.Validate(DocumentAnchorKind.Page, null, null).ShouldNotBeEmpty();
        DocumentAnchorRules.Validate(DocumentAnchorKind.Page, "   ", null).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_payload_is_capped_so_the_column_cannot_become_a_blob_store()
    {
        var atCap = new string('x', DocumentAnchorRules.MaxAnchorLength);
        DocumentAnchorRules.Validate(DocumentAnchorKind.TextRange, atCap, null).ShouldBeEmpty();
        DocumentAnchorRules.Validate(DocumentAnchorKind.TextRange, atCap + "x", null).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_region_names_the_rendition_its_pixels_were_measured_against_while_a_quote_need_not()
    {
        var file = Guid.CreateVersion7();
        DocumentAnchorRules.Validate(DocumentAnchorKind.ImageRegion, "{\"xywh\":\"0,0,10,10\"}", file).ShouldBeEmpty();
        DocumentAnchorRules.Validate(DocumentAnchorKind.ImageRegion, "{\"xywh\":\"0,0,10,10\"}", null).ShouldNotBeEmpty();
        // A text quote survives a new version by being looked for again, so it may stand unpinned.
        DocumentAnchorRules.Validate(DocumentAnchorKind.TextRange, "{\"quote\":\"sump\"}", null).ShouldBeEmpty();
    }
}
