# Location protection

🇬🇧 **English** · 🇷🇴 [Română](../ro/admin/location-protection.md)

[← Administration](README.md) · Related: [Permissions](permissions.md)

---

The mechanism that lets an installation say **"this cave exists and you may read about it,
but you may not know where it is."**

It is the speleology-specific reason this application exists rather than a general-purpose
GIS being used instead, and it is worth understanding properly even if you never administer
anything.

---

## The idea

**That a cave exists** and **where a cave is** are different facts with different audiences.

A cave marked with **Protected location** stays readable: its name, code, description,
history, documents, the trips that named it. What is withheld is the **exact position** —
and everything from which the exact position could be reconstructed.

Seeing an exact protected position is its own permission action: **Exact location**
(`ViewExactLocation`). It is granted like any other action, at any scope. See
[Permissions](permissions.md).

## Setting it

On a cave or feature, in the **Access** section: **Protected location**.

> *"Exact coordinates and precise-location fields are hidden from users without explicit
> permission."*

It can also be set:

- on the map's **New entrance** form (*Protect the exact location*),
- in bulk during a [GPS import](../features/geodata.md) — *Mark everything created as a
  protected location*,
- **by inheritance**: containment passes protection **downwards**, so protecting a karst area
  protects the features inside it.

---

## Every path it covers

This is the important table, because a protection that covers the map and not the export is
not a protection.

| Path | What a reader without the right gets |
|---|---|
| **The map** | An approximate position, labelled *"Approximate location — exact coordinates are protected"*, or *"Location withheld"* for a geometry |
| **Tables and lists** | Precise-location fields withheld |
| **Exports** (GeoJSON, GPX, KML, CSV, shapefile) | The same protection as the screen |
| **Share links and shared views** | The same protection. Sharing is not a route around it |
| **Photographs** | The **original** of a photo whose capture point would place the cave is refused. The rendering is shown |
| **Trip pages and reports** | Caves you may not open are **not named**; you are told *how many* are not named |
| **Notification emails** | **Never name a cave.** Not trip mail, not the overdue-party alarm |
| **The calendar** | A row **never names a cave**, nor a count of caves left out |
| **Change history** | A protected value reads *"Value hidden (protected location)"* |
| **Closest approach** | Refused unless you may place **both** caves |
| **Import duplicate checking** | Only objects whose exact position you may see are measured against — a protected cave you cannot locate is not silently used as a match |
| **A `.stl` origin** | The pre-filled origin says it is deliberately approximate and asks for the true point |
| **QR landing pages** | No cave, no place, no position — for anybody, account or not |
| **The phone sync API** | A position you may not have is **simply absent** |
| **Links** | A link shows each reader only the ends they may see |

**None of this is done by the client.** The withholding happens on the server. A modified
browser, a direct API call and a scraped page all get the same answer.

---

## What is *not* withheld

Deliberately:

- **That the cave exists**, its name and its code.
- **Its documents.** A protected cave's documents are always served — they are never hidden
  or stripped out.
- **That a list is short.** You get *"n not shown to you"* rather than a quietly incomplete
  list. Hiding the count would be its own kind of leak.

### The attachment-association setting

There is one deliberate exception, and it is a switch:

**Administration → Messaging → Location protection → "Show attachments of protected caves to
everyone who can read them."**

Off by default. Location protection normally withholds *the fact that a document points at
that particular cave*, while keeping the document itself readable. Turning this on discloses
the association.

> **Turning it on never reveals a position.** Following the link still gives the protected
> view.

---

## Things that are deliberately *not* approximated

Two, and both carry a warning on their own form:

**A trip's sketch.** *"Exact location — a trip's sketch is never approximated. Everyone who
may read this trip sees this shape exactly as drawn, whatever protection the caves the trip
names carry."*

**A trip's meeting point.** *"Exact position — where the party gathers is never approximated.
A meeting point near a guarded entrance places that entrance for everybody invited."*

These are not oversights. A sketch and a meeting point are things a person drew for a
specific audience, and approximating them would make them useless for the purpose they were
drawn for. The application's answer is to **warn loudly at the moment of drawing** rather
than silently degrade the data.

**Tell your members about these two.** They are the most likely way a protected entrance gets
disclosed in practice.

---

## Deciding your club's policy

The application gives you the mechanism. The policy is yours, and it is worth writing down:

- **When is a location protected?** By protection class? By landowner request? By exploration
  status? A rule you can apply consistently beats case-by-case judgement.
- **Who holds *Exact location*, and at what scope?** Everything, a feature set, one subtree?
- **What happens when somebody is invited on a trip to a cave they cannot place?** The
  application tells the cave's owner and the full administrators, and says explicitly that
  nobody else was told. Somebody has to act on that message — decide who.

---

Related: [Permissions](permissions.md) ·
[Sharing and public pages](../features/sharing-and-public-pages.md) ·
[Trips](../features/trips.md)
