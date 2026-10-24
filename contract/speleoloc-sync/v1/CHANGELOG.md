# Changes to the mobile sync contract

This file is written for one reader: somebody whose build pins a contract version and needs to
know whether it still works. Every entry therefore answers that question in its first line, and
the detail comes after.

## How a change reaches you

A device declares the version it was written against — it sends `contractVersion` on every upload,
and reads the server's own from `GET /api/v1/sync/capabilities`. Two different kinds of change can
happen to this protocol, and they reach a pinned build in two very different ways.

**A version bump breaks pinned builds, and is the only thing that does.** The number goes up only
when a client written against the previous number would get something wrong: a field that changed
meaning, a field that stopped being sent, a rule that started being enforced. When it goes up,
uploads from a build pinned to the old number are refused whole, with `409` and
`sync.contract_unsupported`, and the answer names the version the server does speak. Nothing is
half-applied. The device keeps its rows and its user sees a message telling them to update.

**Everything else is additive and reaches you silently, which is the point.** A new optional field,
a new row kind, a new named capability — a build that has never heard of it carries on unaffected,
because a client of this protocol ignores fields it does not recognise. That rule is what makes an
addition safe, and a client that rejects unknown fields converts every future addition into an
outage of its own making. Additions that a client can *use* are announced in the `features` list
from `capabilities`, so a device can ask what this installation supports rather than guessing from
its version number.

So: read the version to know whether you still work, and read `features` to know what you can do.

## What every entry states

- the date, in UTC;
- whether the contract version changed, and to what;
- **whether a build pinned to the previous version keeps working** — the line that matters;
- which recorded exchanges under this directory were written or re-written, because a difference
  in those files is the change, and everything else is description of it;
- any `sync.*` or `qr.*` code that appeared, changed status, or stopped being sent. A code is
  visible to a client even when the version does not move, and the register of them is
  `docs/speleoloc-sync/04-errors.md`.

---

## 2026-08-29 — contract version 1

**Contract version: 1. Nothing is pinned to an earlier version, because there is no earlier
version.** This is the first published shape of the protocol, assembled and cross-checked as a
whole. No installation has served an earlier one and no build can be pinned to one.

The surface as of this entry:

- **Reading.** `GET /api/v1/sync/sets/{id}/download`, a page at a time, resumed with an opaque
  cursor. Deletions arrive as tombstones. A position the caller may not see exactly is absent from
  the payload entirely rather than delivered approximate, and its absence is not reported as a
  deletion.
- **Writing.** `POST /api/v1/sync/sets/{id}/upload`. The device's own identifier becomes the
  server's; a batch identifier replayed returns the first answer and writes nothing; a row whose
  `baseRevision` is stale is refused with the server's current row alongside it, so a device can
  merge without a second round-trip.
- **Capabilities.** `GET /api/v1/sync/capabilities` answers `contractVersion`, `pageSizeMax`,
  `uploadRowsMax` and `features`, which is `["download", "upload"]`. The two limits are what this
  installation is configured to allow and differ between installations; the version and the feature
  list are statements about the server's code.
- **Landing.** `GET /api/v1/public/qr/{code}` resolves a code printed on a cave label without a
  token, and says only which installation it resolves at.

Recorded exchanges written in this entry: `16-errors/cursor-invalid`, `16-errors/cursor-stale`,
`16-errors/contract-unsupported` — the first recordings of a refusal rather than an answer. They
exist because a refusal is as much of the contract as an answer is, and because the shape of a
refused batch is not the shape of a batch whose rows were individually refused: there are no
per-row results in it at all, so a client that reads `rows` without checking the status crashes on
the one exchange it is most likely to meet in the field.

A refusal is recorded with a `status.txt` beside its body, holding the status line. A successful
exchange has no such file; its status is 200 by construction.

Codes: none changed. The complete register, with the status, the condition and what a client
should do about each, is in `docs/speleoloc-sync/04-errors.md`.

`manifest.json` was added in this entry. It is how a copy of this directory in another repository
tells whether it is current: a digest per file, and one roll-up over all of them to compare first.
It covers every file here except itself — **this changelog included**, because an entry can change
without any recording changing, and that is precisely the case a copy would otherwise report itself
current through. Taking it is a second pass over the directory rather than part of the recording
run, so an entry is written first and hashed with everything else; `README.md` gives the two
commands.

## 2026-10-16 — the 401 and the 429 carry a code

**Contract version: 1, unchanged. A build pinned to it keeps working.** Two refusals that arrived
as problem documents with no `code` now carry one, and nothing else about them moved: a client that
branched on the status first, as `docs/speleoloc-sync/04-errors.md` told it to, behaves exactly as
before.

