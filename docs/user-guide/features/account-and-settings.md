# Your account and settings

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/account-and-settings.md)

[← Feature reference](README.md) · Related: [Notifications](notifications.md) ·
[Coordinates](coordinates.md) · [Mobile and offline sync](mobile-sync.md)

---

**Settings** has ten pages: Profile · Account · Emails · Notifications · Security ·
Accessibility · Saved filters · Coordinates · Sync · Advanced. It is reached from the account
menu at the top right, which is also where you **Sign out**.

---

## Profile

### Identity

**Username** · **Email address** · **First name** · **Surname** · **Display name**

> Your **display name** is shown beside everything you add. Without one you appear under a
> generated name. Set it early.

### About you

**About** (a short bio) · **Phone number** · **Caving club** · **Profile picture**

The profile picture can be uploaded (under 5 MB) or chosen from the built-in set — bat,
helmet, headlamp, rope, karst, stalactite, compass, sump.

Your phone number is changed under **Security**, because a code is texted to the new number
first.

**Caving club** is picked from the installation's list of caving groups; typing narrows it,
ignoring case and accents. If your club is not listed yet, the list offers **Create «name»** —
choosing it adds the club to the caving groups and selects it, without leaving the form. A name
that differs from an existing club only in case or punctuation is not a new club: the existing
one is offered instead. The offer is made to anybody who may add caving groups — every account,
unless your installation has taken that right away.

### Addresses

Where you can be reached. Each address carries a **label**, **country**, **city**, **street
and the rest**, and optionally **a point on the map** you pick or type.

### Per-field audiences

**The small icon beside a field sets who can see it**: *Only me · My caving groups ·
Signed-in members*. That is per field, not one setting for the whole profile — so your name
can be visible to everyone while your address is not.

---

## Account

**Username** — you always sign in with your email address, so changing this only changes how
you are listed.

**Accounts are not deleted from here.** *"Anything you have added stays with the club, so ask
an administrator."*

**Export your data** — a copy of your profile, addresses, settings, saved filters and a list of
what you have added. Queued, prepared in the background, then downloadable.

**Calendar feed** — shown only when an administrator has switched feeds on. Creates the secret
address a phone or desktop calendar subscribes to, lists the ones you have by name and date, and
revokes one. See [Subscribing to your calendar](calendar-feed.md).

---

## Emails

Your address, and whether it is **confirmed**. If it is not, **Send a confirmation link**.

**Changing your address**: you give the new one, a link goes to it, and **nothing changes
until you open that link**. Until then you keep signing in with the current one. The page
shows the pending change, lets you **send it again** (with a short cooling-off period) or
**cancel the change**.

---

## Notifications

See [Notifications](notifications.md) — it has its own page.

---

## Security

### Password

Change the password you sign in with: your **Current password**, then the new one twice. A
password needs at least 10 characters.

> *"Changing your password signs you out everywhere, including here. You then sign in again with
> the new one."*

An account that signs in only through an external provider has no password here, and the card
says so. You are sent an email whenever your password is changed or reset — once — so a change
you did not make does not go unnoticed.

### Two-factor authentication

Ask for a second proof of identity when you sign in. Three methods:

| Method | |
|---|---|
| **Authenticator app** | A six-digit code from an app on your phone. **Works without a signal** — the strongest |
| **Emailed code** | To your confirmed address |
| **Texted code** | To your confirmed number |

Each can be turned on or off individually, and one can be made the **default**. A method your
installation has not enabled is marked as such; one that needs a confirmed address or number
says which.

Setting up an authenticator app: scan the QR square, or add the key by hand, then type the
code it shows.

**Recovery codes** are shown **once**. Store them then. The page tells you how many are left.

