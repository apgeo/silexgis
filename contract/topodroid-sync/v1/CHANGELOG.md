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

## 2026-11-09 — the limits of a raw recording, in the capabilities answer

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — one member is added
to `capabilities.pointCloud`; nothing a device sends or reads changes meaning. One recording was
rewritten for it: `01-capabilities`.

- `capabilities.pointCloud.recording` states what an upload of origin `mandeye` may send:
  `maxFileBytes`, `maxTotalBytes`, `maxFiles`, `acceptedExtensions` and `odometryWorkerFactor` (how
  many times the recording's size the processing worker needs free while it runs). A tool that does
  not know the member ignores it.

## 2026-11-08 — a raw scanner recording as a survey upload

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — one value is added to
`capabilities.sources`; nothing a device sends or reads changes meaning. Two recordings were
rewritten for it: `01-capabilities` and `11-errors/source-unsupported`.

- `capabilities.sources` now lists `mandeye` after `pointcloud`: the raw folder of a MANDEYE
  handheld-scanner recording, sent as an upload of origin `mandeye`. A tool that does not know the
  value ignores it. The `source_unsupported` detail now names all three.

## 2026-11-08 — refused for lack of room, before anything is stored

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — one code is added on
opening and on sending a file, one on the run; nothing a device sends or reads changes meaning. No
recording was written or rewritten.

- `POST /survey-uploads` and `PUT /survey-uploads/{id}/files/{name}` may answer `409`
  `storage.insufficient` when the declared files (or this file) do not fit in the storage left:
  under the account's quota, the installation's cap, for a `pointcloud` upload the scans' own cap
  (`SILEXGIS__Scans__MaxTotalBytes`), or on the disk of the file store. Members `limit`,
  `neededBytes`, `roomBytes`, `missingBytes`. An open upload's declared sizes count as taken from
  the moment it opens until its files arrive, it is abandoned or it expires.
- New code on the run: `survey_pipeline.storage_insufficient` at `downsample` or `tile` — the step
  was not started because what it is estimated to write does not fit; `errorMessage` says how much
  is missing.
- `GET /api/v1/storage/room` (outside this contract, for the web; it does not take a device key)
  answers the totals, the caps and whether a given `addingBytes` fits.

## 2026-11-07 — a point cloud as a survey upload

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — one value is added to
a list, one member is added to the status and list answers, and three values are added to `stage`;
nothing a device sends or reads changes meaning. Every recording but `03-put-file` was rewritten
for the two additions below.

- `capabilities.sources` now lists `pointcloud` beside `topodroid`: an upload of origin
  `pointcloud` declares exactly one `.laz` or `.las` file, as role `archive`, under the
  installation's own cap for scans (`SILEXGIS__Scans__MaxUploadBytes`, 4 GiB by default).
  `maxFileBytes`, `maxFilesPerUpload` and `acceptedExtensions` still describe a survey tool's
  upload; the scans' own figures are the new `capabilities.pointCloud` block (`maxFileBytes`,
  `maxFilesPerUpload`, always 1, and `acceptedExtensions`, `.laz` and `.las`), which a device
  reads before offering a point cloud. The `source_unsupported` detail now names both.
- Added `pointCloud` to the status answer and to each listed upload: `null` for a survey tool's
  upload and for a point cloud whose header has not been read yet; otherwise the scan's facts
  (`modelId`, `pointCount`, `pointFormat`, `lasVersion`, `compressed`, `hasRgb`, `hasIntensity`,
  `sizeM`, `georeferenced`, `generatingSoftware`, and after tiling `tileBytes`, `tileCount`,
  `tilerVersion`, `displayVoxelM`). `crs`, `min` and `max` are sent only to a reader holding the
  cave's exact location, as is the `contentUrl` of an original whose header names a coordinate
  system; an original with none is offered to everybody who may read the upload.
