# Errors

Every refusal is an RFC 9457 Problem Details body (`application/problem+json`) carrying a stable
**`code`**. Branch on the HTTP status first and the `code` second; never on `title` or `detail`,
which are English sentences for a person and may be reworded. A code is part of the contract: a code
that changes meaning or stops being sent is a contract change (`../../contract/topodroid-sync/v1/CHANGELOG.md`).

Some refusals carry a member that names what was wrong — `file`, `files`, `declaredSize`,
`outputs`, `contractVersion` — listed below. A `traceId` member identifies the request in the
server's log; quote it in a bug report, never act on it.

Codes marked *(row)* never come back as an HTTP error. They describe how a **processing run**
ended and sit in the `error` member of the upload's status (`GET /api/v1/survey-uploads/{id}`),
which itself answers `200`.

The folder after each code is its recording under `contract/topodroid-sync/v1/`.

---

## Signing in and the contract

| Code | Status | When | What the tool should do |
|---|---|---|---|
| `auth.unauthenticated` | 401 | no key, an unknown, revoked or expired key, or the account may not sign in (locked, deleted, unconfirmed). `11-errors/unauthenticated` | open the settings: *"The access key was refused — paste a new one."* |
| `auth.access_key_not_accepted` | 403 | a key sent to a route that does not accept its purpose. `11-errors/access-key-not-accepted` | a defect in the tool: it called a route not meant for it |
| `survey_upload.contract_unsupported` | 426 | `X-SilexGis-Contract` names a version this server does not serve; member `contractVersion` is the one it does. `11-errors/contract-unsupported` | tell the user to update the tool or the server |

## Opening an upload — `POST /api/v1/survey-uploads`

| Code | Status | When |
|---|---|---|
| `survey_upload.source_unsupported` | 400 | `source` is not one of the capabilities' `sources`. `11-errors/source-unsupported` |
| `survey_upload.cave_ambiguous` | 400 | more than one of `cave`, `caveSurvey`, `newCave`. `11-errors/cave-ambiguous` |
| `survey_upload.cave_required` | 400 | none of the three, and the installation makes no caves from uploads (capabilities `caveCreation.allowed` is false) |
| `survey_upload.cave_creation_disabled` | 403 | `newCave` named, and the installation makes no caves from uploads |
| `cave.not_found` | 404 | `cave.id` names a cave the account may not see, or none — the two are answered alike on purpose. `11-errors/cave-not-found` |
| `survey_upload.survey_not_found` | 404 | `caveSurvey.id` names a survey the account may not see, or none. `11-errors/survey-not-found` |
| `survey_upload.cave_type_unknown` | 400 | `newCave.caveTypeId` is not a cave type of this installation. `11-errors/cave-type-unknown` |
| `survey_upload.visibility_invalid` | 400 | `newCave.visibility` is `cavingGroup` but the account is in no caving group (`11-errors/visibility-invalid`); or `documentsVisibility` is `cavingGroup` and the upload has a file without a survey format, but the cave has no caving group the account may file documents under |
| `survey_upload.caving_group_required` | 400 | `newCave.visibility` is `cavingGroup`, the account is in several groups and names none (`cavingGroupId`) |
| `access.create_forbidden` | 403 | the account may not create a cave there; or the upload declares a file without a survey format (a drawing, a track, a table — it becomes a document of the cave) and the account may not create documents |
| `access.caving_group_binding_forbidden` | 403 | the account may not bind a cave to the named group |
| `acl.forbidden` | 403 | the account may see the cave but not write to it |
| `survey_upload.files_invalid` | 400 | not exactly one `archive`; a name that is empty, repeated (ignoring case), has a path or a character no file system takes; an extension outside `acceptedExtensions` or wrong for the role; a size over `maxFileBytes`; more than `maxFilesPerUpload` files. Member `file` names the file when one is to blame. `11-errors/files-invalid` |
| `survey_upload.output_unknown` | 400 | `pipeline.outputs` names something outside the capabilities' `outputs.catalogue`; member `outputs` lists them. `11-errors/output-unknown` |
| `survey_upload.busy` | 409 | two uploads of one new survey were opened at the same instant; send it again |

A body that does not parse, or a member of the wrong type, is answered `400` with a validation
problem (`errors` keyed by member) before any of the above.

## Sending a file — `PUT /api/v1/survey-uploads/{id}/files/{name}`

