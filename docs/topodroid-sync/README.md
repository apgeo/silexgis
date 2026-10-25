# Sending surveys from TopoDroid to a SilexGIS installation

TopoDroid is a cave-surveying application for Android. SilexGIS is a web GIS a caving club installs
on its own server, with its own accounts, permissions and protection rules for sensitive cave
positions. This package is the contract between the two: how a survey tool signs in with a key the
caver made, sends one survey's files, follows what the server does with them, and finds surveys to
bring back to another phone. It is written for whoever maintains the survey tool's side, and assumes
no access to the SilexGIS code.

**Contract version in force: 1.** A tool sends it on every call. What moves it is in
the "Versions" section of `01-protocol.md`; what has changed is in `../../contract/topodroid-sync/v1/CHANGELOG.md`.

| Document | What it covers |
|---|---|
| `01-protocol.md` | the routes in order: capabilities, open, send each file, complete, follow, list, run again; who sees what |
| `02-access-keys.md` | signing in with a personal access key: what it is, what it opens, when it stops working |
| `03-errors.md` | every refusal code and every way a processing run can end |
| `04-dev-server.md` | a throwaway server to develop against, and reaching it from an emulator or a phone |

## What the server does with a survey

1. The tool opens an upload naming the cave (or a survey of it, or a cave to make, or nothing — then
   a cave is made from the survey's title), and every file it will send: the TopoDroid ZIP archive,
   and optionally the Therion `.th`/`.th2` files and other exports.
2. Each file is **archived the moment it arrives**, as a survey source or document of the cave.
3. When the tool says the upload is complete, the server runs a processing job over the archived
   files: it checks they are all there, reads the archive's manifest and survey record (date, team,
   declination, fixed points), and — on an installation with a survey compiler — compiles the
   Therion sources into a 3D model, a Survex `.3d`, plan and extended-elevation PDFs and whatever else
   was asked for. Progress, the current step and any error are reported on the upload, which the tool
   polls.
4. Every upload is a **timestamped version** of its survey. A cave may hold any number of surveys;
   none is the default.

An installation with no compiler keeps and reads the files and says so; nothing fails.

## The one exception to a standing project rule

SilexGIS is pre-alpha and deliberately waives backward compatibility of its schema and API. **This
contract is the one place that waiver does not reach**, as for the SpeleoLoc contract beside it: a
survey tool in the field is pinned to what it was built against. Any change to these documents or
the recordings is a change to a published contract.

## Recordings

`contract/topodroid-sync/v1/` holds the real traffic of one invented survey, recorded by the server's
test suite and compared byte for byte on every run: one folder per exchange with `request.txt` (the
request line), `request.json` (a body sent, when there is one), `response.json` and, when the status
is not a plain `200`, `status.txt`. Identifiers, times and download tokens are replaced by
placeholders (`<cave>`, `<timestamp>`, `<token>` …); everything else is byte for byte what the server
sends.

## Where these documents live

In the SilexGIS repository, under `docs/topodroid-sync/`, beside the recordings in
`contract/topodroid-sync/`. A change to the protocol changes both in the same commit.