- Added to `pointCloud` once a worker has run: `estimate` (`{ full, voxels[] }`, each row
  `{ voxelCm, points, estimatedTileBytes, estimatedSeconds }`, `voxelCm` null on the full cloud's
  row), `advice` (`{ level: none | warn | advise, suggestedVoxelCm, warnPoints, advisePoints,
  warnTileBytes }`), and after tiling `displayEveryNth`, `displayPointCount` and `originShift`
  (what was subtracted from the file's coordinates before tiling — a position for a georeferenced
  file, so sent only with the cave's exact location, like `min` and `max`).
- `POST /survey-uploads` takes an optional `display` for a point cloud — `{ method: full | voxel |
  everyNth, voxelCm?, everyNth? }` — deciding how much of the cloud to show before it is
  estimated. Without one, a run whose estimate puts the cloud over the installation's thresholds
  (`advice.level` is `warn` or `advise`) ends `succeeded` at `downsample` with the message
  "Estimated: … points. Choose how much to show, then run again." and tiles nothing; a cloud under
  them is tiled whole. A device that shows the estimate table offers the choice then.
- `POST /survey-uploads/{id}/rerun` takes `display` (the same shape; the run thins to it and tiles,
  reusing the estimate) and `fromStep` (a step id to do again even where the earlier run's product
  would do: `pointcloud.estimate`, `pointcloud.downsample`, `py3dtiles.tile`; applies to that
  rerun only). `fromStep` alone keeps the thinning the upload was running with. A `display` the
  thinning step refuses answers `400` `survey_upload.display_invalid`.
- The file route serves the original of a point cloud whose header names a coordinate system,
  and the thinned copy made from it, only to a reader holding the cave's exact location, as the
  upload's own `contentUrl` already did.
- New `stage` values for a point cloud's run: `inspect`, `downsample`, `tile`. A server with no
  point-cloud worker ends such a run `succeeded` at `tile` with the message "Archived and
  inspected. Not tiled: this server has no point-cloud worker."; a rerun once a worker is
  installed carries on.
- New code on the run: `survey_pipeline.point_cloud_invalid` at `inspect` (the file is not a LAS
  or LAZ file the server can read). `survey_upload.content_mismatch` now also answers a `.laz` or
  `.las` whose bytes do not begin with `LASF`.

## 2026-11-05 — the public page of results shows the map, the geodata and the whole figures

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — nothing a device sends
or reads changes; no recording was written or rewritten.

- A public link's page of results now shows everything the upload's own files contain: the map of
  the survey's centreline with its KML, GeoJSON and GPX downloads, and the compiler's figures whole
  (the stations at each extent with their absolute heights and grid positions, the stations of each
  loop, the coordinate system and declinations). The cave record's own position is not on it.
- Added a web-only route that is not part of this contract: `GET /api/v1/public/survey-uploads/{token}/geodata?format=kml|geojson|gpx`,
  decided exactly as the public page. `survey_upload.not_georeferenced` (404) and
  `survey_upload.geodata_format_unsupported` (400) now also answer there.
- The figures routes (public and signed in) answer `placed` as `true`/`false` on both, the whole
  figures, and `analyses`: the upload's own survey measured (statistics, network, orientation, depth
  profile) rather than the survey the cave is currently drawn from.

## 2026-11-05 — a page of results on the web, beside the contract

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — nothing a device sends
or reads changes; no recording was written or rewritten.

- The web pages a tool opens (`statusPageUrl`, `publicLink.url`) link to a page of results at the same
  address with `/view` added; a tool may open it directly.
- Added web-only routes that are not part of this contract: `GET /api/v1/public/survey-uploads/{token}/figures`
  (decided exactly as the public page), and, signed in, `GET /api/v1/survey-uploads/{id}/figures`,
  `…/{id}/geodata` and `…/by-public-link/{token}`. An access key is refused on the signed-in ones with
  the existing `403 auth.access_key_not_accepted`.
- New codes, on those web routes only: `survey_upload.not_georeferenced` (404) and
  `survey_upload.geodata_format_unsupported` (400).

## 2026-11-04 — the compiler's log on the public page

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — two members are added
to the public page's answer and one route is added beside it; nothing a device sends or reads
changes meaning.

