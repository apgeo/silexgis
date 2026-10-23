# Accounts

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/accounts.md)

[← Administration](README.md) · Related:
[Permissions explained](permissions.md) ·
[People and clubs](../features/people-and-clubs.md) ·
[Your account and settings](../features/account-and-settings.md#security)

---

**Administration → Accounts** is the register of everyone who can sign in to the installation.
It is where somebody is let in, shut out, helped back in after losing their second factor, or
removed.

> *Everyone who can sign in to this installation. Invite somebody to open an account for them,
> lock an account to shut it out until you unlock it, or delete one — the person stays on the
> roster and on the trips they were on.*

An account is not the same thing as a person on the [caver roster](../features/people-and-clubs.md#the-caver-roster):
the roster is *who is in the club*, the account is *who can sign in and act*. Every account has
a roster entry, but a roster entry need not have an account.

## Who can open the page

The page appears in the rail under **Administration** for anybody with **Read** on the
**users** domain. Each button on it follows its own right, and a button for an action you do
not hold is simply not shown.

| To | You need |
|---|---|
| See the register and open a row | **Read** on **users** |
| **Invite someone** | **Create** on **users** |
| **Send the invitation again**, **Lock** · **Unlock**, **Reset second factor** | **Write** on **users** |
| **Delete** | **Delete** on **users** |
| Follow a permission group's name to its page | Read on **permissionGroups** — without it the names are shown but not linked |

Members of **Full Administrators** hold all of these. Somebody who opens the address directly
without the right sees *"You need administrator rights to see this page."*

## The register

Search by name or email address — given name, family name, the name shown, the sign-in name and
the address are all searched, and accents do not matter. The list is paged.

| Column | Shows |
|---|---|
| **Name** | The account's name; your own row is marked **you** |
| **Email address** | The address the account signs in with |
| **Status** | One or more of the tags below |
| **Opened** | The day the account was opened |
| **Actions** | The buttons your rights allow on that row |

| Status tag | Means |
|---|---|
| **Can sign in** | Not locked |
| **Locked** | Shut out — by an administrator, or for a few minutes after too many wrong passwords or codes |
| **No password yet** | Invited, but its owner has not chosen a password |
| **Full administrator** | A member of Full Administrators who can sign in. A *locked* full administrator is not tagged, because they could not act — but they are still one, and become one again the moment they are unlocked |
| **Second factor on** | At least one second factor is switched on |

**Expand a row** to see the permission groups the account belongs to — marked **member** when
it is named directly, or *through …* naming the caving groups that bring it in. An account in
nothing else shows *"Only the group every account belongs to."* Membership is changed on the
[permission groups](permissions.md#permission-groups) page, not here.

## Inviting somebody

**Invite someone** asks for a **Name** and an **Email address**, and **Open the account** opens
it:

> *An account is opened for this address. Its owner receives a link to choose their own
> password; you never learn it.*

The account is made exactly as registering would make it: the same rule that an address can
hold only one account (*"An account with this address already exists."*), an entry on the
caver roster, and the installation's default permission groups. Inviting works **whether or not
open registration is switched on** — on an installation with registration closed, it is the
only way in.

**The link** is single-use and lasts **24 hours**. Any change to the account's credentials
ends it early.

| What happened | What you see |
|---|---|
| The invitation was emailed | *"An invitation was sent to …"* |
| No mail server is configured, or sending failed | The link itself, in a dialog — see below |

### When the invitation cannot be emailed

The link is shown to you **once**, in a dialog titled **Hand this link over** that a stray click
outside cannot close:

> *Copy it now: it is shown only this once. It works once and for 24 hours; after that, send
> the invitation again.*

Copy it, close the dialog with **I have copied it**, and give it to the person yourself, by a
channel you trust. Nothing on the server keeps the link, so a lost one cannot be shown again —
send a new invitation instead.

### Sending the invitation again

**Send the invitation again** appears on rows with **No password yet** — for an account nobody
has entered yet, because the first link ran out, went to a spam folder or was never handed
over. It sends a fresh link (or shows it, as above).

It is refused for an account that can already be signed in to, by a password or through a
linked sign-in provider: *"This account can already be signed in to. Its owner resets their own
password."* A link that sets the password of such an account would be a way into somebody
else's account, not an invitation.

## Locking and unlocking

**Lock** acts at once, with no confirmation: *"… is locked out."* A locked account cannot sign
in until somebody unlocks it. **Unlock** lets it back in (*"… can sign in again."*) and clears
the count of wrong passwords, so the next mistyped one does not lock it straight out again.

A lock does not remove anything: the account, its roster entry and everything it added stay as
they were.

## Resetting a lost second factor

For a member who has lost their authenticator **and** their recovery codes, and so has no way
back in alone. **Reset second factor** appears only on accounts with **Second factor on**, and
asks first:

> *Every second factor they set up is switched off — the authenticator app, codes by email or
> text message — and their recovery codes stop working. They are signed out on every device and
> are told by email. They can then sign in with their password alone and set up a second factor
> again from their security settings.*

There is no way to switch a second factor *on* for somebody — only off. The reset is recorded in
the [audit log](../features/history-and-audit.md), naming which methods were switched off and
nothing they held.

## Deleting an account

**Delete** asks first, naming the account:

> *They will no longer be able to sign in. Their entry on the roster and the trips they were on
> stay.*

| What a deletion removes | What it leaves |
|---|---|
| The account: the person can no longer sign in | Their entry on the [caver roster](../features/people-and-clubs.md#the-caver-roster), now with no account attached |
| | The trips they were on, which keep naming them |
| | Everything they added, which stays with the club |

The roster outlives the logins attached to it on purpose: deleting the person would orphan the
record of what they did. Removing a person from the roster is a separate act — see
[When somebody asks to be removed](../features/people-and-clubs.md#when-somebody-asks-to-be-removed).

Members cannot delete their own account from their settings; they are told to ask an
administrator.

## What the page refuses

The buttons for these are hidden where the page can tell in advance; where it cannot, the
refusal says so in words.

| Refused | Why, as the page says it |
|---|---|
| Locking, deleting or resetting the second factor of **your own** account | *"You cannot lock your own account."* · *"You cannot delete your own account."* · *"You manage your own second factor from your security settings."* — see [Security](../features/account-and-settings.md#security) |
| Locking or deleting **the last full administrator** who can sign in | *"This is the last full administrator who can sign in. Make somebody else a full administrator first, or the installation would have nobody to administer it."* |
| Locking, unlocking, deleting or resetting the second factor of **a full administrator's account**, unless you are one yourself | *"Only a full administrator can lock, unlock, delete or reset the second factor of a full administrator's account."* — otherwise a delegated right over accounts would be a way to take the highest seat down |
| Resetting the second factor of an account that has none switched on | *"This account has no second factor switched on, so there is nothing to reset."* |
| Sending the invitation again to an account that can already be signed in to | *"This account can already be signed in to. Its owner resets their own password."* |

## What this page does not do

- **It never shows or sets a password.** The owner chooses it through the link; you never learn
  it. Once the owner has chosen one, resetting it stays with them.
- **It does not change permissions.** Membership of permission groups is edited on the
  [permission groups](permissions.md#permission-groups) page.
- **It does not edit a person's profile.** Names, addresses and what others may see of them
  belong to the person, in [their own settings](../features/account-and-settings.md).

---

Related: [Permissions explained](permissions.md) ·
[People and clubs](../features/people-and-clubs.md) ·
[Your account and settings](../features/account-and-settings.md) ·
[Messaging and delivery](messaging.md) ·
[← Administration](README.md)
