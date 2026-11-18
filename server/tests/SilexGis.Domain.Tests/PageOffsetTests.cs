// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The rows before a page are exact for every page whose offset fits in a whole number and held at
/// the largest one beyond it: a wrapped, negative offset is refused by the database and skips
/// nothing in memory, so a page far past the end would be a server fault or the first page again.
/// </summary>
public sealed class PageOffsetTests
{
    [Theory]
    [InlineData(1, 500, 0)]
    [InlineData(3, 25, 50)]
    [InlineData(0, 25, 0)]
    [InlineData(-5, 25, 0)]
    [InlineData(2, 0, 0)]
    // The last page whose offset fits at the largest page size, and the first that does not.
    [InlineData(4_294_968, 500, 2_147_483_500)]
    [InlineData(4_294_969, 500, int.MaxValue)]
    [InlineData(int.MaxValue, 1, int.MaxValue - 1)]
    [InlineData(int.MaxValue, 500, int.MaxValue)]
    public void A_pages_offset_is_exact_while_it_fits_and_held_at_the_largest_whole_number(int page, int pageSize, int expected) =>
        PageOffset.Of(page, pageSize).ShouldBe(expected);
}