- Added public page members `logTail` (the last lines the compiler wrote, as the status answer's
  `logTail`; `null` before a compile) and `log { name, sizeBytes, url }` (the whole log, after a
  failed compile as after one that worked; `null` when there is none).
- Added route `GET /api/v1/public/survey-uploads/{token}/log` (`log.url`): the whole log as a
  `text/plain` attachment, opened and refused exactly as the page is.
- Rewritten: `12-public-link`, `12b-public-link-compiled` (the added members).
- Codes: added `survey_upload.public_log_not_found` (404, the log route only, for a link that opens
  on an upload with no log).

## 2026-10-27 — the account's link defaults in the capabilities

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — one member is added
to the capabilities answer.

- Added capabilities member `publicLinks { enabled, defaultAudience }`, for the calling account:
  `enabled` is the installation's switch and the account's *Public links* preference together;
  `defaultAudience` (`public` or `organization`) is the account's *Default link audience*. A device
  presets its per-upload link choice from them; the preferences route
  (`/api/v1/me/survey-upload-preferences`) still does not accept the key.
- Rewritten: `01-capabilities` (the added member).
- Codes: none added, changed or withdrawn.

## 2026-10-26 — the size of a survey's older versions, and the uploader's own status

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — one member is added
to the status answer, and one caller who was answered `404` is now answered with less in it.

- Added status member `olderVersions { count, sizeBytes, warn, warnBytes, keepVersions }`: what the
  survey's versions older than its newest still hold of their runs' outputs, and whether that is
  past the installation's warning size (`SILEXGIS__SurveyUploads__OlderVersionsWarnMegabytes`,
  500 MB by default; `warnBytes` is `null` when the installation never warns). `keepVersions` is
  the installation's `KeepArtefactVersions`.
- `GET /api/v1/survey-uploads/{id}` now answers **the uploader** who may still read the cave but has
  lost its exact position — with `contentUrl` of every artefact and file, `publicLink` and `logTail`
  all `null`. Before, they were answered `404 survey_upload.not_found`; every other route still is.
- The first model of a cave the upload itself created becomes the cave's current model, its
  centreline the cave's shape, unless the installation switched that off. Nothing in the exchange
  changes; the protocol's sentence that no upload model becomes current is corrected.
- Rewritten: `02-open`, `04-complete`, `05-status-queued`, `06-status-succeeded`,
  `06b-status-compiled`, `07-open-next-version`, `08-status-failed`, `10-rerun` (the added member).
- Codes: none added, changed or withdrawn.

## 2026-10-26 — link audiences, a per-upload link choice, and making or withdrawing a link from the device

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — every change adds a
member, a route the key opens, or a code for a new situation; nothing sent before changes meaning.

- Added request member on `POST /api/v1/survey-uploads`: `publicLink { wanted, audience,
  allowProtected }`, all optional. `wanted: false` asks for no link even when the account's
  preference makes one; `wanted: true` asks for one even when it does not. `audience` is `public`
  (anybody holding the address) or `organization` (only a signed-in account of the installation);
  empty means the account's *Default link audience*. `allowProtected: true` is the caver's explicit
  acknowledgement — after a warning that the results show where the passages are — that a link may
  be made although the cave's position is protected; without it a protected cave still gets none.
  Absent, everything is decided as before.
- Added members on `publicLink` wherever it is answered (the status, the completion, the run-again
  answer, the listing): `audience` and `protectedAcknowledged`.
- `publicLink` is now answered to every caller who may read the upload, not only to those who may
  write to the cave, and the listing (`GET /api/v1/survey-uploads`) carries it on each item.
- A new value of `publicLinkRefusal`: `not-wanted` (the upload asked for no link).
- `POST` and `DELETE /api/v1/survey-uploads/{id}/public-link` now accept the survey-upload access
  key (they answered `403 auth.access_key_not_accepted` before). `POST` takes an optional body
  `{ audience, allowProtected }`; with no audience it keeps the audience of the link it replaces,
  else the upload's, else the account's default. A protected cave without `allowProtected: true`
  is still `409 survey_upload.public_link_refused_protected`.
