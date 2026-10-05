# Subscribing to your calendar

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/calendar-feed.md)

[← Feature reference](README.md) · Related: [Events and calendar](events-and-calendar.md) ·
[Your account and settings](account-and-settings.md)

---

Your trips, camps and events can appear in the calendar you already use — on your phone, on your
desktop, in whatever application reads a calendar subscription. The application hands you a
**feed address**; your calendar application polls it, shows what it finds and keeps polling, so a
date that moves, a trip you are added to or an event that is called off follows you there.

## Before it works

An administrator has to switch feeds on: **Administration → Messaging → Location protection →
"Let members subscribe to their calendar from outside."** Until then the section described below
is not shown at all. Feeds are off on a fresh installation, because offering them is a decision
about how far the club's dates may travel.

## Creating an address

**Settings → Account → Calendar feed.**

1. Give the address a name — the device it is going on, such as *Phone* or *Work laptop*. One
   address per device, so that one can be withdrawn without touching the others.
2. **Create a feed address.**
3. **Copy it now.** It is shown once. The application keeps only a fingerprint of it and cannot
   show it again — if you lose it, revoke it and create another.

> **The address is a secret.** Anybody who has it can read your calendar without signing in.
> Treat it like a password: do not post it, do not mail it around, and if it gets out, **revoke**
> it here and create a new one.

## Subscribing

Paste the address wherever your calendar application adds a subscription — it is variously called
*subscribed calendar*, *calendar from URL*, *internet calendar* or *add by URL*:

| Where | How |
|---|---|
| **iPhone / iPad** | Settings → Calendar → Accounts → Add Account → Other → Add Subscribed Calendar |
| **macOS Calendar** | File → New Calendar Subscription |
| **Google Calendar** (on the web) | Other calendars → + → From URL. Google polls slowly — hours, sometimes a day |
| **Outlook** | Add calendar → Subscribe from web |
| **Thunderbird** | New calendar → On the Network → iCalendar (ICS) |
| **Android** | Most phone calendars show what the Google account subscribes to; some let you add a URL directly |

How often the calendar re-reads the address is the application's choice, not ours. Phones poll
every few minutes to every few hours; there is no way to push a change to them sooner.

## What the feed carries

One entry per:

- **trip** you are on — named in the party, or asked to come,
- **camp** whose roster records a stay for you,
- **event** you were asked about and did not decline.

Each entry has the **title**, the **days** (and the hours, for an event with a time), and a
**link back** to the page in the application. A called-off entry is marked cancelled rather than
silently removed, so you see that it was called off.

That is all. **No place, no coordinates, no participants, no description** go into a feed — not
because they are filtered out, but because the feed is not built to carry them. A calendar
application on a phone, or the calendar service behind it, can never learn where a cave is from
the feed, whatever anybody later adds to the trip.

The feed shows **what you are on**, not everything you could read: the calendar page inside the
application shows every trip and event you may see; the feed shows only the ones that concern you.

## What the feed respects

The feed is read **as you**, every time it is polled. It never remembers what you were allowed to
see when you created the address:

- a trip you can no longer read drops out of the feed,
- an account that is locked gets no answer,
- an address you revoke stops answering at once,
- and if an administrator switches feeds off, every address stops at once.

## Withdrawing an address

**Settings → Account → Calendar feed** lists your addresses by name and date — never the address
itself. **Revoke** the one that is lost or no longer used. A calendar application that still has
it will simply stop updating; delete the subscription there too, so it stops asking.

---

Related: [Events and calendar](events-and-calendar.md) ·
[Your account and settings](account-and-settings.md) ·
[Location protection](../admin/location-protection.md)
