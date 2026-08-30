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
