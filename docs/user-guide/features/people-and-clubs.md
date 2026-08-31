# People, cavers and clubs

[← Feature reference](README.md) · Related: [Permissions](../admin/permissions.md)

---

There are two different notions of "person" in the application, and confusing them causes
most of the trouble people have here.

| | |
|---|---|
| **An account** | Somebody who can sign in. Has an email address, a password, settings, notifications |
| **A caver** | An entry in the club's roster. May or may not have an account |

A caver with no account can be named on trips, credited on photographs, listed as a permit
holder — but **cannot be granted access to anything**, cannot be sent a notification, and
cannot answer an invitation. They have to be reached another way.

---

## The caver roster

**People → Cavers.**

| Field | |
|---|---|
| **Name** · **Email** · **Phone** | |
| **Caving groups** | Which clubs they belong to |
| **Notes** | *"Only people who keep the roster can read these."* |

Marked **No account** where there is no sign-in behind the entry.

### Merging duplicates

Rosters accumulate duplicates — *Ion Popescu*, *I. Popescu*, *Popescu Ion*. **Merge** folds
one entry into another: their trips and caving groups move across, and the duplicate is
removed.

You cannot simply delete somebody named on trips. The application refuses and tells you to
merge their duplicate entry instead — which is the right answer, because deleting would
silently rewrite the record of who was underground.

---

## Caving groups

**People → Caving groups.** A club, a group, or an organisation.

Groups matter for three reasons:

1. **Visibility.** The *caving group* visibility means "members of the group this record is
   bound to".
2. **Permission scope.** A rule can be scoped to *a caving group's content*.
3. **Announcements.** See below.

### Membership and roles

Members hold a role in the group: **Member · Admin · Owner**. Manage the roster from the
group's page.

> **Being allowed to edit a club's roster is not the same as being allowed to write to
> everyone on it.** The two rights are granted separately. A club's founder can write to
> their own club from the start.

### Announcing to a club

**Announce** sends one line to a caving group. Every member with an account gets it **in
their own list, by their own choice of mail or in-app** — rather than as a mailing list
nobody can leave.

Before you send:

- you are told **how many people it reaches** (members without an account are not counted,
  and you are never sent your own announcement),
- **confirming is a step of its own**: *"This goes now to the people counted above. It cannot
  be taken back."*

A message to two hundred people is never one careless click. For a large club the notices are
written in the background, and it says so.

If nobody on the roster holds an account, it tells you there is nobody to tell.

> **Text messages cost money.** An installation can allow announcements to travel on a channel
> that charges. It is off unless switched on, one person cannot send them in a stream, and a
> day's spending has a ceiling an administrator sets. That ceiling counts the **segments** a
> carrier bills, not messages — one diacritic makes the same wording cost two of them, so a
> ceiling means **half as many messages to members who read Romanian as to members who read
> English**. See [Messaging](../admin/messaging.md).

---

## Accounts

Accounts are created by whoever runs the installation, unless open registration has been
switched on.

**Accounts are not deleted from the settings page.** *"Anything you have added stays with the
club, so ask an administrator."* — because deleting an account would orphan the record of
what that person did.

You can **export your own data**: a copy of your profile, addresses, settings and a list of
what you have added. It is prepared in the background and downloaded when ready.

See [Your account and settings](account-and-settings.md).

---

## Who can see what about a person

Profile fields carry **their own audience**, individually — the small icon beside each field:
*Only me · My caving groups · Signed-in members*. That includes addresses and the map points
attached to them.

There is no way to ask the application for one person's trips, hours or whereabouts by naming
them. Statistics about a person are computed **over what you may read**, and the screen says
so. See [Measurements and statistics](measurements-and-statistics.md#trip-statistics).

---

Related: [Permissions](../admin/permissions.md) ·
[Your account and settings](account-and-settings.md) · [Notifications](notifications.md)
