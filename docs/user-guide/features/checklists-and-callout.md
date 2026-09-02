# Checklists and the callout

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/checklists-and-callout.md)

[← Feature reference](README.md) · Related: [Trips](trips.md)

---

Two safety-adjacent mechanisms that live on a trip. Neither of them stands in anybody's way;
both of them are about a party knowing where it stands.

---

# Checklists

**Activity → Checklists.** The lists a party works through before it sets off: permit, key
collected, gear booked, whatever your club actually checks.

## Writing one

**New checklist**: a title, a description, **who may read it**, and the **lines**.

Visibility: *Only me · My caving group · Anyone signed in · Everyone.*

> **An administrator's published default is nothing more than a list with a wide audience.**
> There is no special "official checklist" concept — a list everyone may read is how one is
> published for the whole installation.

## Attaching one to trips

A **trip purpose** points at the list trips of that kind work through. So "every survey trip
works through the survey checklist" is one setting on the purpose, not a step on every trip.

## Using one on a trip

The trip's page shows the list as **lines to tick off**, with a quiet *3 of 7* on the trip
listings.

- Ticking records **who said so and when**.
- **Rewording a line later does not lose what was already confirmed.**
- The page states it plainly: *"This is a reading for the party, not a rule. Nothing is
  refused because a line is unsettled."*

## What checklists never do

- **Nothing is ever refused because a list is unfinished.** It tells the party where they are;
  it does not gate anything.
- It **decides nothing about who may read the plan**.
- **Lists you may not read are not named to you**, and no count of them is shown either.
  A trip whose checklist you cannot see says *"This trip works through no checklist you can
  see."*

Deleting a checklist loses the confirmations trips made against its lines, and the
confirmation says so.

---

# The callout

A trip can say **when its party is due back**, and be told when the party is not out.

## Arranging one

On the trip: **Arrange a callout**.

- **Expected back** — when the party expects to be out.
- **Raise the alarm at** — the hour to raise the alarm if nobody has said so. It cannot be
  earlier than the expected return.

## What happens when it fires

A pass runs on a schedule. When that hour goes by with nothing said, **everyone the trip
concerns who may read it gets an email**.

That email names the trip, its date and the expected hour in UTC — and **never a cave**.
Whoever runs a search reads the trip, where the answer is already kept under the rules that
decide who may have it.

The trip's page shows **This party is overdue**.

## Standing it down

**The party is out** — one tap.

**Anybody the trip names or has asked can do this**, whether or not they may edit the record,
because the person who knows the party is out is the one on the trip.

Standing down **disarms the check rather than erasing it**, so what was arranged stays on the
trip.

## States

| State | |
|---|---|
| **None arranged** | |
| **Watching** | Armed |
| **Overdue** | The alarm fired |
| **Party is out** | Stood down |

## When the watcher itself has stopped

A trip showing an armed alarm also says **when the check last ran**.

> **Silence is the good news in a callout**, so a watcher that has stopped and a party safely
> underground look identical. The page comes down on the side of saying nobody has looked:
> *"An alarm is set, but the check that would raise it has not run lately."*

## Reminders, and what stops them

The same pass **reminds the people on a trip that it is coming up**, once.

A trip that has been **called off or put back stops both** the alarm and the reminder,
because the date they were set against is no longer one anybody is going on.

## What a callout is not

**Delivery is the ordinary email queue's** — it tries several times and then gives up.

> An alarm is **a prompt to go and look**, not a guarantee somebody was reached.

Do not build a rescue procedure that assumes the email arrived. Build one that assumes
somebody is watching the clock and the email is a helpful nudge.

The callout notification **cannot be switched off** and is **never held for a daily
summary** — *"an overdue party is nobody's news in the morning."*

---

Related: [Trips](trips.md) · [Notifications](notifications.md)
