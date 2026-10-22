# Administration

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/README.md)

[← Back to the guide](../README.md)

---

Pages for whoever holds administrative rights on the installation. These are still *user*
documentation — how to operate the application, not how to install it.

For installing, upgrading, backing up and TLS, see [INSTALL.md](../../INSTALL.md) and
[DEPLOY-SERVER.md](../../DEPLOY-SERVER.md).

| Page | Covers |
|---|---|
| [Accounts](#accounts) | Who can sign in: inviting somebody, locking an account, resetting a lost second factor, deleting an account |
| [Permissions explained](permissions.md) | Permission groups, rules, scopes, per-object grants, "why can this person see that?" |
| [Location protection](location-protection.md) | The protection mechanism end to end, and every path it covers |
| [Vocabularies your club owns](vocabularies.md) | Document kinds, feature kinds, trip purposes, trip roles, camp roles, report layouts, link relations, detection rules, message texts, feature sets |
| [Messaging and delivery](messaging.md) | Email, SMS, message texts, sign-in policy, watching the mail go out |
| [Building terrain](terrain-builds.md) | Turning elevation data into the ground the 3D scene draws |
| [Maintenance sweeps](maintenance.md) | Backfilling text, document pages and photo positions for files that arrived before those worked; where errors seen in members' browsers are recorded |
| [Published trips](../features/live-tracking.md#everything-published-for-full-administrators) | Every follow link the installation has handed out and what each opens; replacing a link, unpublishing a trip or everything, and the "protected cave nearby?" warning |

---

## "Administrator" is not a rank

There is no single admin role. **Each administration page follows its own domain**, and a
person's rights happen to include some of them and not others.

That means your rail may show *Audit* but not *Messaging*, or *Terrain* but not *Permission
groups*. That is working correctly.

The one genuine exception is **Full Administrators**: membership itself is the grant, decided
before any rule is consulted. That group holds no rules, and the application refuses any
change that would leave no active Full Administrator able to sign in.

## The rights each administration page needs

| Page | Needs |
|---|---|
| Audit | Read on the **audit** domain |
| [Accounts](#accounts) | Read on **users** to see the register. Inviting, locking, unlocking and resetting a second factor need **Write**; deleting needs **Delete** |
| Permission groups | Rights over the **permissionGroups** domain |
| Messaging, Message delivery | Rights over **settings** |
| Message texts | Rights over **messageTemplates** |
| Terrain | Read on **terrain** (starting a build is a separate right) |
| Feature sets | Rights over **featureSets** |
| Document kinds, feature kinds, trip purposes, trip roles, camp roles, report layouts | **Write** on taxonomies — because authoring a kind's schema decides what every record of that kind may say |
| Link relations | Full Administrators |
| [Published trips](../features/live-tracking.md#everything-published-for-full-administrators) | Full Administrators — it lists every trip's links whatever the trip's own access rules say, and can take all of them back |
| Detection rules | Anybody who can create features. Only *promoting* a set to what a group or the installation inherits is an administrator's act |
| [Maintenance sweeps](maintenance.md) | **Run** on the **jobs** domain (no interface page yet — triggered through the API) |

## Accounts

**Administration → Accounts** is the register of everyone who can sign in. Search it by name or
email address; each row says whether the account **Can sign in**, is **Locked**, has **No
password yet**, has a **Second factor on**, or belongs to a **Full administrator**. Expand a row
to see the permission groups the account belongs to — named directly, or reached through one of
its caving groups. Membership is changed on the [permission groups](permissions.md#permission-groups)
page, not here.

| Action | What happens |
|---|---|
| **Invite someone** | Opens an account for a name and an email address. Its owner receives a link to choose their own password — you never learn it. The account gets a roster entry and the installation's default permission groups, exactly as registering would, and inviting works whether or not open registration is switched on |
| **Send the invitation again** | For an account nobody has entered yet. Refused for one that can already be signed in to — its owner resets their own password |
| **Lock** · **Unlock** | Act at once. A locked account cannot sign in until it is unlocked |
| **Reset second factor** | For a member who has lost their authenticator *and* their recovery codes. Every second factor they set up is switched off and their recovery codes stop working; they are signed out on every device and told by email, and they then sign in with their password alone and set a second factor up again |
| **Delete** | Asks first, naming the account. The person can no longer sign in, but **their entry on the roster and the trips they were on stay** |

**When the invitation cannot be emailed** — no mail server, or sending failed — the link is
shown to you **once**, in a dialog that a stray click cannot close: *"Copy it now: it is shown
only this once."* It works once and for 24 hours; after that, send the invitation again. Hand it
over yourself, by a channel you trust.

What the page refuses, and says so in words:

- locking, deleting or resetting the second factor of **your own** account — your own second
  factor is managed from your [security settings](../features/account-and-settings.md#security);
- locking or deleting **the last full administrator** who can sign in;
- acting on **a full administrator's account** unless you are one yourself — otherwise a
  delegated right over accounts would be a way to strip the highest seat of its second factor.

---

## Things worth setting up early

1. **Permission groups** — before there is data to get wrong.
2. **Caving groups** and the caver roster — most other things reference them.
3. **Document kinds** and **trip purposes** — because they decide what every document and
   report of that kind is asked for, and changing them later is easier than un-filing a
   thousand documents.
4. **Messaging** — or accept that links and codes go to the server log instead of being sent.
5. **Your club's rule on when a location is protected** — a policy decision, not a setting.
