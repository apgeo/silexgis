# Notifications

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/notifications.md)

[← Feature reference](README.md) · Related:
[Your account and settings](account-and-settings.md) · [Messaging](../admin/messaging.md)

---

## The grid

**Settings → Notifications** is a grid: the **kinds of thing that happen** down one side, the
**ways of reaching you** across the top.

For each pair you say one of three things:

| | |
|---|---|
| **Off** | |
| **As it happens** | |
| **Daily summary** | Held and sent once a day |

Ways of reaching you: **In the app · Email · Text message.**

> A daily summary of something you are **not** told about by mail is not a thing you can
> accidentally ask for — the grid will not let you.

## What you can be told about

| Event | |
|---|---|
| Caving group membership changes | |
| Someone shares something with me | A permission grant |
| I am added to a trip | |
| A trip I am on was published as a page anybody with the link can read | See [Live tracking and published trips](live-tracking.md) |
| A trip recorded on a phone needs somebody to look at it, or could not be made into a trip report | See [Phone recordings](phone-recordings.md) |
| An account whose uploads are reviewed sent something that waits for a reviewer | For those who hold the review right; see [Reviewing uploads](upload-review.md) |
| A reviewer decided about something I uploaded | **Accepted**, **Partly accepted** or **Rejected** |
| Trip planning | Invited to a trip or a club event, a trip you are on changes, a trip needs access you can grant, or a trip or club event you answered about is coming up |
| An expedition I am on the roster of is changed or called off | Also when you are put on an expedition's roster |
| Someone replies to a comment I wrote | |
| Someone comments on something of mine | |
| Someone writes to a caving group I am in | An announcement |
| My uploads finish processing | |
| **Security alerts** | Cannot be switched off. Your password changed or reset, your email address changed, two-factor sign-in switched off — including when removing your phone number or an administrator's reset switches it off |
| **A trip I am on is overdue** | Cannot be switched off, never held for a summary |

The two comment settings are **deliberately separate**, so switching off traffic on a busy
document never costs you the answers meant for you.

## What the page tells you honestly

- **Warnings about your own account cannot be switched off**, and the page says so.
- Switch **every** way of reaching you off for one kind of event and that is allowed — but
  the page tells you plainly: *"This will not reach you anywhere."*
- A way of reaching you **this installation has not set up** is marked as such, and what you
  chose is **kept for the day somebody sets it up**.
- If there is no mail server at all: *"Your choices are saved and will be used once one is."*

## The quiet window

An installation can set a quiet window — **nothing leaves the server during your night**,
read in **your own time zone**, so it means the same hour of the night whatever the season.

What is waiting is in your in-app list the whole time, because a list wakes nobody. A warning
you may not switch off ignores the window.

---

## The inbox

A **bell in the top bar** counts what you have not read. Behind it, **Notifications** lists
what happened, newest first, filterable by kind, marked read as you open them or all at once.

- A line is **worded by the installation**, in the language you are reading the site in — so
  wording an operator has rewritten is the wording you see.
- A line about something you **can no longer open** says exactly that, in place of a name and
  a link. You are still told that it happened — hiding that would be its own kind of leak —
  but nothing about the thing itself survives your losing access to it.
- A line about a **trip** — you were added, invited, it changed or was called off — decides
  whether you may open the trip **when you open the line**, not only when it was written; a line
  about a finished **geodata import** or **map conversion** names the file or map it was about.
- A line saying that **somebody asked onto a trip cannot open a cave** leads to that cave's
  **Permissions**, opened about that person — see
  [An invitation grants nothing](trips.md#an-invitation-grants-nothing).

---

## What a notification never contains

> **A message about a trip names the trip and its date and never a cave.** An email is as much
> a copy of a protected location as anything on the screen is.

And:

- **No message ever repeats what was said** in a comment. It names the document and links to
  it; you open it and read the conversation there.
- If a remark is deleted the link still takes you to the right page. If the document goes,
  the line says the subject is no longer available rather than sending you to a dead link.
- Whether you may still read something is decided **for you personally at the moment the
  message is written**. Somebody who has lost access is told nothing.
- A **draft** tells nobody, since telling you a draft changed would be telling you it exists.
- A write that **changed nothing** sends nothing.

**Expedition notices** follow the same rules as trip notices: a changed or called-off camp tells
the people on its roster, never the person who made the change, never while it is a draft, and
only those who may read it. Being put on a roster tells you. **Inviting somebody to a club event**
for the first time tells that person only.

---

## Unsubscribing from an email

The *stop sending me this* link in a mail client **switches that kind of message off for mail
alone**, and leaves your inbox in the application exactly as it was.

The page you land on says which of the two it changed. It is reachable without signing in,
because you open it from a mail client.

---

## For administrators

Whether messages are actually leaving the server is a page of its own —
[Message delivery](../admin/messaging.md#watching-the-mail-go-out).

---

Related: [Checklists and the callout](checklists-and-callout.md#the-callout) ·
[People and clubs](people-and-clubs.md#announcing-to-a-club) ·
[Messaging](../admin/messaging.md)
