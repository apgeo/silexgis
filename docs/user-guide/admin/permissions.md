# Permissions explained

[← Administration](README.md) · Related:
[Location protection](location-protection.md) ·
[Core concepts](../core-concepts.md#2-visibility-permissions-and-location-protection-are-three-different-things)

---

SilexGIS has **editable permission groups instead of fixed roles**. There is no "Editor" or
"Moderator" built into the software; there are named rulesets somebody wrote, and you are a
member of some of them.

---

## The three things that decide access

Access to a record is decided by three independent mechanisms that compose:

### 1. Ownership

You own what you created. Ownership grants access by itself.

### 2. Visibility

A property of the record, set by whoever made it:

| Value | Admits |
|---|---|
| **Private** | Only the owner and anybody explicitly granted |
| **Caving group** | Members of the group it is bound to |
| **Authenticated users** | Anybody with an account here |
| **Public** | Anybody, including visitors with no account |

### 3. Rules

*Allow* or *deny*, per action, per domain, at a scope. Rules live in **permission groups**, or
directly on one object.

Above all three sits **Full Administrators** — membership decides before any rule is
consulted.

---

## Anatomy of a rule

A rule says: **[subject] is [allowed / denied] [these actions] on [this domain] within [this
scope]**.

### Subject

A **user** or a **caving group**. (Inside a permission group, the members are the subject.)

### Actions

| Action | |
|---|---|
| **Read** | |
| **Write** | |
| **Create** | |
| **Delete** | |
| **Share** | Mint share links |
| **Run** | Execute — e.g. starting a job |
| **Manage permissions** | Write rules on this thing |
| **Exact location** | See withheld coordinates — see [Location protection](location-protection.md) |

### Domains

Features · Trip logs · Geodata files · Georeferenced maps · Saved map views · File store ·
Documents · Expeditions · Map layers · Tags · Hierarchies · Taxonomies · Cavers ·
Caving groups · User accounts · Permission groups · Feature sets · Installation settings ·
Message texts · Audit log · Jobs · Terrain · Checklists · Calendar events

### Scopes

This is the part that makes the system practical rather than tedious.

| Scope | Reaches |
|---|---|
| **Everything** | The whole domain |
| **Own objects** | What the subject created |
| **A caving group's content** | Everything bound to that group |
| **A feature and everything inside** | A subtree of the feature hierarchy |
| **A feature set** | A named set of features — see below |
| **A cabinet and everything filed below it** | A branch of the filing tree |
| **One object** | A single record |

Rules also have a **level** — *global*, *collection* or *object* — which is what the conflict
resolution below is stated in terms of.

---

## How conflicts resolve

Two rules, stated on every screen that lets you write one:

> **A deny outweighs every allow at the same level, and a rule on the object itself outweighs
> anything inherited.**

So an object-level allow beats a global deny; a global deny beats a global allow.

**Check what it costs before saving.** A permission group containing a deny will not let you
save it without opening the preview first — see below.

---

## Permission groups

**Administration → Permission groups.**

A group has a **name**, a **description**, **members**, and **rules**. Three tabs: Rules,
Members, Preview.

> *"A rule lives here or directly on an object — never in a caving group."* Caving groups are
> about who is in a club; permission groups are about what people may do.

### Built-in and protected groups

Some groups ship with the product and are marked **Built-in** or **Protected**. In particular
**Full Administrators holds no rules**: membership itself is the grant, decided before any
rule.

The application refuses two things outright:

- anything that would **leave no active Full Administrator able to sign in**,
- a rule that would **hand out rights you do not hold yourself**.

### The preview

**Pick a user or caving group to see the rights they would hold.** Expand a row to see which
rule decided each action.

A ruleset containing a **deny** requires you to open the preview before saving. That is the
one piece of friction in the whole permission system, and it exists because a deny is the
thing that quietly breaks somebody's access three months later.

### Deleting a group

*"Members only lose what it granted."* Deleting a permission group does not delete anything
else.

---

## Per-object grants

Every object has a **Permissions** tab (where you may manage permissions on it).

Add a rule naming a **user** or a **caving group**, an effect, actions, and a scope of
**this object only** or **this object and everything inside**.

### Rules written by a camp

On a trip belonging to a camp, some rules show as **From a camp**. They were written by the
camp and are changed there, not here.

They are shown on the trip anyway, so **the whole list of who may read this trip is in one
place**.

---

## Feature sets

**Configuration → Feature sets.** A named set of features that access rules can be scoped to —
*"the way to say «these particular caves»"* without writing one rule per cave.

- Add and remove features by searching them.
- **Changing a set's features moves access**, because rules point at the set. Every change is
  recorded.
- A set that rules still point at cannot be deleted until those rules are removed.

Cabinets do the equivalent job for documents — see
[Documents and cabinets](../features/documents-and-cabinets.md#cabinets).

---

## "Why can this person see that?"

Every object carries **Your access here, and why**.

For each action it says **Allowed** or **Denied**, and names the reason:

| Source | |
|---|---|
| *Full administration: membership decides before any rule is consulted.* | |
| *Granted by ownership of the object.* | |
| *Granted by the object's visibility.* | |
| *Decided at the [object / collection / global] level by "[rule name]".* | |
| *Decided at the … level by a rule you cannot see.* | You are told a rule decided it, not what the rule says |
| *Decided by a rule written directly on this object.* | |
| *No rule grants this.* | |

This is the tool for every "but why can't Maria open that?" conversation. Start here.

---

## What permissions do not do

- **They do not override [location protection](location-protection.md).** Seeing an exact
  position is its own action (*Exact location*), granted separately.
- **They do not travel through an invitation.** Being asked onto a trip grants nothing.
- **They do not reach somebody with no account.** A caver in the roster who cannot sign in
  cannot be granted anything.

---

Related: [Location protection](location-protection.md) ·
[People and clubs](../features/people-and-clubs.md) ·
[History and audit](../features/history-and-audit.md)
