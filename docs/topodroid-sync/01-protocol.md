# The survey upload protocol

How a survey tool on a phone sends one survey to a SilexGIS installation, follows what the server
does with it, and finds surveys to bring back. Every exchange named here is recorded byte for byte
under `contract/topodroid-sync/v1/`; **where this text and a recording differ, the recording is
right** and this text is the bug.

All routes are under `/api/v1/survey-uploads`, take and give JSON unless stated, and need
`Authorization: Bearer <key>` (`02-access-keys.md`). Times are UTC, ISO 8601. Identifiers are
UUIDs. Every refusal is a Problem Details body with a stable `code` (`03-errors.md`).

---

## 1. Versions

**Contract version in force: 1.** Send it on every call:

```
X-SilexGis-Contract: 1
```

A server that does not serve the version named answers `426 survey_upload.contract_unsupported`
with the version it does serve in `contractVersion` (`11-errors/contract-unsupported`).
`GET …/capabilities` never refuses on the header — it is where a tool learns the version. A call
without the header is served as the current version (the SilexGIS web site uses the same routes).

### 1.1 What changes the version

- **Adding** a member to an answer, a value to a list (a source, an extension, an output, a stage), a
  route, or a refusal code for a new situation is **not** a version change. A tool must ignore
  members it does not know and treat an unknown enum value as "something else" rather than fail.
- **Removing or renaming** a member, changing what a member or code means, or refusing something
  the previous version accepted **is**, and is announced in `CHANGELOG.md` beside the recordings.

## 2. The whole exchange

```
GET  /survey-uploads/capabilities            what the server takes                        01-capabilities
POST /survey-uploads                         open: where it goes and every file coming     02-open
PUT  /survey-uploads/{id}/files/{name}       one per declared file, raw bytes              03-put-file
POST /survey-uploads/{id}/complete           every file is in; the run is queued           04-complete
GET  /survey-uploads/{id}                    poll every pollIntervalSeconds until done     05 / 06 / 08
```

Then, later: open the **next version** of the same survey by the id the server answered
(`07-open-next-version`), list what may be imported (`09-list`), run an upload again (`10-rerun`),
or give up an upload still receiving files (`DELETE /survey-uploads/{id}`, `204`).

## 3. Capabilities — `GET /survey-uploads/capabilities`

Read once per session, before showing the export dialog (`01-capabilities`).

| Member | Meaning |
|---|---|
| `contractVersion` | the version this server serves |
| `sources` | the survey tools accepted, by the names `source` uses (`topodroid`) |
| `maxFileBytes` | the largest single file, in bytes (250 MB by default) |
| `maxFilesPerUpload` | the most files one upload may declare |
| `acceptedExtensions` | every extension a file may have, lower-case with the dot |
| `compiler.available` | **false: the server keeps and reads the files but compiles nothing.** Say so before the caver starts: *"This server keeps the files but does not compile them."* |
| `compiler.version`, `compiler.outputs` | the compiler and what it can produce, when there is one |
| `caveCreation.allowed` | whether an upload that names no cave may make one |
| `caveCreation.defaultCaveTypeId`, `caveCreation.visibilities` | how such a cave is made |
| `outputs.catalogue`, `outputs.defaults` | every output a run can be asked for, and what it makes when asked for none |
| `pollIntervalSeconds` | how often to poll a run |

## 4. Opening an upload — `POST /survey-uploads`

The request (`02-open/request.json`) says **where the survey goes** and **every file that will be
sent**. Nothing is stored yet; the answer is `201` with the upload's status body (see "Completing and following the run").

**Where it goes — at most one of:**

| Member | Meaning |
|---|---|
| `caveSurvey: { id }` | an existing survey of a cave, by the id the server answered for it before. **Keep the `caveSurvey.id` of every answer per survey in the tool** and send it next time: the upload becomes the survey's next version (`07-open-next-version`). |
| `cave: { id }` | an existing cave (from `GET /api/v1/caves?names=…`). The survey is matched among the cave's surveys by `surveyName`, else a new survey of the cave is made. |
| `newCave: { name?, visibility?, cavingGroupId?, caveTypeId?, locationProtected? }` | make a cave. `visibility` ∈ `private`, `cavingGroup`, `authenticated`, `public`; when empty, the account's caving group, else private. `locationProtected` (default `false`, as on the web's form) protects the cave's position from the moment it exists, so the entrance the survey's fixed station places is protected from its first second. |
| *(none)* | the server makes a cave named after `surveyTitle` (else `surveyName`). If a cave **the account can see** already has that name, the new one is `<title> (2)`, `(3)`, … The answer's `caveCreated` is `true` (`02-open`). |

