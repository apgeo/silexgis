// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;

namespace SilexGis.Domain.Tests;

public class ObjectPermissionTests
{
    [Fact]
    public void Flags_combine_and_test_independently()
    {
        var granted = ObjectPermission.Read | ObjectPermission.Write;

        granted.HasFlag(ObjectPermission.Read).ShouldBeTrue();
        granted.HasFlag(ObjectPermission.Write).ShouldBeTrue();
        granted.HasFlag(ObjectPermission.Delete).ShouldBeFalse();
        granted.HasFlag(ObjectPermission.ViewExactLocation).ShouldBeFalse();
    }

    [Fact]
    public void Flag_values_match_the_data_model_contract()
    {
        // 02-data-model.md §7 — stored values, must never change.
        ((int)ObjectPermission.Read).ShouldBe(1);
        ((int)ObjectPermission.Write).ShouldBe(2);
        ((int)ObjectPermission.Delete).ShouldBe(4);
        ((int)ObjectPermission.Share).ShouldBe(8);
        ((int)ObjectPermission.ManagePermissions).ShouldBe(16);
        ((int)ObjectPermission.ViewExactLocation).ShouldBe(32);
    }

    [Fact]
    public void Visibility_values_match_the_data_model_contract()
    {
        // 02-data-model.md RLS-ready columns — stored values, must never change.
        ((short)Visibility.Private).ShouldBe((short)0);
        ((short)Visibility.Team).ShouldBe((short)1);
        ((short)Visibility.Authenticated).ShouldBe((short)2);
        ((short)Visibility.Public).ShouldBe((short)3);
    }
}