Lost both the authenticator and the recovery codes? An administrator who manages accounts can
**reset your second factor**: every method is switched off, you are signed out everywhere and
told by email, and you sign in with your password alone and set a second factor up again. See
[Accounts](../admin/README.md#accounts).

Too many wrong codes locks the account for a while.

### Phone number

Add or change your number — a code is texted to it and you confirm. Enter it in international
form (`+40712345678`). It is used **only for sign-in codes**.

Removing your number switches texted codes off with it, and says so. **If texted codes are your
only second factor**, it asks a second time — *"Remove your only second factor?"* — because
two-factor sign-in then goes off and your password alone protects the account; you get an email
saying so. If the installation has no SMS gateway, texted codes are simply unavailable and the
page says why.

### Losing a device

> **Changing or resetting your password revokes every token and every authorisation on your
> account — every client, every device, including the one you are using.**

That is the answer to a lost or stolen phone, and it is **one action any caver can reach**
without waiting for an administrator. You are signed out everywhere, and you sign back in on
the devices you still have.

It works this way because the person acting cannot list their own sessions and cannot name the
device that was taken — so the only honest thing to revoke is all of them.

See [Mobile and offline sync](mobile-sync.md#signing-a-device-in-and-taking-it-back).

### Linked accounts

If your installation offers external providers, sign in with **Google, GitHub or another
OIDC provider** in addition to your password. Link and unlink from here.

A provider proves who you are at the provider and nothing more: **with two-factor sign-in on,
you are still asked for your code** after signing in through one.

---

## Accessibility

### Appearance (follows your account to every device)

| | |
|---|---|
| **Theme** | Match my system · Light · Dark |
| **Density** | Comfortable · Compact |
| **Reduce motion** | Turns off animations and map fly-to movement |
| **Overview map on the map** | The small inset showing where the map's view lies in a wider area |
| **Language** | English · Română — the same choice as the switch in the top bar; see [Language](#language) |

Both themes use the interface library's own colours. Moving through a page with the keyboard
draws a ring around the control that has the focus, in the light theme and the dark alike; the
first press of Tab offers **Skip to main content**, and on the map page **Skip to map**. Press
**?** for the list of keyboard shortcuts.

### This browser only

*"These stay on this device — a phone and a workstation want different answers."*

| | |
|---|---|
| **Open the app on** | The map · The dashboard |
| **Hide the on-map controls** | |

**Reset these** puts back the two settings above together with the theme, the density and
reduced motion. It leaves the language and the overview map alone, and it does not touch the numbers under
[Advanced](#advanced) — those have their own way back there.

---

## Saved filters

| | |
|---|---|
| **Share new filters with** | Only me · Members of one of your caving groups · Everyone signed in |

This is where a filter you save **starts out**: every save dialog pre-selects it, and you can still
choose differently each time. It is **Only me** until you change it, and it follows your account
to every device. Sharing gives others the question only — each of them is answered with what they
themselves may see. If the caving group chosen here is no longer one you can share with, new
filters start out private until you choose again. See
[Saved filters](search-and-filters.md#saved-filters).

---

## Coordinates

How positions are written wherever one is shown or copied. *"These settings follow your
account."*

| | |
|---|---|
| **Coordinates shown as** | The format every position is written in — decimal degrees, degrees and minutes, degrees–minutes–seconds, UTM, MGRS, Stereo 70, the Swiss grids or Plus Code |
| **Also listed under "Other formats"** | The formats a position shown in detail — a selected entrance, a cave's entrances — can also be opened in, each with its own copy button |
| **Settings of each format** | For instance the UTM zone kept near a zone's edge, with a note on how far each format can be trusted |

A sample point shows every chosen format live as you change them. A format with no reference for
a place — a national grid outside its country — is left out there. Everything about the formats,
protected positions and typing a position in any of them is on
[Coordinates and the converter](coordinates.md).

---

## Sync

For the companion phone app. See [Mobile and offline sync](mobile-sync.md).

---

## Advanced

Numbers that decide how much the application asks of the machine in front of you. They are kept
**in this browser only** and do not follow your account — a phone and a workstation want
different answers.

### Cave walls in the 3D view

Three numbers bound the 3D view when it draws the
[walls of every cave in view](3d-view.md#walls-of-every-cave-in-view):

| | |
|---|---|
| **Show from zoom** | The zoom from which that mode draws anything; below it the scene asks you to zoom in. 1 to 22 |
| **Caves at most** | How many caves' walls are drawn at once, nearest the middle of the view first |
| **Megabytes at most** | How much wall model is downloaded and held for them. A browser needs roughly twice that in graphics memory |

**An empty field follows your installation's default**, which is shown in grey in its place —
14, 12 and 64 MB unless your administrator changed them. A number you type replaces the default
in this browser: raise it on a machine with a real graphics card, lower it on a phone.

The last two stop at a ceiling your administrator sets — 60 caves and 512 MB unless changed —
and the page says what it is. If the ceiling is lowered later, a larger number you had stored is
held to it.

**Use this installation's defaults** in this card empties all three.

The numbers are in force the next time you open the 3D view. A 3D view already open in another
window keeps the old ones until it is reloaded.

**Walls of the selected cave** has one number: **Ask above (MB)**. Selecting a cave in the 3D view
downloads its walls straight away unless they are larger than this; then the view says how large
they are and waits until you choose to download them. Empty follows your installation's default
(50 MB unless changed); **0** asks before every download. Its own **Use this installation's
defaults** button empties it.

### Survey lines on the map

Two numbers bound the survey lines the flat map draws. They are the same two the map's layer
list shows while **Cave centerlines** is switched on — see
[Map workspace](map-workspace.md#centerline-detail) — and a number set in either place is the
one in force in both.

| | |
|---|---|
| **Detail from zoom** | The zoom from which full survey detail appears, splays included; below it you get passage outlines. 1 to 22 |
| **Line budget** | How many lines one view may hold. Past it the remaining centerlines are held back, and the map says how many |

**An empty field follows your installation's default**, shown in grey in its place — 18 and
25,000 unless your administrator changed them. Bring the detail in earlier and raise the budget
on a fast machine; push both the other way on a phone.

The line budget stops at a ceiling your administrator sets — 100,000 unless changed — and the
page says what it is. If the ceiling is lowered later, a larger number you had stored is held to
it.

**Use this installation's defaults** in this card empties these two and leaves the walls' three
as they are; the button in the walls' card does the reverse. Each card hands back only its own
numbers, so undoing an experiment with one does not discard what you tuned for the other.

These two bound the flat map only: the 3D view draws survey lines by the installation's numbers.

---

## What follows your account, and what stays in this browser

Most of what you arrange is kept **with your account**, so it is there when you sign in on
another computer:

- the appearance — theme, density, reduced motion, the overview map — and the language;
- the coordinate formats, and who new saved filters are shared with;
- how the workspace is arranged: panel arrangements, named layouts, the docks, the feature types
  you pinned.

A few things stay **in this browser only**, because a phone and a workstation want different
answers: where the application opens, hiding the on-map controls, and every number under
[Advanced](#advanced). A named layout your account's stored preferences had no room for is kept
in this browser too, and the layouts menu marks it **This browser only** — delete a layout you no
longer use and it goes to your account with that change.

A change made just before you close the page, or in a [popped-out window](saved-views-and-windows.md#pop-out-panels),
is still written to your account. **Another member signing in on the same browser starts from the
defaults**, not from your arrangement.

---

## Signing out

**Sign out**, in the account menu, ends your sign-in on the server as well as in the page:
opening the application again asks for your email address and password. Nothing of yours is
handed to whoever signs in next on that browser.

To sign out **everywhere** — a lost phone, a shared computer you cannot reach — change your
password; see [Losing a device](#losing-a-device).

---

## Language

The language switch is in the top bar, and the same choice is under
[Accessibility](#accessibility). English and Romanian ship with the application.

**The language follows your account**, so the messages the system sends you arrive in it too,
not only the screens. If the change cannot be saved to your account you are told — because
messages might still arrive in the previous language.

Dates follow too: calendars and date pickers show the month and weekday names of the language
you are reading in, and **start the week on the day that language does**.

---

Related: [Notifications](notifications.md) · [Coordinates and the converter](coordinates.md) ·
[People and clubs](people-and-clubs.md) · [Mobile sync](mobile-sync.md)
