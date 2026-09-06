# The rules that keep the app working without a server

SpeleoLoc works today with no SilexGIS installation anywhere, and must go on working that way. That
is not a nice-to-have and it is not a migration phase: a caver underground with no signal, and a club
that never runs a server at all, are the ordinary cases this application was built for.

**Nothing in this integration enforces these rules from the server side, and nothing can.** The
server has no way to know whether the application still starts without a profile configured, whether
an import suspended change logging, or whether a coordinate was quietly copied into a property
document. Every rule below is held by the people writing the client, which is why it is written down
here rather than assumed. A rule whose consequence is not stated gets treated as style, so each one
carries the failure it prevents.

The list is a checklist on purpose. Work through it when the sync feature is designed, and again
before it ships.

---

## 1. The twelve invariants

### 1. The local database stays the sole source of truth for the running app

A server profile is exactly as optional as an FTP profile is today. Its absence fails soft, and a
build with the feature unconfigured behaves the same as today's build in every respect a caver can
observe.

*If this breaks:* the application starts needing a network to do something it used to do offline,
which is the one failure this whole design exists to avoid — and it will be discovered in a cave.

### 2. Client-generated UUIDs are identity

The device mints the identifier; the server adopts it verbatim and never re-keys. For rows the device
first meets by download, the device adopts the server's identifier verbatim in the same way. **No
mapping table exists on either side, ever.**

*If this breaks:* two identifiers for one cave, on two devices, with no way to tell they are the same
row — and every FTP archive made afterwards carries the split forward.

### 3. Server-driven imports run with change logging suspended

Applying downloaded rows is an import, not a user edit. Run it the way the existing import path runs:
with the change logger suspended.

*If this breaks:* every downloaded row is logged as a local change, the FTP upload gate sees work to
do, and the same rows travel back out in the next archive — archive ping-pong between devices, with
no user action anywhere in the loop.

### 4. Device-to-device conflict rules are untouched

Last-writer-wins on the row timestamp, and tie-goes-to-delete, remain the law between devices and in
FTP archives. **The server arbitrates its own writes differently — on a base revision — and that
difference is deliberate.** Do not "fix" one to match the other.

*If this breaks:* an archive merge between two devices starts disagreeing with what the same two
devices would have got through the server, and neither answer is wrong on its own terms.

### 5. Deletes travel as tombstones, in both directions

A delete is a row that says it was deleted and when, not the absence of a row.

*If this breaks:* rows resurrect. A device that deletes a place and then takes a full snapshot from
anywhere — the server or an archive — gets it back, silently, and the caver deletes it again.

### 6. The synced strategy configuration rows travel with the data

The place-code and QCRI strategy configuration is data, and it moves with the rows it governs.

*If this breaks:* two devices generate codes under different rules, silently. Nothing errors; the
codes simply stop agreeing, and the divergence is only visible much later, on a label somebody
printed.

### 7. `device_uuid` and `current_user_uuid` never sync

They are per-installation facts about one phone. They are not identity and they do not leave it.

*If this breaks:* one device's local identity is planted on another, which breaks the user remap in
rule 8 and makes provenance meaningless.

### 8. The username-based user remap keeps working

User UUIDs are allocated per device and are merged by username. They are **not** cross-device
identities, and they are **never** mapped to SilexGIS accounts — the server treats them as opaque
provenance strings and nothing more.

*If this breaks:* the same caver appears as two people after a merge, or two cavers collapse into
one.

### 9. Assets remain immutable-per-name on import

An asset name identifies content that does not change. An import never overwrites one.

*If this breaks:* an archive import silently replaces a photo or a map with a different file of the
same name, and there is no version to go back to.

### 10. No network path outside the sync service touches domain data — and never a new value in the FTP protocol enum

The sync feature is the only thing that talks to a SilexGIS server, and it does not join the FTP
profile machinery to do it.

**Specifically: do not add a `silexgis` member to the FTP protocol enum.** An older build reading a
profile with a protocol it does not recognise falls back to plain FTP. It would then try to speak FTP
to an HTTPS server, with the caver's stored credential, on whatever host the profile names. A
SilexGIS profile is its own configuration key, with its refresh token in secure storage, following
the FTP-profile *pattern* without joining it.

