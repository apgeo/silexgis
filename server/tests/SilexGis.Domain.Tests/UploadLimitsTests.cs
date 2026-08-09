// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What an installation refuses an upload for, and how much room it says is left.
///
/// <para>
/// The same function answers before the transfer and after it, so these tests are also what
/// keeps the notice a client draws and the refusal a server issues from ever disagreeing —
/// the failure they exist to prevent is somebody watching 400 MB transfer and then being told
/// there was never room for it.
/// </para>
/// </summary>
public class UploadLimitsTests
{
    private static UploadAllowance Allowance(
        long maxUpload = 100,
        long? userQuota = null,
        long userUsed = 0,
        long? storeQuota = null,
        long storeUsed = 0,
        string[]? accepted = null,
        string[]? refused = null) =>
        new(maxUpload, userQuota, userUsed, storeQuota, storeUsed, accepted ?? [], refused ?? []);

    [Fact]
    public void An_ordinary_file_under_every_limit_is_accepted()
    {
        UploadLimits.Refuse(Allowance(), "survey.pdf", 50).ShouldBeNull();
    }

    [Fact]
    public void An_empty_file_is_refused_before_anything_else_is_considered()
    {
        // Zero bytes is a mistake rather than a limit, and saying "too large" or "no room"
        // about it would send somebody to delete things that were never the problem.
        UploadLimits.Refuse(Allowance(maxUpload: 0, userQuota: 0), "survey.pdf", 0)
            .ShouldBe(UploadItemReasons.Empty);
    }

    [Fact]
    public void A_file_past_the_single_file_limit_is_refused_as_too_large()
    {
        UploadLimits.Refuse(Allowance(maxUpload: 100), "survey.pdf", 101)
            .ShouldBe(UploadItemReasons.TooLarge);

        // Exactly at the limit is inside it.
        UploadLimits.Refuse(Allowance(maxUpload: 100), "survey.pdf", 100).ShouldBeNull();
    }

    [Fact]
    public void With_no_lists_configured_every_type_is_accepted()
    {
        var allowance = Allowance();
        UploadLimits.Refuse(allowance, "survey.pdf", 1).ShouldBeNull();
        UploadLimits.Refuse(allowance, "scan.tif", 1).ShouldBeNull();
        UploadLimits.Refuse(allowance, "notes", 1).ShouldBeNull();
        UploadLimits.Refuse(allowance, "archive.tar.gz", 1).ShouldBeNull();
    }

    [Fact]
    public void An_acceptance_list_admits_only_what_it_names()
    {
        var allowance = Allowance(accepted: [".pdf", "tif"]);

        UploadLimits.Refuse(allowance, "survey.pdf", 1).ShouldBeNull();
        // Written without the dot in configuration, and meaning the same thing.
        UploadLimits.Refuse(allowance, "scan.TIF", 1).ShouldBeNull();

        UploadLimits.Refuse(allowance, "notes.txt", 1).ShouldBe(UploadItemReasons.TypeNotAccepted);
        // A name with no extension matches nothing on a list, so a list refuses it.
        UploadLimits.Refuse(allowance, "notes", 1).ShouldBe(UploadItemReasons.TypeNotAccepted);
    }

    [Fact]
    public void A_refusal_beats_an_acceptance_naming_the_same_type()
    {
        // The operator who wrote the refusal was being specific about this one.
        var allowance = Allowance(accepted: [".pdf", ".exe"], refused: [".exe"]);

        UploadLimits.Refuse(allowance, "survey.pdf", 1).ShouldBeNull();
        UploadLimits.Refuse(allowance, "setup.exe", 1).ShouldBe(UploadItemReasons.TypeNotAccepted);
    }

    [Fact]
    public void A_refusal_list_alone_still_admits_everything_it_does_not_name()
    {
        var allowance = Allowance(refused: [".exe"]);

        UploadLimits.Refuse(allowance, "survey.pdf", 1).ShouldBeNull();
        UploadLimits.Refuse(allowance, "notes", 1).ShouldBeNull();
        UploadLimits.Refuse(allowance, "setup.exe", 1).ShouldBe(UploadItemReasons.TypeNotAccepted);
    }

    [Fact]
    public void A_personal_quota_is_measured_against_what_is_left_not_against_the_quota()
    {
        var allowance = Allowance(maxUpload: 1000, userQuota: 100, userUsed: 60);

        allowance.RemainingBytes.ShouldBe(40);
        UploadLimits.Refuse(allowance, "survey.pdf", 40).ShouldBeNull();
        UploadLimits.Refuse(allowance, "survey.pdf", 41).ShouldBe(UploadItemReasons.QuotaExceeded);
    }

    [Fact]
    public void The_installations_ceiling_binds_as_well_as_the_persons_and_the_tighter_one_wins()
    {
        // Plenty of personal room, no room on the disk.
        var full = Allowance(maxUpload: 1000, userQuota: 500, userUsed: 0, storeQuota: 100, storeUsed: 95);
        full.RemainingBytes.ShouldBe(5);
        UploadLimits.Refuse(full, "survey.pdf", 6).ShouldBe(UploadItemReasons.QuotaExceeded);

        // Room on the disk, none of it theirs.
        var spent = Allowance(maxUpload: 1000, userQuota: 10, userUsed: 10, storeQuota: 1000, storeUsed: 0);
        spent.RemainingBytes.ShouldBe(0);
        UploadLimits.Refuse(spent, "survey.pdf", 1).ShouldBe(UploadItemReasons.QuotaExceeded);
    }

    [Fact]
    public void A_quota_lowered_below_what_is_already_held_leaves_no_room_rather_than_a_debt()
    {
        // An administrator tightening a quota must not produce a negative number that a
        // subtraction somewhere else turns back into room.
        var over = Allowance(maxUpload: 1000, userQuota: 50, userUsed: 500);

        over.RemainingBytes.ShouldBe(0);
        UploadLimits.Refuse(over, "survey.pdf", 1).ShouldBe(UploadItemReasons.QuotaExceeded);
    }

    [Fact]
    public void No_quota_anywhere_means_no_limit_rather_than_no_room()
    {
        Allowance().RemainingBytes.ShouldBeNull();
        UploadLimits.Refuse(Allowance(maxUpload: long.MaxValue), "survey.pdf", 1_000_000).ShouldBeNull();
    }

    [Fact]
    public void An_extension_is_read_from_the_last_dot_and_lower_cased()
    {
        UploadLimits.ExtensionOf("survey.PDF").ShouldBe(".pdf");
        UploadLimits.ExtensionOf("survey.2019.tif").ShouldBe(".tif");
        UploadLimits.ExtensionOf("notes").ShouldBe(string.Empty);
        UploadLimits.ExtensionOf(null).ShouldBe(string.Empty);

        // A dotted prefix is a name, not an extension — the same answer the platform gives.
        UploadLimits.ExtensionOf(".gitignore").ShouldBe(string.Empty);
        // A trailing dot names nothing.
        UploadLimits.ExtensionOf("survey.").ShouldBe(string.Empty);
    }
}