- `401` now carries `auth.unauthenticated`. Same status, same `title`, same `type`; the action is
  still `re-auth`.
- `429`, from the rate limiter in front of the QR landing route, now carries `rate.limited`. Same
  status and `title`; the action is still `retry` after a wait.

Recorded exchanges written in this entry: none — neither refusal is recorded here.

Codes: `auth.unauthenticated` and `rate.limited` appeared. They are written by the server itself
when an answer would otherwise leave without a code, so every refusal of this API now carries one.

## 2026-10-23 — a cave row names its type

**Contract version: 1, unchanged. A build pinned to it keeps working.** A row of kind `cave` now
carries `featureTypeCode: "cave"` where it carried `null`, and a `propertiesSchemaVersion` once its
property document has been checked against that type's schema. Both are fields a client already
receives on every other row; a client that reads a row by its `kind` first, as this protocol has
always asked, sees no difference.

Why: every cave now carries one feature type, bound to the cave kind, so that a club can give caves
fields of its own that are validated and filterable like any other kind's. The schema of that type
declares the keys a device writes on a cave (`speleolocCaveLocalIndex`, `speleolocSchemaVersion`)
with the types the cave-place schema already gives them, and stays open to keys it does not list.

One thing a client must not do: send `featureTypeCode: "cave"` on a row of kind `generic`. That row
is refused with `sync.type_unknown`, the same as any type this installation does not have for
generic rows; caves arrive as caves and are given their type on creation.

Recorded exchanges re-written in this entry: `07-download-first-page`,
`10-download-protected-withheld` — the cave row in each now shows the two fields above.

Codes: none changed.

## 2026-10-24 — a locked account can no longer refresh, and a server restart no longer signs devices out

**Contract version: 1, unchanged. A build pinned to it keeps working.** No sync route, field or
`sync.*` / `qr.*` code changed. Two changes on the sign-in side reach a device; both are described in
`docs/speleoloc-sync/03-auth.md` §5 and §6.

- **A locked account's refresh is refused.** `POST /connect/token` answers `400 invalid_grant` with
  the description "The account is locked." while the lock lasts. Before, a lock stopped new sign-ins
  only and an existing session kept renewing itself. A lock revokes nothing: the same refresh token
  works again once it lifts, so a client should keep its tokens and retry later rather than asking
  for a password. A client that treats every `invalid_grant` as "sign in again" still works; it just
  asks for a password it did not need.
- **Restarting the server, or recreating its container, no longer ends sessions.** The token signing
  and encryption keys now persist in the installation's keys directory and roll over without
  invalidating what they issued. No client change is needed; a device simply stops being signed out
  by server maintenance.

Recorded exchanges written in this entry: none — neither change is a sync exchange.

Codes: no API problem code appeared or changed. The new refusal is an OAuth `invalid_grant`, told
apart from the others only by its `error_description`.

## 2026-10-24 — photos and documents are listed for a phone, and fetched by name

**Contract version: 1, unchanged. A build pinned to it keeps working.** Everything below is new and
additive: two new routes, one new value in `features`, and no change to any existing route, field or
recorded exchange. A build that has never heard of them carries on unaffected.

- **`features` gains `documents`.** `GET /api/v1/sync/capabilities` answers
  `["download", "upload", "documents"]`.
- **`GET /api/v1/sync/sets/{id}/documents`** — a catalogue of the photos and documents hung on the
  set's features, one row per link with its document embedded, paged and resumed exactly as the
  feature download is, with a cursor of its own. Rows never carry a position. Each row's `reach` says
  whether fetching the file itself hands over the stored file, the whole picture with its location data
  removed, pictures of it only, or nothing.
- **`POST /api/v1/sync/files/links`** — signed addresses for the files a phone chose to fetch,
  `"original"` or a picture size, each item decided again against the account's access now.

Described in `docs/speleoloc-sync/01-protocol.md` §11 and `02-field-mapping.md` §7.

Recorded exchanges written or re-written in this entry: none (the new routes' recordings come with
the cases numbered from 17). `07`–`16` are byte-for-byte unchanged.

Codes: new on the address route, as per-item refusals in a `200` — `sync.document_unavailable`,
`sync.original_withheld`, `sync.rendition_unavailable`; and `validation.failed` for a malformed
request. The catalogue answers only the reading codes it shares with the feature download
(`sync.set_not_found`, `sync.cursor_invalid`, `sync.cursor_stale`). See `04-errors.md` §3 and §3.1.