*If this breaks:* an old build on a caver's second phone attempts an FTP connection to the club's web
server. The best case is that it fails confusingly.

### 11. Coordinates never go into the property document

In either direction. The property document is emitted verbatim to every reader with **no protection
filter at all** — that is what makes it useful for opaque labels like the place code, the QCRI, the
local index and the general-area identifier, and it is exactly why a position must never be in it.

**There is no server-side backstop for this, and an earlier revision of this document wrongly said
there was.** The closed key set is real, but only for the kinds that carry a property schema at all:
today `cave_place` and `surface_area`, whose schemas declare `additionalProperties: false`, so an
undeclared key on one of those is a `feature.properties_invalid` refusal. Every other kind this
channel carries has no schema — a `cave` row, a `caveEntrance` row, and a `generic` row of any
schema-less kind (`cave_area`, `pit`, `karst_area` and most of the rest) have their property document
stored exactly as sent, unread. A latitude put into one of those is accepted silently and then
emitted verbatim to every reader that can see the row.

So this rule is held by the client and by nothing else. Do not look for the refusal; do not do it.

*If this breaks:* location protection is defeated entirely, for every reader, in a way no permission
check can catch — a protected cave's position published in a field designed to be public.

### 12. The server revisions and the download cursor are local-only state

The per-row revision this server hands back, and the resume position a download returns, are facts
about **one device's conversation with one installation**. They are stored — a device must send the
revision back on its next upload, which is how a write is arbitrated at all — but they are stored in
**local-only** state: a table of their own, excluded from the change log, from device-to-device
sync, and from an FTP archive. They are never columns on a synced row, and they never leave the
device except back to the server that issued them.

*If this breaks:* an archive carries one device's resume position to a second device. That device
sends a position issued against data it never read, the server answers "nothing after this", and the
rows in between are never downloaded — silently, with no error anywhere and nothing to retry. The
same shape applies to a base revision travelling between devices: the second device then loses every
upload to a conflict it cannot explain.

---

## 2. What the server guarantees in return

The checklist above is one-sided unless this is written beside it. These are commitments this server
makes, and the client may build on them.

**The device's UUID becomes the server's id.** Create-by-id, idempotent on retry, no re-keying, no
mapping table. *(the other half of rule 2)*

**Uploads are one revertible unit.** Every upload batch is recorded with its provenance and can be
reverted as a whole, which soft-deletes exactly what it created and nothing else.

**Replay is safe.** A batch identifier sent again returns the answer the first send got and writes
nothing. A client that loses its response can always retry — which is what makes rule 3's suspended
logging safe to combine with an unreliable connection. *(the other half of rule 3)*

**Conflicts are answered, not swallowed.** A rejected row comes back with the current server row
attached, so the device can merge and resubmit without a second round-trip to discover what changed.
*(the other half of rule 4 — the server's arbitration is visible, so the two rules never have to
guess about each other)*

**The server never rewrites a synced code.** Place codes and QCRIs are data. No uniqueness is
enforced at any scope, matching what the client does, so legal client data never fails to upload for
a constraint the client does not have — and a code the client generated is the code that comes back.
*(the other half of rule 6)*

**The server never nulls a field it was not sent.** An upload is a partial write. A device may send
one changed field and leave the rest of the row alone, and the thirty-odd server-side fields the
device knows nothing about are never cleared by an upload that did not mention them.

The converse is a real limit and is stated in `01-protocol.md`: because an absent member and an
explicit `null` are the same bytes on the wire, **this generation cannot clear a field**. Sending
`"description": null` leaves the stored description alone. A device that must clear one sends a
space until a later generation gives the row shape a way to say it.

**Sync routes are bearer-only, always.** No sync route joins the anonymous allow-list, and no
capability token ever appears in a sync payload. The one anonymous route in this package is the QR
landing lookup, which carries no coordinate and no name. *(the other half of rule 10)*

**A cursor this server did not issue is refused, never quietly restarted.** A resume position is
readable only by the installation that made it and only against the revision of the set it was made
for; a stale or foreign one comes back `sync.cursor_stale` or `sync.cursor_invalid` rather than as a
silent full re-read that a device would mistake for an incremental page. That refusal is what makes
rule 12 checkable: a cursor that has travelled between devices announces itself instead of costing a
device rows it never learns it is missing. *(the other half of rule 12)*