A new cave has no position until the survey's fixed point is read into an entrance; it is
created on the account's own rights, and the installation may switch automatic creation off
(`caveCreation.allowed`).

**The survey and the files:**

| Member | Meaning |
|---|---|
| `source` | `topodroid` |
| `client` | `{ name, version }` of the tool, recorded, never trusted |
| `surveyTitle` | the title the results are grouped under; TopoDroid's survey title |
| `surveyName` | the tool's own name for the survey — how a re-upload without `caveSurvey.id` is recognised (compared exactly, case included) |
| `description` | an optional note, ≤ 4000 characters |
| `files[]` | `{ name, size, role }` for every file. **Exactly one** `archive` (the TopoDroid ZIP); `therionSource` (`.th`), `therionSketch` (`.th2`) and `other` (an export: `.svx`, `.pdf`, `.kml`, …) as wanted. Names are unique ignoring case, have no path, and keep the extension the role needs. `size` is the exact byte count the body will have. |
| `pipeline` | optional: `compile` (true when empty), `outputs` (catalogue names; the defaults when empty), `mapScale` (500 when empty) |
| `documentsVisibility` | optional: who the documents this upload makes are open to; the cave's own audience when empty |

The account must be able to **write** to the cave (or create one); that is checked here, once.

## 5. Sending the files — `PUT /survey-uploads/{id}/files/{name}`

