# People, cavers and clubs

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/people-and-clubs.md)

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

Somebody comes onto the roster in one of four ways: whoever keeps it adds them here; an
account gets an entry of its own when it is created; an imported sheet of old trips may be
told to create the people it names; and **a name typed on a trip, or on a
[camp's roster](camps.md#who-was-there), that nobody is recorded under adds them**. A typed
name somebody *is* recorded under means that person — the older entry, if two hold it — so
the same name written twice is one caver, not two. A name written two ways is two, which is
where most duplicates come from.

### Merging duplicates

Rosters accumulate duplicates — *Ion Popescu*, *I. Popescu*, *Popescu Ion*. **Merge** folds
one entry into another: their trips and caving groups move across, and the duplicate is
removed.

You cannot simply delete somebody named on trips: deleting would silently rewrite the record
of who was underground. The application refuses — and for a duplicate, merging is the answer.

### When somebody asks to be removed

A person who is not a duplicate, and who asks for what the application holds about them to be
removed, cannot be merged away. So the refusal says what holds them. Press the bin on their row
and, when trips still name them, a dialog opens — ***name* cannot be removed yet** — listing
**each trip you may open**, as a link, with what holds the person there:

- **On the roster** — the trip names them. Open the trip and take them off its roster.
- **Tracking reports: 3** — the trip's [tracking log](live-tracking.md) has reports about them,
  counting the ones already taken off the log and kept. Where you may edit that trip and its
  tracking has been started, **Remove their reports from this trip: 3** is offered beside it.

**Remove their reports from this trip** destroys every report that trip holds about the person —
the ones on the log and the ones under *Removed reports* — and asks first, saying exactly that.
It cannot be undone and nothing is left to put back. The trip's replay changes, and so does a
published page of it. **What stays:** their name on the trip's roster until you edit the trip,
and the trip's **History**, which records that the reports were removed and still shows what
each one said.

**What a published page shows afterwards.** A page published for the trip — the one being
followed now, or the replay of a past trip of the cave — lists the trip's roster and draws each
person from their reports. After the removal the person is still in the party list there, under
the name or caption the page already used, with nothing reported about them: no place, no *Went
in*, no *Came out*, and no dot at any moment of the replay. A page somebody has open shows this
at its next refresh, and so does one framed on another website. To take the name off the page as
well, edit the trip and take the person off its roster — which a running watch refuses for
somebody with reports, and allows once the reports are gone. None of this reaches what somebody
has already saved: a screenshot, or a movie made from the replay.

When something you are not shown also holds the person — a trip you may not open, a deleted
trip still waiting out its retention, more trips than the list names — the dialog says only
that: *Something not shown here also holds this person… Ask an administrator.* It never says
how many, or which.

Once nothing listed holds them, **Remove this person** appears in the dialog; nothing is
deleted until you press it. **Merge a duplicate instead** stays there as the other way out. A
person on a [camp's roster](camps.md#who-was-there) is refused separately, with a sentence
saying so.

---

## Caving groups

**People → Caving groups.** A club, a group, or an organisation.

Groups matter for three reasons:

1. **Visibility.** The *caving group* visibility means "members of the group this record is
   bound to".
2. **Permission scope.** A rule can be scoped to *a caving group's content*. A new group starts
   with such a rule for its members — read, correct and create the group's own content — which is
   why a member can
   [record a trip that belongs to the group](trips.md#when-your-right-to-record-trips-is-your-clubs)
   without holding any wider right.
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
