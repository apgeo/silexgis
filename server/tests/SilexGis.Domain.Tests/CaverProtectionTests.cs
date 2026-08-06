// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Profiles;

namespace SilexGis.Domain.Tests;

public class CaverProtectionTests
{
    private static Caver AccountLess() => new()
    {
        FullName = "Maria Ionescu",
        Email = "maria@example.org",
        Phone = "+40 700 333 444",
        Notes = "Prefers weekend trips.",
    };

    private static Caver Linked(Guid userId) => new()
    {
        FullName = "Roster Name",
        // Contact columns on the roster row of a linked person must never be served —
        // only what the account holder's own profile projection released.
        Email = "stale-roster@example.org",
        Phone = "+40 700 555 666",
        Notes = "Joined via club import.",
        UserId = userId,
    };

    private static PublicProfile Profile(Guid userId, string? email, string? phone) => new(
        userId, "Chosen Label", null, null, null, null, null, email, phone, null, null, []);

    [Fact]
    public void An_account_less_cavers_contact_is_roster_keeper_only()
    {
        var caver = AccountLess();

        // Nobody consented on this person's behalf, so contact and remarks answer only
        // to whoever keeps the roster.
        var forKeeper = CaverProtection.Project(caver, canKeepRoster: true, accountProfile: null);
        forKeeper.Email.ShouldBe("maria@example.org");
        forKeeper.Phone.ShouldBe("+40 700 333 444");
        forKeeper.Notes.ShouldBe("Prefers weekend trips.");

        var forMember = CaverProtection.Project(caver, canKeepRoster: false, accountProfile: null);
        forMember.FullName.ShouldBe("Maria Ionescu"); // the name tier: any signed-in caller
        forMember.Email.ShouldBeNull();
        forMember.Phone.ShouldBeNull();
        forMember.Notes.ShouldBeNull();
    }

    [Fact]
    public void A_linked_cavers_contact_follows_the_account_holders_own_projection()
    {
        var userId = Guid.CreateVersion7();
        var caver = Linked(userId);

        // The holder released their email but not their phone: the roster serves
        // exactly that, and its own stale contact columns never leak around it.
        var projected = CaverProtection.Project(
            caver, canKeepRoster: false, Profile(userId, "chosen@example.org", phone: null));
        projected.FullName.ShouldBe("Chosen Label"); // one person, one name everywhere
        projected.Email.ShouldBe("chosen@example.org");
        projected.Phone.ShouldBeNull();
    }

    [Fact]
    public void Roster_keeping_grants_no_way_around_a_linked_holders_choices()
    {
        var userId = Guid.CreateVersion7();
        var caver = Linked(userId);

        var projected = CaverProtection.Project(
            caver, canKeepRoster: true, Profile(userId, email: null, phone: null));

        // The keeper reads their own remarks, but a linked person's contact stays what
        // the holder released — hiding everything hides it from the keeper too.
        projected.Email.ShouldBeNull();
        projected.Phone.ShouldBeNull();
        projected.Notes.ShouldBe("Joined via club import.");
    }

    [Fact]
    public void The_label_prefers_the_accounts_chosen_name()
    {
        CaverProtection.Label("Roster Name", "Chosen Label").ShouldBe("Chosen Label");
        CaverProtection.Label("Roster Name", null).ShouldBe("Roster Name");
        CaverProtection.Label("Roster Name", "  ").ShouldBe("Roster Name");
    }
}
