# Syncing a SpeleoLoc device with a SilexGIS installation

SpeleoLoc is a cave-navigation application that runs on a caver's phone and keeps its own database of
caves, cave places and the codes printed on their labels. SilexGIS is a web GIS a caving club
installs on its own server, holding the same kinds of records for the whole club, with its own
permissions and its own protection rules for sensitive entrance positions. This package is the
contract between the two: how a device signs in, names the caves it wants to carry, reads them a page
at a time, writes its own edits back, and resolves a code somebody scanned off a cave label. It is
written for whoever builds the device side — there is no client here, only the server and the
description of what it will do. **The server side exists and is finished for this phase; the client
side does not exist yet, and building it is a separate piece of work.**

**Contract version in force: 1.** A device pins this number and sends it on every upload. What moves
it, and what does not, is in `01-protocol.md`; what has changed between versions is in
`../../contract/speleoloc-sync/v1/CHANGELOG.md`.

---

## Both channels work, and neither replaces the other

Stated once, here, so it does not get re-argued in eight documents:

**A SilexGIS server and the existing FTP archive channel both work, independently.** The server is
the club's source of truth for the data it holds. FTP archives remain the offline, device-to-device
hand-off, and nothing here changes how they behave. Neither impersonates the other. A device may use
either, both, or none — and a device using none is a device behaving exactly as it does today, which
is a requirement rather than a fallback (`06-standalone-rules.md`).

## The one exception to a standing project rule

SilexGIS is pre-alpha and deliberately waives backward compatibility of its schema, its model, its
API and its stored data: a wrong shape is rewritten rather than preserved.

**This contract is the one place that waiver does not reach.** A phone in the field pins what it was
built against, cannot be redeployed on the server's schedule, and has no way to discover a change
except by being told. So this surface is versioned, the version is answered before anything else is
asked, and an upload naming a version the server cannot serve is refused rather than interpreted
generously. Treat a change to anything in this package as a change to a published contract.

## Getting a server to work against

The full instructions are in `07-dev-server.md`. The short of it: one command from a SilexGIS
checkout starts a dedicated PostGIS container, migrates it, seeds both the demo data and the
sync-specific development data, serves the API on a loopback port, and then prints the base URL, both
account credentials, the caving group, the client id and a paste-ready sequence of `curl` calls for
the whole sign-in dance.

It is a **per-session** server, started and thrown away, not a shared instance somebody maintains. It
stands up its own database on its own port with its own volume, so it never touches another
developer's.

**Do not skip the sync-specific seeding.** A stock demo installation cannot demonstrate a single rule
this work exists for: every demo object belongs to the bootstrap administrator, who sees everything
exactly, so location protection never engages and there is no caving group at all. The extra seeding
adds a group, a second non-administrator account that owns nothing, and the missing group binding —
which is what makes withholding and group targeting observable from a client at all.

## Where these documents live

They are maintained in the SilexGIS repository, under `docs/speleoloc-sync/`, alongside the recorded
traffic in `contract/speleoloc-sync/v1/`. **That is the copy that is current**, and the manifest
beside the recorded traffic is how any other copy tells whether it is stale.

A copy has been placed in the application's repository, under `docs/integrations/silexgis/` and
`test_data/silexgis_contract/v1/`. **It is in a working tree and is not committed**, so a fresh clone
of that repository does not get it: until somebody on the application side commits it, read the
server repository. Whoever commits it should also decide how it is refreshed, because a copy nobody
refreshes is worse than no copy — the manifest is what makes that decidable rather than a guess.

## The eight other documents

| Document | The question it answers |
|---|---|
| `01-protocol.md` | The wire. Capabilities and versioning, sync sets, the paged download and its cursor, tombstones, the feature row, the settings document, and writing rows back with per-row arbitration. **Start here.** |
| `02-field-mapping.md` | Which SpeleoLoc record becomes which SilexGIS record, which fields carry across, and what may never be sent. |
| `03-auth.md` | Signing in, with real recorded exchanges: the cookie, the authorization request, the token exchange, two-factor, refresh lifetimes, and the five things a client author gets wrong. |
| `04-errors.md` | Every failure this surface can answer with, what each means, and — the point of the document — the one action from a closed set that the client is supposed to take. |
| `05-qr-landing.md` | What happens when somebody scans a code printed on a cave label, what an anonymous visitor is told, and the three conditions on the printed URL that only the client can enforce. |
| `06-standalone-rules.md` | The twelve invariants that keep the application working with no server at all, each with the failure it prevents — and what the server guarantees in return. |
| `07-dev-server.md` | How to get a running server with usable data, in one command, and how to reset it. |
| `08-near-future.md` | What was deliberately written down rather than built, and why. |

## What the client effort decides for itself

None of these is a question for the server side, and none of them is waiting on an answer from here.

1. **The whole client architecture** — service shape, state management, wiring, error surfaces, retry
   and backoff policy, and how a sync run is presented to the caver. The only constraints are the
   twelve rules in `06-standalone-rules.md`, and they are constraints on effects, not on design.
2. **When a sync runs** — manual, on connectivity, on a schedule, on app foreground. The server has no
   opinion and no session state to lose.
3. **How a conflict is presented.** The server hands back the current row; whether the application
   applies it silently, shows a difference, or queues it for a human is entirely the application's
   call.
4. **How the sync set is chosen on the device** — the picker, the defaults, whether a caver can change
   it later.
5. **Local storage of the refresh token and the server profile.** The existing FTP-profile pattern is
   the precedent; the shape is theirs. See rule 10 for the one thing it must not do.
6. **The local schema change.** It is permitted and additive. The shape is theirs; the one
   server-visible requirement on it is rule 12 in `06-standalone-rules.md` — per-row server revisions
   and the download cursor live in local-only state, excluded from sync and from archives — which is
   stated as an invariant there rather than here.
7. **How the application displays a code and a QR square**, and whether a label switches from the
   deep-link prefix to a URL prefix. The scanner already handles both; the three URL conditions in
   `05-qr-landing.md` are the only hard constraints.
8. **Testing strategy beyond replaying the recorded traffic.** The recordings pin the wire; everything
   above the wire is theirs.
9. **Their own questions for the owner.** Anything about the application's product behaviour goes to
   the owner through that effort, not through this one.

And two things that are the mirror image of the above:

- **Do not wait for a server change to work around a client problem.** If something in the protocol is
  genuinely wrong, that is a contract change with a version bump — not a quiet server-side
  accommodation.
- **Do not re-derive a rule from the server's behaviour.** If a document does not answer a question,
  the document is the defect: record it against the contract rather than encoding an observation of
  today's server into the client.