| Code | Status | When |
|---|---|---|
| `survey_upload.not_found` | 404 | the upload is not one the account may see. `11-errors/not-found` |
| `survey_upload.file_unknown` | 404 | `{name}` was not declared when the upload was opened. `11-errors/file-unknown` |
| `survey_upload.size_mismatch` | 400 | the body's length is not the declared size; member `declaredSize`. `11-errors/size-mismatch` |
| `survey_upload.content_mismatch` | 400 | the bytes are not what the role says — an `archive` that is not a ZIP, a Therion file that is not text; member `file`. Nothing is kept. `11-errors/content-mismatch` |
| `survey_upload.not_receiving` | 409 | the upload is complete or abandoned and the file had not arrived. (A repeat of a file that **did** arrive answers `200` with the first answer, whatever the upload's state.) |
| `survey_upload.expired` | 410 | the upload passed its `expiresAt` before every file arrived and was closed; open a new one. `11-errors/expired` |
| `acl.forbidden` | 403 | the account no longer may write to the cave |
| `access.create_forbidden` | 403 | a file without a survey format, and the account no longer may create documents |
| `survey_upload.visibility_invalid` | 400 | a file without a survey format, `documentsVisibility` is `cavingGroup`, and the cave no longer has a caving group the account may file it under |

## Completing, abandoning, running again

| Code | Status | Route | When |
|---|---|---|---|
| `survey_upload.files_missing` | 400 | `complete` | a declared file has not arrived; member `files` lists them. `11-errors/files-missing` |
| `survey_upload.expired` | 410 | `complete` | as above |
| `survey_upload.not_receiving` | 409 | `DELETE` | the upload is complete; it is undone from the web, not abandoned. `11-errors/not-receiving` |
| `survey_upload.not_completed` | 409 | `rerun` | the upload never completed |
| `survey_upload.run_in_progress` | 409 | `rerun` | a run is already queued or running. `11-errors/run-in-progress` |
| `survey_upload.output_unknown` | 400 | `rerun` | as for opening |
| `survey_upload.not_found` | 404 | all | the upload is not one the account may see |

`complete` repeated on a completed upload answers `202` with where it stands; `DELETE` repeated on
an abandoned one answers `204`.

## The public link

The status answer's `publicLinkRefusal` says why an upload has no public link — not an error, a
reason to show beside the missing link:

| Value | Meaning |
|---|---|
| `installation-off` | this installation makes no public links |
| `account-off` | the uploader turned public links off in their SilexGIS settings |
| `no-share-right` | the account that completed the upload may not share the cave with people outside the installation |
| `protected-cave` | the cave's position is protected, so its survey results are never published — also when protection was switched on after the link was made |
| `revoked` | someone who may write to the cave withdrew the link on the web |

The web's routes for making a link again and withdrawing it (not opened by an access key):

| Code | Status | Route | When |
|---|---|---|---|
| `survey_upload.not_completed` | 409 | `POST …/public-link` | the upload is still receiving files, or was abandoned |
| `survey_upload.public_links_disabled` | 409 | `POST …/public-link` | the installation makes no public links |
| `survey_upload.public_link_forbidden` | 403 | `POST …/public-link` | the account may not share the cave |
| `survey_upload.public_link_refused_protected` | 409 | `POST …/public-link` | the cave's position is protected |
| `survey_upload.public_link_not_found` | 404 | `GET /api/v1/public/survey-uploads/{token}` | **one answer for everything**: an invented, malformed, withdrawn or replaced token, a cave protected now, an installation that turned links off, an upload or cave that is gone. `11-errors/public-link-not-found` |

## How a processing run ended *(row)*

| Code | Stage | When |
|---|---|---|
| `survey_pipeline.files_missing` | `archive` | an archived file is no longer in the server's store — an operator matter; the upload can be run again once it is restored |
| `survey_pipeline.archive_invalid` | `unpack` | the archive is not a TopoDroid survey archive: no `manifest`, `manifest` not the first entry, no SQL dump at its root, a database version older than 21, an entry name that leaves the archive, an encrypted entry, or not a ZIP at all. `08-status-failed` |
| `survey_pipeline.archive_too_large` | `unpack` | over 10,000 entries, more unpacked bytes than the installation allows, or an entry that inflates suspiciously (over 200:1) |
| `survey_pipeline.unexpected` | any | a defect on the server; its log holds the detail |

Sent by a server that compiles: `survey_pipeline.source_missing` (compiling asked for, no `.th`
sent), `survey_pipeline.source_invalid` (more than one `.th`, or a survey that reads a file not sent
with it), `survey_pipeline.compiler_unavailable` (a compiler is configured but could not be reached
— the files are archived), `survey_pipeline.compile_failed`, `survey_pipeline.compile_timeout`,
`survey_pipeline.output_missing`, `survey_pipeline.store_failed`.

## What is wrong with one output *(on an artefact entry)*

These sit in `artefacts[].error` (`{ code, message }`) on a run that **succeeded**; the `message` is a
fixed English sentence per code.

| Code | Entry | When |
|---|---|---|
| `survey_pipeline.output_missing` | any output `kind`; `sizeBytes` and `contentUrl` are `null` | the output was asked for and Therion did not write it (a KML or a georeferenced map of a survey with no position, say). The same code fails the whole run, at `compile`, only when nothing asked for was written |
| `survey_pipeline.crs_unknown` | `lox` | the survey is fixed in a coordinate system the server has no EPSG code for, so the model is stored and not read into stations (`modelStatus: failed`); `message` on the run says the same |

**A server with no compiler does not fail.** When the capabilities say `compiler.available: false`,
a run ends `succeeded` at stage `unpack` with a sentence in `message` saying the survey was archived
and read but not compiled (`06-status-succeeded`). Show it as a success with that note.