- A new code: `401 survey_upload.public_link_sign_in_required` from the public page for a link made
  for the organization, read without signing in (a key does not count as signed in there). Asked
  only once the link would otherwise open; everything else is still the one `404`.
- A link made with the acknowledgement keeps opening while the cave stays protected; an ordinary
  link still closes the moment its cave is protected.
- Recordings rewritten: `02-open` (the request now carries `publicLink`), `04-complete`,
  `05-status-queued`, `06-status-succeeded`, `06b-status-compiled`, `08-status-failed`, `09-list`,
  `10-rerun`. Added: `13-make-public-link` (the device making an organization link with its key)
  and `11-errors/public-link-sign-in-required`.

## 2026-10-25 — per-output errors and a recorded compiled run

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working** — no tool has been
built against the artefact list yet, and everything here only adds to it.

- Added member: `artefacts[].error` — `{ code, message }` for what is wrong with one output, else
  `null`. Two codes: `survey_pipeline.output_missing` (asked for, not produced) and
  `survey_pipeline.crs_unknown` (a `lox` stored unread: its coordinate system has no known EPSG
  code). The second is new; the first was listed already as a run's code.
- An entry with `error.code: survey_pipeline.output_missing` names an output that has no file:
  its `sizeBytes` and `contentUrl` are `null`. Check `error` before offering a download.
- A failed compile now keeps the configuration and the whole log: `files[]` lists the
  `generatedConfig` files and `artefacts[]` the `log` on a `failed` status too.
- Recordings added: `06b-status-compiled` (the status after a compiled run, against a stand-in
  compiler writing invented outputs) and `12b-public-link-compiled` (that upload's public page).
  None rewritten.

---

## 2026-10-25 — public links to an upload's results

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working**: everything here is an
added member, a value of a member already announced, or a route a tool may ignore.

- `publicLink` (announced in version 1, always `null` until now) is filled from the `complete`
  answer on: `{ url: "/p/survey/<token>", createdAt }`, for an account that may write to the cave.
- Added member: `publicLinkRefusal` — why there is no link (`installation-off`, `account-off`,
  `no-share-right`, `protected-cave`, `revoked`), or `null`.
- Added route, read with no account: `GET /api/v1/public/survey-uploads/{token}`, the public page's
  own shape (`12-public-link`); every refusal is `404 survey_upload.public_link_not_found`
  (`11-errors/public-link-not-found`).
- Added codes on the web's routes for withdrawing and remaking a link (not opened by an access key):
  `survey_upload.public_links_disabled`, `survey_upload.public_link_forbidden`,
  `survey_upload.public_link_refused_protected`.
- Recordings rewritten: `02-open` and `07-open-next-version` (`publicLinkRefusal: null`),
  `04-complete`, `05-status-queued`, `06-status-succeeded`, `08-status-failed` and `10-rerun` (the
  link, its token recorded as a placeholder); added `12-public-link` and
  `11-errors/public-link-not-found`.

---

## 2026-10-25 — compiled uploads: stored artefacts and a model's reading

**Contract version: 1, unchanged. A tool pinned to version 1 keeps working**: everything here is an
added member or a value already announced.

- A server that compiles now ends a run at stage `read` (announced in version 1's stage list) and
  lists what it stored in `artefacts[]` — `kind` is an output's catalogue name or `log`, `name`
  carries the version stamp.
- Added member: `artefacts[].modelStatus` (`pending`, `processing`, `ready`, `failed`), how far a
  stored model's reading has got.
- `files[]` also lists the two files a compile generates around the survey, with
  `role: generatedConfig` (a role value already in version 1).
- Added member of the open request: `newCave.locationProtected` (boolean, default `false`), so a
  cave made by the upload can start with its position protected — before the entrance the survey's
  fixed station places is written.
- No code appeared or changed. Recordings rewritten: none — the recorded server does not compile.

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
