// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

public class DocumentRetentionWindowTests
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Zero_or_less_means_deleted_documents_are_kept(int days)
    {
        var window = SoftDeleteRules.Window(days);

        window.ShouldBeNull();
        SoftDeleteRules.RemovedAt(DeletedAt, window).ShouldBeNull();
        SoftDeleteRules.PurgeCutoff(DeletedAt.AddYears(5), window).ShouldBeNull();
        SoftDeleteRules.IsRestorable(DeletedAt, DeletedAt.AddYears(5), window).ShouldBeTrue();
    }

    [Fact]
    public void A_window_is_a_number_of_days_and_ends_exactly_where_the_sweep_starts()
    {
        var window = SoftDeleteRules.Window(30);

        window.ShouldBe(TimeSpan.FromDays(30));
        SoftDeleteRules.RemovedAt(DeletedAt, window).ShouldBe(DeletedAt.AddDays(30));
        SoftDeleteRules.IsRestorable(DeletedAt, DeletedAt.AddDays(29), window).ShouldBeTrue();
        SoftDeleteRules.IsRestorable(DeletedAt, DeletedAt.AddDays(31), window).ShouldBeFalse();
        SoftDeleteRules.PurgeCutoff(DeletedAt.AddDays(31), window).ShouldBe(DeletedAt.AddDays(1));
    }

    [Fact]
    public void Deleting_twice_keeps_the_first_deletion_and_restoring_clears_both_marks()
    {
        var document = new Document { Title = "t" };
        var first = Guid.NewGuid();

        document.MarkDeleted(DeletedAt, first);
        document.MarkDeleted(DeletedAt.AddDays(3), Guid.NewGuid());

        document.IsDeleted.ShouldBeTrue();
        document.DeletedAt.ShouldBe(DeletedAt);
        document.DeletedByUserId.ShouldBe(first);

        document.MarkRestored();

        document.IsDeleted.ShouldBeFalse();
        document.DeletedByUserId.ShouldBeNull();
    }
}