One request per declared file, the file's bytes as the body (`Content-Type:
application/octet-stream`), `{name}` URL-escaped. The answer is `200` (`03-put-file`):

```json
{ "name": "…", "status": "stored", "sha256": "…", "receivedSize": 2276, "sourceId": "…", "documentId": "…" }
```

- Each file is **archived the moment it arrives** — the survey files as survey sources of the cave,
  an `other` file as a document attached to it. Nothing waits for `complete` to make a file safe; a
  phone that loses its signal halfway has lost nothing it already sent.
- **A repeat is safe.** A `PUT` of a file already stored answers the first answer and stores
  nothing twice — retry freely after a lost answer. Compare `sha256` with your own to be sure.
- The body must be exactly the declared size (`size_mismatch`), and the bytes must be what the role
  says — an `archive` a ZIP, Therion files text (`content_mismatch`). A refused file may be sent
  again correctly.
- Files may be sent in any order.
- An upload not completed by its `expiresAt` (24 hours by default) is closed; files sent until then
  stay archived (`expired`, `410`).

## 6. Completing and following the run

`POST /survey-uploads/{id}/complete` answers `202` with the status body, `status: "queued"`
(`04-complete`); repeated, it answers where the upload stands. Then poll
`GET /survey-uploads/{id}` every `pollIntervalSeconds` until `status` is `succeeded`, `failed` or
`abandoned`.

| Member | Meaning |
|---|---|
| `status` | `receiving` → `queued` → `running` → `succeeded` \| `failed`; or `abandoned` |
| `stage` | the step the run is in, or reached last: `archive`, `unpack`, `scaffold`, `compile`, `store`, `read`; `null` before a run |
| `progress` | 0–100, for a progress bar; each stage owns a band of it, so it moves forward stage by stage |
| `message` | what the run is doing now, or — on a run that compiled nothing — why (`06-status-succeeded`). **English free text, for a log line or a fallback**: it is not translated and its wording may change. A screen in another language shows its own label for `stage` (and for `error.code` when failed) rather than this |
| `error` | `{ code, message }` when `failed` (`08-status-failed`); codes in `03-errors.md` |
| `cave` | `{ id, name, url }` — `url` is the cave's web page, relative to the installation |
| `caveSurvey` | `{ id, title }` — **keep this id** for the next version |
| `version` | this upload's version of its survey: the UTC moment it was opened, to the second. Result files are named `<survey>-<yyyyMMddTHHmmssZ>.<ext>` from it. |
| `caveCreated` | whether this upload made its cave |
| `survey` | `{ title, name, date, team }` — `date` and `team` once the archive has been read |
| `files[]` | every declared file: `status` (`declared`, `received`, `stored`, `refused`), sizes, `sha256`, `refusalCode`, and `contentUrl` once stored; after a compile also the two files the run generated around the survey (`role: generatedConfig` — the Therion configuration `<survey>-<stamp>.thconfig` and the wrapper survey `<survey>-<stamp>.wrapper.th`), which are kept with the inputs |
| `artefacts[]` | what the run produced: `{ kind, name, sizeBytes, contentUrl, surveyModelId?, documentId?, sourceId?, viewUrl?, modelStatus?, error? }` — `kind` is the output's catalogue name (`lox`, `3d`, `plan-pdf`, …) or `log`; `name` carries the version stamp; `modelStatus` (`pending`, `processing`, `ready`, `failed`) is how far a model's reading has got; `error` (`{ code, message }`, else `null`) says what is wrong with that one output — **an entry whose `error.code` is `survey_pipeline.output_missing` was asked for and not produced, and has no file (`sizeBytes` and `contentUrl` are `null`)**; `survey_pipeline.crs_unknown` on a `lox` entry means it was stored but not read (see below). Empty on a server that does not compile (`06b-status-compiled` records a compiled run) |
| `compiler` | `{ version }` once a compiler ran |
| `outputs`, `compile` | what the run was asked for, after the server combined the request with the account's and the installation's standing choices |
| `logTail` | the compiler's last lines; only for an account that may write to the cave |
| `statusPageUrl` | the signed-in web page the run is watched on, relative to the installation — offer *Open in browser* |
| `publicLink` | `{ url, createdAt }` — the public page of the results (`url` is `/p/survey/<token>`, relative to the installation), shown to an account that may write to the cave; `null` when there is none, or for an account that may only read. Offer *Copy* and *Open* (see "The public link" below) |
| `publicLinkRefusal` | why there is no public link, as a code: `installation-off`, `account-off`, `no-share-right`, `protected-cave`, `revoked` (`03-errors.md`); `null` while there is a link, or before the upload is complete |
| `createdAt`, `expiresAt`, `startedAt`, `finishedAt` | the upload's times |

**`contentUrl` is a signed, short-lived download address** (`/api/v1/files/{id}/content?token=…`):
fetch it promptly, with or without the key, or poll again for a fresh one. **`url`, `viewUrl` and
`statusPageUrl` are web pages**, relative to the installation's address — prefix the server URL the
caver typed.

On a server with no compiler a run checks the archive, reads it, and ends `succeeded` at `unpack`
with progress 100 and a `message` saying it was not compiled (`06-status-succeeded`). That is the
expected end there, not a failure.

On a server that compiles, a run goes on through `scaffold`, `compile` and `store` and ends
`succeeded` at `read`. What it stored, by `kind`:

| `kind` | Stored as | Name |
|---|---|---|
| `lox` | a survey model of the cave (`surveyModelId`), read into stations and legs when the survey is fixed to a position — `modelStatus` goes `pending` → `ready`; a survey fixed to no position is stored unread (`failed`, and the `message` says why); a survey fixed in a coordinate system the server has no EPSG code for is stored unread too, with `error.code` `survey_pipeline.crs_unknown` on the entry | `<survey>-<stamp>.lox` |
| `3d` | a survey model, for download and viewers; not read | `<survey>-<stamp>.3d` |
| `plan-pdf`, `extended-pdf`, `plan-svg`, … | a document attached to the cave (`documentId`), tagged `survey-output` | `<survey>-<stamp>.plan.pdf`, `.extended.pdf`, `.plan.svg`, … |
| `log` | the compiler's log, archived as a survey source of the cave (`sourceId`) with its loop closures read from it | `<survey>-<stamp>.log` |

An output that was asked for and not produced — a KML of a survey with no position is the ordinary
case — is listed with its name and `error.code: survey_pipeline.output_missing`, and the run still
succeeds on the rest; the run fails with that code only when none of what was asked for came out.

A run whose compile failed (`compile_failed`, `compile_timeout`, or `output_missing` for the whole
run) still keeps what it compiled — the two `generatedConfig` files in `files[]` — and the
compiler's whole log, listed in `artefacts[]` as `kind: log`, so the caver can read the error and
carry on in Therion.

A model somebody deleted on the web drops off `artefacts[]` (and off the public page) at the next
read.

The run reports `succeeded` without waiting for the model's reading; poll on while `modelStatus` is
`pending` or `processing` if the tool shows it. **None of it becomes the cave's current model** —
which model draws the cave is chosen on the web. Every upload is a version: a second upload of the
same survey stores its own files under its own stamp beside the first's, and running an upload again
(`rerun`) replaces that upload's own outputs as new revisions of the same files. An installation may
keep the outputs of the newest versions only (`SILEXGIS__SurveyUploads__KeepArtefactVersions`); an
older version's `artefacts[]` is then empty, and *Rerun* compiles it again from its kept inputs.

A cave the upload made is placed at the survey's fixed station (the archive's `fixeds` row), as its
main entrance, whether or not the survey is compiled. A cave that was named in the request is never
moved.

## 7. Running again — `POST /survey-uploads/{id}/rerun`

Runs a finished upload again on the files it archived, with the options it ran with or with a new
`pipeline` body (`10-rerun`); answers `202` with the status body. Refused while a run is queued or
running (`run_in_progress`) and for an upload that never completed (`not_completed`). Needs write
access to the cave.

## 8. Listing — `GET /survey-uploads`

For the tool's *import from SilexGIS* and for the web (`09-list`). Query: `caveId`, `caveSurveyId`,
`source`, `mine` (only the account's own), `page` (from 1), `pageSize` (≤ 100, default 50). Answer:
`{ items, total, page, pageSize }`, newest first. Each item carries the cave's name, the survey and
version, who sent it, its status, and — when they are still there — `archive` and `therionSources`
with `contentUrl`s: download the archive into TopoDroid's `zip/` folder to import it, or a `.th` to
import that.

`source` in a list item is the tool's name as the capabilities' `sources` give it (`topodroid`).

## 9. Who sees what

Every route answers on the **cave's survey terms**: the account must be able to read the cave
**and see its exact position**, because a survey archive carries fixed stations and a run's messages
can quote them. An upload of a cave that fails either half does not exist for that account — it is
answered `404` like an upload that never existed, and it is absent from the list and from its
`total`. This holds for the uploader too, if their access to the cave's position is later taken
away. Changing an upload — sending a file, completing, abandoning, running again — needs write
access to the cave as well.

## 10. The public link

When the installation makes public links (on by default) and the uploader's SilexGIS settings have
not turned them off, completing an upload makes **a public page of its results** — if the account
completing it may share the cave, and the cave's position is not protected. The status answer
carries it as `publicLink.url` from the `complete` answer on (`04-complete`); running the upload
again keeps the same link. Without one, `publicLinkRefusal` says why. On the web, the upload's page
can withdraw the link or make a new one in its place; the old address then stops working.

The page is `GET /api/v1/public/survey-uploads/{token}` — the token is the last part of
`publicLink.url` — read **without any account or key** (`12-public-link`). Its answer is its own,
smaller shape:

| Member | Meaning |
|---|---|
| `survey` | `{ title }` |
| `cave` | `{ name }` — the cave's name and nothing else |
| `version` | the upload's version, as in the status answer |
| `status`, `stage`, `progress`, `message` | as in the status answer |
| `stages[]` | `{ stage, state }` for all six stages, `state` ∈ `pending`, `active`, `done`, `failed`, `skipped` |
| `error` | `{ code, message }` when failed |
| `artefacts[]` | `{ kind, name, sizeBytes, contentUrl }` — the outputs stored; never the log, never an output that was not produced (`12b-public-link-compiled`) |
| `createdAt`, `finishedAt` | the upload's times |

It never carries the archive, the Therion sources, the log, the party, a station name, the cave's
id or any coordinate. **Every refusal is the same `404 survey_upload.public_link_not_found`**, an
invented token's: a withdrawn link, a cave protected since, an installation that turned links off.
`contentUrl`s are short-lived; read the page again for fresh ones. Public pages are rate-limited per
address (`429` with `Retry-After`).
