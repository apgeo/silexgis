# Messaging and delivery

[← Administration](README.md) · Related:
[Notifications](../features/notifications.md)

---

**Administration → Messaging** configures where email and text messages go, what signing in
requires, and one location-protection switch. **Administration → Message delivery** shows
whether any of it is actually working.

> These can also be set from the deployment's environment, and the page says so. Setting them
> here is the route that does not need a restart.

---

## Email

| Field | |
|---|---|
| **Enabled** | |
| **Server** · **Port** | |
| **Connection security** | Automatic · TLS on connect (465) · None (plain text) |
| **Username** · **Password** | The page says whether a password is stored; leave blank to keep it |
| **Sender address** · **Sender name** · **Reply-to address** | |
| **Timeout** | Seconds |
| **Accept an invalid certificate** | *Only for an internal relay with a self-signed certificate.* It removes the protection on the path |

Status is stated plainly: **"Email is configured"**, or:

> *"No mail server configured. Messages are written to the server log instead of being sent."*

**Configure nothing and the application still runs.** Sign-in links, confirmation links and
codes go to the server log. That is a legitimate way to run a small installation — as long as
whoever needs a link can read the log.

## Text messages

Any SMS gateway that speaks HTTP. Same shape: configured, or *"Texts are written to the
server log instead of being sent."*

### The money guard

An installation can allow **announcements** to travel by text. It is **off unless switched
on**, one person cannot send them in a stream, and a day's spending has a **ceiling** an
administrator sets and can see on the delivery page.

> The ceiling counts **the segments a carrier bills**, not messages. **One diacritic makes the
> same wording cost two of them** — so a ceiling means roughly **half as many messages to
> members who read Romanian as to members who read English**.

Set the ceiling with that in mind, and tell whoever writes announcements.

## Sign-in policy

What signing in requires — including which two-factor methods this installation offers.
Methods not enabled here are marked *"Not enabled on this installation"* on members' security
pages.

## Location protection

One switch, described in full under
[Location protection](location-protection.md#the-attachment-association-setting):

**"Show attachments of protected caves to everyone who can read them."**

Documents attached to a protected cave are **always served** — they are never hidden or
stripped out. What this switch controls is whether the *association* between the document and
that particular cave is disclosed.

Turning it on **never reveals a position**: following the link still gives the protected view.

---

## Message texts

**Configuration → Message texts.** The wording of every message, editable per language.
See [Vocabularies](vocabularies.md#configuration--message-texts).

---

## Watching the mail go out

**Administration → Message delivery.**

The numbers at the top:

- how many messages are **waiting**,
- how many are **held for a summary**,
- how many have been **given up on**,
- and the one that matters: **how long the oldest one that has not left has been waiting.**

Below, the ones worth looking at: what each was about, **who it was for — under the name they
may be shown under and never their address** — and what the far end said, with anything
address-shaped taken out of it.

### Putting one back by hand

A message that has given up can be retried. Retrying **asks every question again before it
sends**:

- somebody who has since said they do not want that kind of message **does not get one**,
- somebody who asked for a daily summary **gets it in their summary**,
- and **every such act is recorded against the person who made it**.

So a retry is not a replay. It is a fresh send under current rules.

### Retention

How long notifications are kept is a number an administrator can change, over whatever the
deployment configured.

---

## The quiet window

An installation can set a window during which **nothing leaves the server** — read in **each
recipient's own time zone**, so it means the same hour of the night whatever the season.

What is waiting sits in members' in-app lists the whole time, because a list wakes nobody.
Warnings a member may not switch off ignore the window — as does the overdue-party alarm.

---

## Announcements

Whoever is entrusted with it can write one line to a caving group. See
[People and clubs](../features/people-and-clubs.md#announcing-to-a-club).

> **Being allowed to edit a club's roster is not the same as being allowed to write to
> everyone on it.** The two rights are granted separately.

---

## Troubleshooting checklist

| Symptom | Look at |
|---|---|
| Nobody is getting anything | Is email *Enabled*? Does the page say *"written to the server log"*? |
| Some people get it, some do not | Their own [notification grid](../features/notifications.md) — a member may have switched that kind off |
| Messages queue and never leave | The **oldest waiting** figure on Message delivery, and what the far end said |
| Texts stop partway through a day | The **daily ceiling** — and remember it counts segments |
| Somebody says a message named a cave | It cannot have. Messages never name caves. Check whether they mean the *page* they followed the link to |

---

Related: [Notifications](../features/notifications.md) ·
[People and clubs](../features/people-and-clubs.md)
