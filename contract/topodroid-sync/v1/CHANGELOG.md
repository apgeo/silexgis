# Changes to the survey upload contract

Written for one reader: somebody whose survey tool pins a contract version and needs to know whether
it still works. Every entry answers that in its first line; the detail comes after.

## How a change reaches you

A tool sends the version it was written against on every call (`X-SilexGis-Contract`) and reads the
server's from `GET /api/v1/survey-uploads/capabilities`.

**A version bump breaks pinned builds, and is the only thing that does.** The number goes up only
when a tool written against the previous number would get something wrong: a member that changed
meaning or stopped being sent, a code that changed meaning, something accepted before that is now
refused. A server that no longer serves a tool's version answers every call but `capabilities` with
`426` and `survey_upload.contract_unsupported`, naming the version it does serve. Nothing is
half-applied, and what the tool already sent stays archived.

**Everything else is additive and reaches you silently.** A new member in an answer, a new value in
a list (a source, an extension, an output, a stage), a new route, a new code for a new situation —
a tool that ignores what it does not know carries on unaffected. A tool that rejects unknown members
turns every future addition into an outage of its own making.

## What every entry states

- the date, in UTC;
- whether the contract version changed, and to what;
- **whether a tool pinned to the previous version keeps working** — the line that matters;
- which recordings under this directory were written or rewritten;
- any `survey_upload.*`, `survey_pipeline.*` or `auth.*` code that appeared, changed or stopped being
  sent (the register is `docs/topodroid-sync/03-errors.md`).

---

## 2026-10-25 — contract version 1

**Contract version: 1. Nothing is pinned to an earlier version, because there is none.** This is the
first published shape of the exchange.

The surface as of this entry:

- **Signing in** with a personal access key (`sgx_…`) made on the web, purpose *survey upload*;
  `401 auth.unauthenticated` for a key that is not good, `403 auth.access_key_not_accepted` for a
  route a key does not open.
- **Capabilities** — `GET /api/v1/survey-uploads/capabilities`: contract version, accepted sources
  and extensions, per-file and per-upload limits, whether the server compiles and what it can
  produce, whether it makes caves from uploads, the output catalogue and its defaults, the poll
  interval.
- **One upload** — open (`POST /api/v1/survey-uploads`, naming a cave, a survey, a cave to make, or
  nothing), one `PUT …/files/{name}` per declared file (repeatable), `POST …/complete`, then poll
  `GET …/{id}`; `DELETE …/{id}` gives up an upload still receiving; `POST …/rerun` runs a finished
  one again.
- **Listing** — `GET /api/v1/survey-uploads`, newest first, only uploads of caves whose survey the
  caller may see with the exact position.
- **The processing run** on this version checks the archived files and reads the TopoDroid archive;
  compiling is reported by `compiler.available` and is not part of what this entry's server does —
  its runs end `succeeded` with a note. The compile stages' codes are reserved (listed in the error
  register) so a tool can be written once.

Recorded: every folder under this directory, `01-capabilities` to `11-errors/*`.

A list item's `source` is the tool's name exactly as the capabilities' `sources` and the open
request write it (`topodroid`).
