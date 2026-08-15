// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Tests;

public class AccessActionTests
{
    [Fact]
    public void Flags_combine_and_test_independently()
    {
        var granted = AccessAction.Read | AccessAction.Write;

        granted.HasFlag(AccessAction.Read).ShouldBeTrue();
        granted.HasFlag(AccessAction.Write).ShouldBeTrue();
        granted.HasFlag(AccessAction.Delete).ShouldBeFalse();
        granted.HasFlag(AccessAction.ViewExactLocation).ShouldBeFalse();
    }

    [Fact]
    public void Flag_values_match_the_data_model_contract()
    {
        // Stored values, must never change. The first six are the pre-redesign
        // per-object permission flags; Create/Execute were appended by the access model.
        ((int)AccessAction.Read).ShouldBe(1);
        ((int)AccessAction.Write).ShouldBe(2);
        ((int)AccessAction.Delete).ShouldBe(4);
        ((int)AccessAction.Share).ShouldBe(8);
        ((int)AccessAction.ManagePermissions).ShouldBe(16);
        ((int)AccessAction.ViewExactLocation).ShouldBe(32);
        ((int)AccessAction.Create).ShouldBe(64);
        ((int)AccessAction.Execute).ShouldBe(128);

        AccessActions.All.Aggregate(AccessAction.None, (acc, a) => acc | a)
            .ShouldBe(AccessActions.Everything);
    }

    [Fact]
    public void Stored_enum_values_match_the_data_model_contract()
    {
        // Stored values of the RLS-ready visibility column, must never change.
        ((short)Visibility.Private).ShouldBe((short)0);
        ((short)Visibility.CavingGroup).ShouldBe((short)1);
        ((short)Visibility.Authenticated).ShouldBe((short)2);
        ((short)Visibility.Public).ShouldBe((short)3);

        // Access-model enums are schema contracts too.
        ((short)AccessEffect.Allow).ShouldBe((short)0);
        ((short)AccessEffect.Deny).ShouldBe((short)1);
        ((short)AccessSubjectKind.User).ShouldBe((short)0);
        ((short)AccessSubjectKind.CavingGroup).ShouldBe((short)1);
        ((short)AccessScopeKind.All).ShouldBe((short)0);
        ((short)AccessScopeKind.Own).ShouldBe((short)1);
        ((short)AccessScopeKind.CavingGroup).ShouldBe((short)2);
        ((short)AccessScopeKind.Subtree).ShouldBe((short)3);
        ((short)AccessScopeKind.FeatureSet).ShouldBe((short)4);
        ((short)AccessScopeKind.Object).ShouldBe((short)5);
        ((short)AccessScopeKind.Cabinet).ShouldBe((short)6);
        ((short)AccessDomain.Features).ShouldBe((short)0);
        ((short)AccessDomain.Jobs).ShouldBe((short)18);
        ((short)AccessDomain.Documents).ShouldBe((short)19);
        // 21, not the next unused number: 20 is claimed by a domain being added in
        // parallel. The catalogue is append-only, and two members sharing a value would
        // neither fail to compile nor violate a database constraint — it would surface
        // only as one domain's rules quietly governing the other's rows.
        ((short)AccessDomain.Terrain).ShouldBe((short)21);
    }
}
