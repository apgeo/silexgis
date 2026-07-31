// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Settings;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which second factors an account may use. The interesting cases are all the ways a method can
/// be taken away by someone other than the person signing in.
/// </summary>
public class TwoFactorPolicyTests
{
    private static readonly SecuritySettings AllAllowed = new()
    {
        AuthenticatorTwoFactorEnabled = true,
        EmailTwoFactorEnabled = true,
        SmsTwoFactorEnabled = true,
    };

    private static TwoFactorState State(
        bool authenticator = false,
        bool email = false,
        bool sms = false,
        bool emailConfirmed = true,
        bool phoneConfirmed = true,
        TwoFactorMethod? preferred = null) =>
        new(authenticator, email, sms, emailConfirmed, phoneConfirmed, preferred);

    [Fact]
    public void Nothing_is_available_when_the_user_enabled_nothing()
    {
        TwoFactorPolicy.AvailableMethods(State(), AllAllowed, true, true).ShouldBeEmpty();
        TwoFactorPolicy.AnyEnabled(State()).ShouldBeFalse();
    }

    [Fact]
    public void Enabled_methods_are_offered_strongest_first()
    {
        var available = TwoFactorPolicy.AvailableMethods(
            State(authenticator: true, email: true, sms: true), AllAllowed, true, true);

        available.ShouldBe([TwoFactorMethod.Authenticator, TwoFactorMethod.Email, TwoFactorMethod.Sms]);
    }

    [Fact]
    public void An_unconfirmed_destination_removes_its_method()
    {
        TwoFactorPolicy.AvailableMethods(
                State(email: true, emailConfirmed: false), AllAllowed, true, true)
            .ShouldBeEmpty();

        TwoFactorPolicy.AvailableMethods(
                State(sms: true, phoneConfirmed: false), AllAllowed, true, true)
            .ShouldBeEmpty();
    }

    [Fact]
    public void A_channel_that_is_not_configured_removes_its_method()
    {
        TwoFactorPolicy.AvailableMethods(State(email: true), AllAllowed, mailConfigured: false, smsConfigured: true)
            .ShouldBeEmpty();

        TwoFactorPolicy.AvailableMethods(State(sms: true), AllAllowed, mailConfigured: true, smsConfigured: false)
            .ShouldBeEmpty();
    }

    [Fact]
    public void An_installation_can_disallow_a_method_the_user_had_enabled()
    {
        var policy = AllAllowed with { SmsTwoFactorEnabled = false };

        TwoFactorPolicy.AvailableMethods(State(sms: true), policy, true, true).ShouldBeEmpty();
        // The user's own switch is untouched — only the offer is withdrawn, so restoring the
        // installation setting restores the method without the user doing anything.
        TwoFactorPolicy.AnyEnabled(State(sms: true)).ShouldBeTrue();
    }

    [Fact]
    public void Losing_every_method_falls_back_to_a_recovery_code_rather_than_a_lockout()
    {
        var state = State(sms: true);
        var policy = AllAllowed with { SmsTwoFactorEnabled = false };

        TwoFactorPolicy.RequiresRecoveryCode(state, policy, true, true).ShouldBeTrue();
        TwoFactorPolicy.PreferredMethod(state, policy, true, true).ShouldBeNull();
    }

    [Fact]
    public void An_account_with_no_second_factor_never_needs_a_recovery_code()
    {
        TwoFactorPolicy.RequiresRecoveryCode(State(), AllAllowed, true, true).ShouldBeFalse();
    }

    [Fact]
    public void The_users_choice_is_offered_first_when_it_is_usable()
    {
        var state = State(authenticator: true, email: true, preferred: TwoFactorMethod.Email);

        TwoFactorPolicy.PreferredMethod(state, AllAllowed, true, true).ShouldBe(TwoFactorMethod.Email);
    }

    [Fact]
    public void An_unusable_choice_falls_back_to_the_strongest_that_works()
    {
        // The user picked email, then the mail server went away.
        var state = State(authenticator: true, email: true, preferred: TwoFactorMethod.Email);

        TwoFactorPolicy.PreferredMethod(state, AllAllowed, mailConfigured: false, smsConfigured: true)
            .ShouldBe(TwoFactorMethod.Authenticator);
    }

    [Fact]
    public void Enrolment_is_refused_for_a_method_the_installation_disallows()
    {
        var policy = AllAllowed with { SmsTwoFactorEnabled = false };

        TwoFactorPolicy.IsMethodAllowed(TwoFactorMethod.Sms, policy).ShouldBeFalse();
        TwoFactorPolicy.IsMethodAllowed(TwoFactorMethod.Email, policy).ShouldBeTrue();
    }

    [Fact]
    public void Delivered_methods_are_the_ones_that_need_sending()
    {
        TwoFactorProviders.IsDelivered(TwoFactorMethod.Email).ShouldBeTrue();
        TwoFactorProviders.IsDelivered(TwoFactorMethod.Sms).ShouldBeTrue();
        TwoFactorProviders.IsDelivered(TwoFactorMethod.Authenticator).ShouldBeFalse();
    }

    [Fact]
    public void Provider_names_match_the_ones_identity_registered()
    {
        TwoFactorProviders.For(TwoFactorMethod.Email).ShouldBe("Email");
        TwoFactorProviders.For(TwoFactorMethod.Sms).ShouldBe("Phone");
        TwoFactorProviders.For(TwoFactorMethod.Authenticator).ShouldBe("Authenticator");
    }
}
