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
| `<trace>` | a correlation identifier | The framework attaches a fresh one to every problem document. It is for reading a server log with, not for a client to act on |
| `<token>` | the token of a signed delivery address | Minted afresh every time an address is handed out. The address around it is kept, because its shape is what a client follows; the token itself is only ever sent back as it came |
| `<sha256>` | the hash of a generated picture | The pictures the suite uploads are generated, and their bytes are the imaging library's. Pinning the hash would rewrite these files on an upgrade of that library rather than on a change to this protocol. A `null` hash in an answer is the server saying it will not quote one, and stays `null` |
| `<my-label>` | the caller's account name | The suite invents a name for every account it creates |

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
| `16-errors/cursor-invalid` | A resume position this server never issued. The device is told to throw the position away rather than to try again, and the status says so before a single field is read |
| `16-errors/cursor-stale` | A resume position that was good until the selection moved. A different status and a different code from the one above, for what is nonetheless the same decision on the device: drop the position and read the set from the beginning |
| `16-errors/contract-unsupported` | A build pinned to a contract version this server does not speak. The batch is refused whole and the answer names the version that would have worked — note that there are no per-row results at all, so a client that reads `rows` without checking the status crashes here |
| `16-errors/client-update-required` | An account whose uploads wait for a reviewer, sending from a build that does not declare `clientFeatures: ["held"]`. `409`, refused whole like the version refusal and with no `rows` either: nothing is written, and the device tells its user to update rather than retrying |
| `16-errors/feature-disabled` | The people directory asked for while this installation has it switched off. `409`, not an empty list — a phone stops offering the choice instead of reading "nobody" |
| `16-errors/file-bytes-missing` | A document row naming bytes that were never staged. **Not a refusal of the exchange** and so **no `status.txt`**: the batch is a `200`, and the row inside it comes back `rejected` with the code. It is filed here because it is the refusal a phone meets when an upload of the bytes was lost; the device uploads them and sends the row again |
| `16-errors/chunk-out-of-order` | A piece that does not continue the file. `409`, with the offset to send from as the member `expectedOffset` |
| `17-documents-first-page` | The documents stream for a reader who may read the cave but not place everything the photo hangs on. The photo is announced with `reach: "locationFree"`, and its `sizeBytes` and `sha256` are `null`: the bytes this reader will be given are a copy without the position, not the stored file, so the stored file's figures would be wrong |
| `18-files-links` | The address route, asked for the whole file and one picture of it in one call. Each item is answered on its own; the addresses are short-lived and carry a token |
| `19-upload-document-and-link` | One upload carrying a photo, from bytes the device staged first, and the link hanging it on a cave. The document keeps the device's identifier; the link is given one by the server |
| `20-upload-held` | An upload from an account under review, declaring `held`. Both rows come back `held`, with no revision, each with the `reviewId` the reviews stream will answer about later. Nothing is written |
| `21-reviews-first-page` | The decision on a held edit, turned down, with the server's own row riding along as `current` for a caller who may place it — at the position the server holds, not the one the phone proposed |
| `22-reviews-current-withheld` | The same decision for a caller who may no longer place the row. The decision and its revision are told; `current` is **absent**, not blurred |
| `23-people` | The people directory: the caller's clubmates and the caller, names only, and the links the caller's phones have sent (none yet) |
| `24-trips-first-page` | A trip recording as another member of the club is handed it: two of its three scans. The third is on a place this member may not see exactly, and there is no word of it — a phone cannot tell a scan withheld from a scan never made |
| `30-files-config` | What this installation takes and how much room the account has left: the largest file, the room, the types it accepts and refuses, the piece size |
| `25-rasters-first-page` | A raster map as a member is handed it, with the one pin on a place it may see. The map's other pin is on a protected place and is absent; the picture itself is a document, reached through `17`'s stream and `18`'s route |
| `26-files-upload-open` | A phone opening a transfer for bytes it will name in an upload (`stageForSync: true`). `201`, with the transfer's `id`, how much has arrived, the piece size to send and when the transfer expires |
| `27-files-upload-piece` | One piece of the bytes, sent as the raw request body at `?offset=` — only the request line is recorded, since the body is the file. The answer says how far the file has got |
| `28-files-upload-complete` | The last piece in, completing: the bytes hashed and typed, waiting under the transfer's `id` as the staged upload a document row names, with `stagedAt` and `expiresAt`. No document is made |
| `29-files-upload-status` | Asking about a transfer after a dropped connection or before sending the row that needs it: `receivedBytes`, `stagedForSync`, `stagedAt`, `sha256` and `expiresAt` |

A refusal is recorded with a `status.txt` beside its body, holding the status line. A successful
exchange has no such file: its status is 200 by construction, and a file saying so on every case
would be noise hiding the three where the value is the whole point.

`manifest.json` describes every file here — a digest per file and one roll-up over all of them —
so that a copy of this directory living in another repository can tell whether it is current
without diffing it. It is generated from a walk of the directory as it stands, which is also the
only thing that notices a file nothing asserts about: the byte comparison looks only at files a
test names, so an exchange left behind by a case that was renamed or deleted is invisible to it and
visible to the manifest. `manifest.json` itself is the only file outside it — it cannot state its
own digest — and it names that exclusion. `CHANGELOG.md` is covered like everything else, because a
changelog entry can be added without any recording changing, and a copy holding the old changelog
would otherwise match on every digest and call itself current.

**Rewriting these files is therefore two passes, not one.** The walk cannot run while the
recordings are being written — the tests that rewrite them run in parallel with it, and each write
empties its file before refilling it, so a walk crossing that window hashes a half-written file or
misses one that does not exist yet. The manifest pass is separate, needs no database, and is
deliberately not a recording run:

```
SILEXGIS_CONTRACT_RECORD=1   dotnet test                                    # rewrite the exchanges
SILEXGIS_CONTRACT_MANIFEST=1 dotnet test --filter FullyQualifiedName~ContractManifestTests
```

The suite refuses the first pass's manifest rather than taking a wrong one, and says this. Anything
else in this directory that changes by hand — this file included — needs the second pass too, since
everything but the changelog and the manifest itself is hashed.

`CHANGELOG.md` is written for somebody whose build pins a contract version. Every entry says
whether that build still works.

Numbering starts at 07, and the gap is deliberate rather than a set of missing files. Steps 01–06
are reserved for the exchanges that come before a download — signing in (`01-login`,
`02-login-mfa-required`, `03-authorize-code`, `04-token-exchange`, `05-token-refresh`) and creating
the selection (`06-sync-set-create`). **None of them is recorded as a directory here yet.** The
sign-in traffic is transcribed inline, as prose with pasted bodies rather than as guarded
recordings, in `docs/speleoloc-sync/03-auth.md` §2; the sync-set write shapes are in the served
description. So a copy of this directory that contains no `01`–`06` is complete as of today, and
the reserved names are listed above so that a later recording lands in the slot it was meant for
rather than renumbering everything after it.