**The property document is the one thing the server does not guard, and says so.** It is stored as
sent and emitted verbatim, with no protection filter and — for every kind on this channel except
`cave_place` and `surface_area` — no schema either. There is no counterpart to rule 11 here, and
that absence is the statement: a coordinate placed in it is published, and nothing on this side will
notice. *(the missing half of rule 11, named rather than left to be discovered)*

**What the server delivered, it cannot recall.** Marking a cave protected later, or withdrawing a
caller's rights, changes what the server emits next time. It does not reach copies already on a
device. There is no remote wipe and none is planned — which is why the rules above about what is
persisted, and the section below about what is emitted, matter as much as they do.

---

## 3. The write-right rule: server behaviour to expect, not an error to diagnose

This is the one piece of server behaviour most likely to be mistaken for a bug, so it is stated
plainly.

**A caller without exact-location rights on a row will find that row's coordinate fields silently not
applied, while their other field changes land normally.** The row comes back `updated`, not rejected.
The name change, the description change and the property changes are all written. The geometry, the
altitude and the position quality are dropped, and on an entrance so is the change to which
entrance is the cave's main one — because that is a position decision too. An entrance is judged
against its cave as well as against itself: if either is withheld from this caller, neither moves.

It is deliberate, and the reason is this: the server decides by **right**, not by provenance. It has
no reliable way to know whether a coordinate on an incoming row was typed by a caver standing at the
entrance or was handed to that same account, blurred, by some other part of this server and copied
back. Rather than guess, it applies the same rule to both — if the caller may not see the exact
position, the caller may not set it.

**A client must not treat this as an error, and must not retry it.** There is nothing to retry: the
same account will get the same answer forever, and the edit that was accepted was accepted. If the
device shows the caver a position that the next download contradicts, that is this rule working, not
a lost message.

**The one case that is refused rather than dropped** is creating a *new entrance* under a cave whose
position the account may not see exactly. That answers `sync.location_forbidden`. It is refused
because a cave's own map point is its main entrance's, so writing one would move the other — there is
no half-application available.

---

## 4. What the sync channel does not cover

Stated honestly rather than implied away, because assuming otherwise would lead a client author to
build on a guarantee that does not exist.

**Protected rows the caller may not see exactly are withheld from sync entirely** — not delivered
approximate. That holds on the download and on the conflict echo alike, so **a device never receives a
grid-snapped coordinate through the sync channel at all.** Absence is the answer; there is no blurred
stand-in to mistake for a real position.

**But that bounds the sync channel, not the server.** The same account, with the same token, still
reaches grid-snapped coordinates for the same caves through the ordinary cave list, the feature list
and the export endpoints. Those are unchanged and are not part of this contract. A caver can export a
file from the web interface and type a snapped coordinate into the device by hand, and nothing here
prevents it.

The write-right rule in §3 is what contains the consequence: a coordinate obtained that way, from an
account that may not see the exact one, does not get written back as truth. That is a containment,
not a closure, and this package says so rather than implying the withhold rule closes it.

---

## 5. A suggested seed for the client effort's own rules

Nothing here installs itself anywhere. If the app-side effort keeps a rules file its sessions re-read,
these are the lines worth copying into it, because they are the ones a fresh session cannot infer
from the SpeleoLoc codebase:

- The twelve invariants above, by name — especially 3 (suspended change logging), 10 (never a new
  FTP protocol enum value), 11 (no coordinates in the property document) and 12 (the revisions and
  the cursor are local-only).
- **The sync contract is versioned and the version is pinned by builds in the field.** Additive
  changes are announced through the capability list; anything that breaks a pinned client moves the
  version. Read the protocol document before assuming a field can be added or a meaning changed.
- **If a document does not answer a question, the document is the defect.** Record it against the
  contract rather than encoding an observation of today's server into the client. Do not re-derive a
  rule by watching what the server happens to do.
- **Do not wait for a server change to work around a client problem.** If something in the protocol is
  genuinely wrong, that is a contract change with a version bump, not a quiet server-side
  accommodation.
