# Recorded traffic for contract version 1

Each directory here is one exchange with a running server, taken from the test suite that also
asserts about it. `request.txt` is the request line; `response.json` is the body that came back.
A write also has a `request.json` — the body that went up. A read is fully described by its
request line and the first recordings here were all reads; a write is not, and what a device
sends is the half the other application has to compose rather than merely parse.

These are the specification. Prose describing a payload drifts from the payload silently, so the
documents in `docs/speleoloc-sync/` explain *why* the protocol is shaped as it is and these files
say *what* it actually sends. Where the two disagree, these files are right.

**They are guarded, not decorative.** A normal test run compares the live server's answer against
these bytes and fails on any difference. They change only by re-running the suite with
`SILEXGIS_CONTRACT_RECORD=1`, which rewrites them — so a diff here is a deliberate contract change
and is reviewed as one.

## What has been replaced, and why

A response contains three things that differ on every run. Left alone they would make the
comparison meaningless, so they are replaced before anything is written or compared.

| In the file | Was | Why it could not be recorded |
|---|---|---|
| `<cave>`, `<place>`, `<set>`, … | a UUID | Identifiers are minted by the server as each row is written. The application accepts none from the caller, so there is no way to pin them. The name is the one the test gave that row; an unnamed identifier appears as `<uuid-1>` in order of first appearance |
| `<timestamp>` | an ISO-8601 instant | Every time in a payload is stamped by the server from its own clock |
| `<cursor>` | an opaque resume token | It encodes a position that moves with the data. It is opaque by contract — store it, send it back, never parse it — so recording its bytes would publish an internal shape as though it were promised |
| `<marker>` | eight hex characters | The suite suffixes every name it creates so several runs can share one database |

Two consequences worth stating, because they are easy to misread:

- **Every timestamp is the same placeholder.** That is deliberate: numbering them would encode
  which stamps happen to be equal in a particular run, and two writes inside one clock tick would
  then rewrite these files for no reason anybody could act on. What the times have to *do* — order
  a page, resume a cursor, carry a deletion past a watermark — is asserted by the tests, not here.
  A `null` stays `null`, which is the distinction that does belong in a recording:
  `clientUpdatedAt` is null for every row this server's own interface wrote.
- **The bodies are re-serialised indented**, so they are a readable rendering of the payload rather
  than its literal bytes. Field order, field names, nesting and every value that is not in the
  table above are exactly as sent.

## The cases

| Directory | What it shows |
|---|---|
| `07-download-first-page` | A device's first read of a selection: the settings document with the revision it belongs to, a cave, and the place inside it with its containment edge |
| `08-download-cursor-restart` | The page after the first one, asked for with the cursor the first one returned. `hasMore` is true, so the device knows to come back |
| `09-download-tombstones` | A row that has gone. It arrives as an identifier and a moment, in `tombstones` rather than in `features`, and carries nothing else at all |
| `10-download-protected-withheld` | A caller who may read a cave but may not place one point inside it. The point is **absent** — there is no entry for it anywhere in the payload and nothing says one was kept back. It is not delivered blurred, and its absence is not reported as a deletion |
| `11-upload-create` | A device pushing up a place it surveyed underground, under the identifier the device itself minted. `baseRevision` is null, which is how a row says it is new, and the answer carries the revision to send back next time |
| `12-upload-retry` | The same batch identifier again, after an answer was lost on the way back. `replayed` is true, the import batch is the same one, and nothing was written a second time |
| `13-upload-conflict` | A row whose `baseRevision` is no longer the server's. It is refused, and the server's own version of it rides back in `conflicts` so the device can show a caver what to merge against without fetching it |
| `14-upload-conflict-withheld` | Two rows lose the same conflict, and one of them is a position this caller may not place. Both are named in `rows`; only the readable one appears in `conflicts`. The other is **absent**, exactly as it would be from a download — the conflict answer is a second place this server hands a device a coordinate, and it asks the same question in the same place |
| `15-upload-delete` | A device removing a row it holds. A removal is arbitrated exactly as an edit is: it carries the revision the device last saw, and a stale one loses the same way |

Numbering starts at 07, and the gap is deliberate rather than a set of missing files. Steps 01–06
are reserved for the exchanges that come before a download — signing in (`01-login`,
`02-login-mfa-required`, `03-authorize-code`, `04-token-exchange`, `05-token-refresh`) and creating
the selection (`06-sync-set-create`). **None of them is recorded as a directory here yet.** The
sign-in traffic is transcribed inline, as prose with pasted bodies rather than as guarded
recordings, in `docs/speleoloc-sync/03-auth.md` §2; the sync-set write shapes are in the served
description. So a copy of this directory that contains no `01`–`06` is complete as of today, and
the reserved names are listed above so that a later recording lands in the slot it was meant for
rather than renumbering everything after it.
