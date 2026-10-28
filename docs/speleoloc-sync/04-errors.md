# What a failure looks like, and what to do about it

This document is for whoever writes the client side. It is the list of ways a request on this
surface can fail, what each one means, and — the part that matters on a phone in a cave — what the
client is supposed to do about it.

It is written as the server is built rather than reconstructed afterwards, because a catalogue
assembled at the end is how codes end up undocumented. It has since been reconciled against the
server source in both directions: **every code the sync and QR surface can emit appears here, and
every code here is one the surface actually emits.** A code that is not in this document is not one
these routes produce today.

A write fails at two levels — the batch as a whole, and each row inside it — and section 6 is where
that distinction lives. A row's verdict is not an HTTP status: it rides a `200` beside the rows
that were written.

---

## 0. The action vocabulary

Every failure row below names one action from a closed set, so a client can branch on it in one
place instead of writing a case per code. The word is the first thing in the last column; the
sentence after it is why, and what "again" means for that particular code.

| Action | What the client does |
|---|---|
| `retry` | Send the same request again, unchanged, after a backoff. It can succeed later. |
| `re-auth` | Refresh the credential, or sign in again; then send the request again. |
| `apply-and-resubmit` | Take what the answer carries — the server's row, or the thing it names as missing — change the request accordingly, and send it again. |
| `surface-to-user` | Only a human can move this forward: a caver, or the installation's administrator. Say what the server said; do not invent a diagnosis. |
| `stop` | Nothing the device can do makes this request succeed. Do not resend it; do not loop. |
| `ignore` | Informational. The request succeeded and this is an annotation on it. |

`stop` is not "give up on syncing". It means *this request, as sent, is finished*: the caver's data
is still on the device, which is the source of truth, and the next run may well succeed because the
installation changed.

## 1. The envelope

Every failure is an RFC 9457 problem document, `Content-Type: application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "That resume position was not issued by this server.",
  "code": "sync.cursor_invalid"
}
```

**Branch on `code`, never on `detail` and never on `title`.** `code` is a stable identifier and is
what this document is a list of. `detail` is a sentence for a human reading a log; it is not
translated, it is not stable, and it is sometimes absent. `status` is the HTTP status and is
meaningful, but several different codes share one status.

**Two answers are produced before any route runs, and carry a code the server adds itself.** A
`401`, from authentication, and the QR route's `429`, from the rate limiter. No handler runs for
either, so until 2026-10-16 they arrived with no `code`; the server now writes one wherever an answer
would otherwise leave without it — `auth.unauthenticated` and `rate.limited`. They are
`application/problem+json` like every other refusal, carrying `status`, `title`, a `traceId` and the
`code`, and no `detail`. The `401` carries a `type` as well; the `429` does not, which is one more
reason not to key anything off that field:

```json
{"type": "https://tools.ietf.org/html/rfc9110#section-15.5.2", "title": "Unauthorized", "status": 401,
 "traceId": "00-f755a89a14e9ed9515a25af937676607-36e31dc4d28ba6ed-00", "code": "auth.unauthenticated"}
```

**Branch on the status first all the same.** A server older than that date answers both with no
`code`, and a client that reached for `code` before the status would find none there and fall
through to whatever it does with an answer it does not understand — which for a `401` means never
refreshing the credential.

**On any other status, a response with no `code` is not from this API.** A proxy, a captive portal
or a load balancer between the device and the server produces its own errors, usually as HTML. On a
`4xx` or `5xx` that is neither `401` nor `429`, treat a body that does not parse as a problem
document as a transport failure, not as a server verdict — but never let that rule reach a `401`,
because a token that has expired would then be retried for ever instead of refreshed, and sync would
stop within the access token's lifetime with nothing in the log but retries.

## 2. Authentication and authorisation

| Status | Code | Means | Action |
|---|---|---|---|
| 401 | `auth.unauthenticated` | No token, an expired token, or a token this server did not issue. A problem document carrying `status`, `title` (`Unauthorized`), a `traceId` and this code — the code since 2026-10-16; an older server sends none, so branch on the status. **Every route in this slice answers this** — none of them is anonymous | `re-auth` — refresh the credential, then send the request again. Never resend with the same token, and never treat this as a transport failure to retry |

**There is no general `403` code on this surface.** Every refusal a sync route can answer with a
`403` names its own reason — `sync.caving_group_forbidden` is the only one today, in section 4 and
in section 6.1 — so a client never has to interpret a bare authorisation failure. The API does have
a generic `acl.forbidden`, and the earlier revision of this document listed it here; no sync route
emits it, and the cross-check removed it rather than leave a client writing a branch that can never
be taken. It is reachable elsewhere on the server, from the web interface's own cave-publication
routes (section 8), which a device does not call.

A `401` on `GET /api/v1/sync/capabilities` is the cheapest way to find out a stored credential has
lapsed, which is one reason to ask it first. Refreshing is described in the sign-in document, which
also carries the `auth.*` codes the sign-in and two-factor calls answer with — they are not repeated
here.

**There is no `403` for a sync set somebody else owns.** It is a `404` — see the next section.
The one thing an installation can change about that is whether a full administrator, reading a set
by an identifier they already hold, gets it back instead of the `404`
(`SILEXGIS__Sync__AllowAdministratorRead`, off by default). It is decided by the account, not by
the client: any caller holding a full-administrator account gets it, a device included. Nothing
else widens — every other route, the transfer routes included, answers `sync.set_not_found` for a
set the signed-in account does not own however that setting stands.

## 3. Reading

| Status | Code | Means | Action |
|---|---|---|---|
| 404 | `sync.set_not_found` | No sync set with that id belongs to this account. **A set that exists but belongs to somebody else answers exactly this**, so the route cannot be used to count other people's devices. The single exception is an administrator reading one set on an installation that has allowed it, which turns on the account and not on the client, so a device signed in as a full administrator gets it too — a download or an upload, by contrast, answers this for a set the signed-in account does not own, unconditionally | `surface-to-user` — the set was deleted, or the device is signed in as a different account. A caver picks a set again |
| 400 | `sync.cursor_invalid` | The `cursor` was not issued by this server: truncated, re-encoded, hand-made, or from an older contract | `apply-and-resubmit` — drop the cursor and start the set again from the beginning, accepting that this is a full re-read. Never resend it as sent |
| 409 | `sync.cursor_stale` | The `cursor` was issued by this server, but against an older revision of the sync set: the selection or its settings have been edited since | `apply-and-resubmit` — drop the cursor and read the set from the beginning. Retrying the same cursor will never succeed |

The documents catalogue (`GET /sync/sets/{id}/documents`), the trips stream
(`GET /sync/sets/{id}/trips`), the reviews stream (`GET /sync/sets/{id}/reviews`) and the rasters
stream (`GET /sync/sets/{id}/rasters`) answer exactly these three codes, on exactly these terms: each
is read the way the feature download is read. Its cursor is its own — a
position the feature download issued is answered `sync.cursor_invalid` there, and the other way
round — so a device keeps one cursor per stream and drops only the one refused.

**The read routes carry no field validation, so `validation.failed` is not among them.** A
`pageSize` outside the range the capabilities answer names is clamped rather than refused, and a
cursor that cannot be read answers `sync.cursor_invalid` rather than a validation failure. The
code exists on this surface only on the two set-writing routes, on the upload and on the file-address
route below.

### 3.1 Fetching files

`POST /sync/files/links` refuses a request whole only when it is malformed. Everything else is
decided per item, and a refused item rides a `200` beside the addresses that were given, in the
answer's `refused` list:

| Status | Code | Means | Action |
|---|---|---|---|
| 400 | `validation.failed` | No items, more than 200, an empty document id, or a `rendition` that is neither `"original"` nor one of 2400, 1200, 480, 160 | `stop` — fix the client |
| item | `sync.document_unavailable` | No such document, it was deleted, or this account may no longer read it — the three are one answer on purpose, so the route cannot be used to ask whether a document exists | `surface-to-user` — drop the file from the device's catalogue copy; the next catalogue read says whether it is still listed. Retrying the same item does not help |
| item | `sync.original_withheld` | The file itself is not handed to this account — a video, a sound recording or a picture that cannot be rewritten without its location, for an account that may not have the location, or for every account while the installation keeps location out of photo files (unless it also lets accounts that may have the location download such files, which then reach those accounts again). Whether a position was ever read out of the file does not matter: a phone writes one into a video that nothing here reads. Pictures of it may still be offered | `apply-and-resubmit` — ask for a rendition instead where the catalogue lists any; otherwise `surface-to-user` |
| item | `sync.rendition_unavailable` | The server draws no picture of this kind of file (anything but an image) | `apply-and-resubmit` — ask for `"original"` instead, if the catalogue's `reach` allows it |

An address handed out is a signed URL that answers `404` once it expires — ask for a new one; that
is not one of these codes, and the content route never says why.

**`sync.cursor_invalid` is deliberately not silent.** A server that quietly restarted an
unrecognised cursor would hand a device a full re-download that looks exactly like an incremental
page, and the device would have no way to know it had lost its position.

**`sync.cursor_stale` is a different failure and needs a different reflex.** The cursor is genuine
and the position it names is real; what changed is the set it names a position *within*. Adding a
cave to a sync set writes nothing to any feature row, so nothing under that cave is ever after the
device's stored position — a server that accepted the cursor would answer "nothing new" and the cave
the caver just asked their phone to carry would never arrive. This is the ordinary consequence of
editing a set from the settings page, not a fault: expect it after every edit, and answer it by
starting the set again rather than by backing off and retrying.

### 3.2 The people directory

| Status | Code | Means | Action |
|---|---|---|---|
| 409 | `sync.feature_disabled` | The installation has the people directory switched off; `people` is then missing from `features` too | `stop` — do not offer choosing a caver; ask the capabilities again on the next sync |
| 403 | `access.forbidden` | This account may not be told who people are | `stop` |

## 4. Editing a sync set

| Status | Code | Means | Action |
|---|---|---|---|
| 400 | `sync.root_not_found` | A named root feature does not exist **or is not readable by this account** — the two are answered alike on purpose, so the route cannot be used to ask whether a cave exists | `surface-to-user` — the caver chose something this account cannot carry. Offer the selection again |
| 400 | `sync.caving_group_not_found` | The named caving group does not exist | `surface-to-user` — the group was deleted, or the id is stale. Offer the group list again |
| 403 | `sync.caving_group_forbidden` | The account is not entitled to the caving group named — when writing a set, because it is not a member; when uploading, because it is no longer entitled to bind rows to the group the set carries. The question is asked again at every upload rather than only when the set was written, because that is when the binding is actually made | `surface-to-user` — only an administrator can restore the membership. Retrying changes nothing |
| 400 | `sync.set_revision_required` | A replacement stated no `baseRevision`. A write here replaces the whole set — the selection and the settings document together — so it is arbitrated on the revision the caller last read, exactly as an uploaded row is | `apply-and-resubmit` — re-read the set and send its `revision` back |
| 409 | `sync.set_conflict` | The `baseRevision` sent is not the set's current revision — somebody else wrote it since this caller last read it. The settings page and the caver's own phone hold the same set and both post the whole of it, so this is an ordinary race and not a fault | `apply-and-resubmit` — re-read the set, re-apply the change, and re-send with the revision that read returned |
| 400 | `validation.failed` | The body failed validation — a name that is blank, a root list with a duplicate | `stop` — fix the client |

**`sync.set_conflict` is also what a `DELETE` answers when it loses a race.** A delete sends no
`baseRevision` and cannot conflict on one, but if the set is written by somebody else between the
read and the delete the removal is refused with the same `409` and the same code. The action is the
same: read the set again and decide again, this time knowing what changed. It is worth stating
because the served description declares only the `404` on that route.

## 5. Transport failures, which are not codes

These have no `code` because they never reached the application. On a mobile connection they are the
common case, not the exception. All of them are `retry`.

- **A timeout or a dropped connection mid-page.** The download holds no server-side state, so the
  request is repeatable exactly as sent: re-issue it with the same cursor. A page is either received
  whole or not at all.
- **A `502`/`503`/`504` with an HTML body.** Something in front of the server. Back off and retry.
- **A redirect.** Never follow one on a sync route. The API does not redirect; something else did.

## 6. Uploading: the batch, and then each row

A write fails at two levels, and they are answered differently on purpose. **The batch** can be
refused as a whole — before a single row is looked at — and then the answer is a problem document
like every other in this file. **A row** cannot: a device that edited forty caves offline and lost
one of them to a conflict has to be told which one, and must not have the other thirty-nine thrown
away with it. So a row's verdict rides a `200` inside the answer, one entry per row.

The batch-level checks happen in a fixed order, and knowing it saves guessing: the contract version
first, then the row count, then the set's ownership, then **the replay check** — a batch identifier
already applied returns its stored answer here, before anything else is asked — then whether the
account's uploads wait for a reviewer and the device can be told so, then the caving-group binding, and
only then the rows. A replayed batch is therefore answered even by an account whose
group membership has since been withdrawn, deliberately: it is being told what already happened, not
being allowed to write again.

### 6.1 The whole batch

| Status | Code | Means | Action |
|---|---|---|---|
| 409 | `sync.contract_unsupported` | The device sent a `contractVersion` this server does not speak. Asked before the rows are looked at, so nothing was written. The server's own version is in `detail` | `stop` — and tell the caver the application needs updating, or the installation does. Retrying cannot help |
| 400 | `sync.batch_too_large` | More items than `uploadRowsMax` from the capabilities answer, counted over every section of the upload together | `apply-and-resubmit` — split the batch. **Mint a new batch identifier for each part**; a batch identifier stands for one attempt |
| 404 | `sync.set_not_found` | No sync set with that id belongs to this account | `surface-to-user` — as in section 3 |
| 409 | `sync.batch_conflict` | The same batch identifier arrived twice at once, and the first copy is still being applied | `retry` — after a short wait. The winner's answer is what a later send returns |
| 409 | `sync.client_update_required` | The account's uploads wait for a reviewer, and the request did not list `held` in `clientFeatures` — so the device could not be told that a row was kept for review rather than written. Refused whole; nothing was written or kept | `stop` — and tell the caver the application needs updating. A build that sends `held` gets the row status `held` instead |
| 400 | `validation.failed` | The body failed validation — a row, a recording or a scan named twice, a recording without `caveId` or `startedAt`, a scan without `recordingId` or `scannedAt`, an upload with no item in any section, a property document that is not an object, more than 16 `clientFeatures` or one longer than 64 characters | `stop` — fix the client |
| 403 | `sync.caving_group_forbidden` | The set is bound to a caving group this account is no longer entitled to bind rows to. Nothing was written | `surface-to-user` — only an administrator can restore the membership |
| 400 | *(a write-service code)* | The write service refused something the loop could not attribute to one row. Nothing is kept. The names are listed under section 6.3 | `stop` — the batch as sent cannot be applied |

### 6.2 One row inside a `200`

Each entry in `rows` carries a `status`, and a `code` when there is a reason worth naming. Only
`rejected` and `conflict` carry one.

| `status` | `code` | Means | Action |
|---|---|---|---|
| `created` | — | The row was not here and now is, under the identifier the device gave it | — store the `revision` as the row's base revision |
| `updated` | — | The row was here, the device held the current version, its edit was written | — store the `revision` |
| `deleted` | — | The row was removed | — drop it locally |
| `unchanged` | — | Nothing was written and nothing was wrong: the row already said what was asked. A resent create whose identifier is already here lands here, and so does a delete of a row that was never received | — store the `revision` if one came back |
| `held` | — | The account's uploads wait for a reviewer. Nothing was written; the row is kept as a proposal under `reviewId`, and `revision` is the one the row already had (null for a new row). Sending the same content again is answered with the same `reviewId` and keeps nothing new; different content replaces the earlier proposal under a new `reviewId`. Only ever answered to a request that listed `held` in `clientFeatures` | `ignore` — keep the row as it is on the device and send it again as usual; it is not an error. The decision arrives later, under `reviewId` |
| `conflict` | `sync.conflict` | Somebody wrote the row after the device last read it. The device's version was **not** applied | `apply-and-resubmit` — take the server's row from `conflicts`, merge, and send again with the revision that row carries. **A row that lost and is missing from `conflicts` is a row whose position this account may not have** — the same answer a download gives, absence rather than a blurred stand-in. Re-read it instead |
| `rejected` | `sync.row_not_found` | A `baseRevision` was sent for a row that is not on this server | `apply-and-resubmit` — send it as a new row, with `baseRevision` null |
| `rejected` | `sync.id_conflict` | The identifier is in use here by a row this account may not read | `surface-to-user` — do not re-key the row silently. Mint a new identifier only if a human establishes that the row is genuinely a different one |
| `rejected` | `sync.row_deleted` | The row was removed on the server | `apply-and-resubmit` — the ordinary answer is to drop it locally. Sending it again as a new row, under a new identifier, is a caver's decision, not the client's |
| `rejected` | `sync.row_forbidden` | The account may read the row but not write it | `stop` for this row |
| `rejected` | `sync.row_delete_forbidden` | The account may write the row but not remove it. Editing and removing are separate rights here exactly as they are in the web interface, because a delete takes the row's whole containment subtree with it | `stop` for this row |
| `rejected` | `access.create_forbidden` | The account may not create a row of this shape in this place, and binding the selection to a caving group would not change that. This is the only non-`sync.` code the row loop mints itself | `stop` for this row |
| `rejected` | `sync.set_unbound` | The selection names **no caving group**, and that is what is standing in the way: the same account, sending the same row through a selection bound to a club it belongs to, would have been allowed. The deciding field is on the selection, not on the row, so it is not in the request that failed | `surface-to-user` — ask the caver to choose a caving group for this selection, then `apply-and-resubmit`. The client can offer the clubs the account belongs to |
| `rejected` | `sync.parent_required` | On **a new row**: it named no container, and **this kind** only exists inside one. Whether a kind does is a property of the kind: `cave_area` and `cave_place` do, `surface_area` does not and is written with nothing above it | `apply-and-resubmit` — send the container's identifier. Protection and visibility are inherited along containment and along nothing else, so a row of such a kind with nothing above it would be unguarded whatever guards the cave it belongs to |
| `rejected` | `sync.parent_not_found` | On **a new row**: the named container is not on this server, or is not readable by this account — answered alike, so the field cannot be used to ask whether a cave exists | `apply-and-resubmit` — send the container in the same batch, before the row, or drop the row |
| `rejected` | `sync.parent_forbidden` | On **a new row**: the account may read the container but not add to it | `stop` for this row |
| `rejected` | `sync.location_forbidden` | **A new entrance** was sent for a cave whose position this account may not see exactly. It is refused rather than half-applied, because a cave's own map point is its main entrance's and writing one would move the other | `stop` for this row. The coordinate may be one this server handed the device blurred or not at all, and it must not travel back as truth |
| `rejected` | `sync.geometry_invalid` | The geometry could not be read, or is the wrong shape for the kind — an entrance carries a point | `apply-and-resubmit` after fixing it, if the client can; otherwise `surface-to-user` |
| `rejected` | `sync.type_unknown` | On **a new row**, `caveTypeCode`, `entranceTypeCode` or `featureTypeCode` names a kind this installation does not have. **Codes, never numeric identifiers**: the number standing for "cave" here stands for something else on the next server | `surface-to-user` — an administrator adds the kind, or the caver picks a different one |
| `rejected` | `sync.kind_unsupported` | This `kind` is not carried by sync yet | `stop` for this row, permanently in this contract version |
| `rejected` | *(a write-service code)* | The write service refused this row — a broken containment shape, a property document that fails its schema. The `detail` says which, and section 6.3 names them | `apply-and-resubmit` after fixing it, if the client can; otherwise `surface-to-user` |

An entry in `people` (section 12.2 of the protocol document) is answered in the same shape, with the
phone user's `deviceUserId` as its `id`. It is `created`, `updated` or `unchanged` — `updated` also when
a link made in the browser was kept and only the names were taken — or one of these:

| `status` | `code` | Means | Action |
|---|---|---|---|
| `rejected` | `sync.person_not_listed` | The `caverId` is not a caver the people directory lists for this account. Nothing about this phone user was written | `apply-and-resubmit` — read the directory again and choose from it, or send the phone user without `caverId` |
| `rejected` | `sync.feature_disabled` | The installation has the people directory switched off, so it takes no links from phone users to cavers. Nothing about this phone user was written | `apply-and-resubmit` — send the phone user without `caverId`; stop offering the choice while `people` is missing from `features` |

An entry in `documents` (protocol, *Sending photos and documents*) is answered in the same shape,
with the document's `id` and, when it was written, its new `revision`. It is `created`, `updated`
(what is said about it changed, or new bytes became its current revision), `deleted` or `unchanged`,
or `conflict` with `sync.conflict` exactly as a feature row is — re-read the document from the
catalogue and send the row again against the revision it carries. A `baseVersionId` that is no longer
the document's current revision is the same `conflict`: somebody gave it new bytes since, and the
row is sent again against the `versionId` the catalogue now names. Refusals a feature row also meets
mean the same here: `sync.row_not_found`, `sync.id_conflict`, `sync.row_deleted`,
`sync.row_forbidden`, `sync.row_delete_forbidden` and `access.create_forbidden`. Those of its own:

| `status` | `code` | Means | Action |
|---|---|---|---|
| `rejected` | `sync.file_bytes_missing` | The row brings content the server does not have — a new document, or (on an edit naming no `baseVersionId`) a hash different from the current revision's — and names no staged upload this account still holds: none was named, it was never completed, it was withdrawn or already adopted, or it waited past the staging retention | `apply-and-resubmit` — send the bytes (`stageForSync: true`), then send the row again naming the new transfer |
| `rejected` | `sync.file_hash_mismatch` | The staged upload the row names holds different bytes: its hash, or the size the row states, is not the row's | `apply-and-resubmit` — compare the hash `complete` answered with the phone's own; send the right bytes, or name the transfer that holds them. The staged bytes are left where they are |
| `rejected` | `sync.caving_group_forbidden` | On **a new document**: this account may not add documents to the caving group the selection is bound to | `stop` for this row |
| `rejected` | `sync.document_unavailable` | The document has no file of its own here, so there is nothing for new bytes to replace | `stop` for this row |

A refused document row adopts nothing: whatever it named stays staged until its retention runs out,
so the corrected row can name the same transfer.

An entry in `documentLinks` (protocol, *Hanging a document on a feature*) is answered in the same
shape, **in the order the links were sent**, with the link's identifier as its `id` — or the
document's, for a link refused before it existed — and, when written, the link's `revision`. It is
`created`, `updated` (its role, caption or place changed), `deleted` (taken down) or `unchanged`
(already hung, already down, or nothing different), `conflict` with `sync.conflict` when the link
went between the check and the write, or one of these:

| `status` | `code` | Means | Action |
|---|---|---|---|
| `rejected` | `sync.document_unavailable` | The document is not on this server, has been removed, or is not one this account may read. A link hands the document to everyone who can read the target, so the account must be able to read it itself | `apply-and-resubmit` — send the document first (in the same upload, before the link, is enough); otherwise `stop` for this link |
| `rejected` | `sync.target_not_found` | The target is not on this server, is not readable by this account, or is not in this selection — answered alike, so the field cannot be used to ask what exists. For a recording or a scan: it is absent, removed or not readable, or its cave (or a scan's place) is not in the selection | `stop` for this link; re-read the selection |
| `rejected` | `sync.location_forbidden` | The target is a feature whose location this account may not see — or a recording whose cave, or a scan whose cave or place, is one — so the catalogue would never hand it a file hanging there. A feature the same upload created in its `rows` is exempt; a document sent under a feature's identifier is not | `stop` for this link — keep the file on the phone, or ask somebody who may see the location to hang it |
| `rejected` | `sync.row_forbidden` | The account may read the target but not change what hangs on it (Write on the target; on a recording or a scan, Write on the recording) | `stop` for this link |
| `rejected` | `sync.kind_unsupported` | The target's `kind` is not one a phone may hang files on — `feature`, `tripRecording` and `tripPoint` are | `stop` for this link, permanently in this contract version |

An entry in `tripRecordings` or `tripPoints` (section 13.2 of the protocol document) is answered in
the same shape, with the recording's or the scan's identifier as its `id`. Their `created`, `updated`,
`deleted`, `unchanged` and `conflict` mean what they mean for rows — except that a `conflict` carries no
echo: read the recording again from the trips stream — and they share `sync.row_not_found`,
`sync.id_conflict`, `sync.row_deleted`, `sync.row_forbidden`, `sync.row_delete_forbidden` and
`access.create_forbidden` with rows. These are their own:

| `status` | `code` | Means | Action |
|---|---|---|---|
| `rejected` | `sync.cave_not_found` | On a recording: the named cave is not on this server, is not a cave, or is not readable by this account — answered alike | `surface-to-user` — the recording names a cave this account does not have; send the cave first if it is new |
| `rejected` | `sync.caving_group_forbidden` | On a new recording: the set is bound to a caving group this account may not add trips to | `stop` for this item; choose another set |
| `rejected` | `sync.recording_not_found` | On a scan: the recording it belongs to is not on this server, is not readable by this account, or was removed | `apply-and-resubmit` — send the recording in the same upload, before its scans; drop the scan if the recording was removed |
| `rejected` | `sync.place_not_found` | On a scan: the named place is not on this server or is not readable by this account — answered alike | `apply-and-resubmit` — send the place in `rows` of the same upload, or send the scan without `placeId` |

**A field the device sent that is silently not applied is not an error.** An account that may write
a row but may not see its position exactly keeps its name and description edits and loses only the
geometry, the altitude and the position quality — the row comes back `updated`, and it is
`updated` honestly. The alternative is losing a caver's rename to protect a coordinate that was
never going to move.

**Five of the refusals above can only be met on a create.** `sync.location_forbidden`,
`sync.type_unknown`, `sync.parent_required`, `sync.parent_not_found` and `sync.parent_forbidden` are
all raised while a row is being **created**. On an update the server does not refuse: an update
carrying a position the caller may not set has that position dropped and the rest of the edit
applied, and an update reads neither `parentId` nor the three type-code fields at all — they are
ignored rather than applied or refused, so a row uploaded with a different container comes back
`updated` with its containment unchanged. **Changing a row's container or its kind is not something
this contract version carries**; do not build a re-parenting gesture on top of it and expect the
server to follow. So a device that meets `sync.location_forbidden` is always looking at a create, and a
device whose entrance edit comes back `updated` with the coordinate unchanged on the next download
is looking at the write-right rule working, not at a lost message.

### 6.2a One raster map or pin inside a 200

An entry in `rasterMaps` or `rasterPins` (section 15.2 of the protocol document) is answered in the
same shape, with the map's or the pin's identifier as its `id`. Their `created`, `updated`, `deleted`,
`unchanged` and `conflict` mean what they mean for rows — a `conflict` carries no echo: read the map
again from the rasters stream — and they share `sync.row_not_found`, `sync.id_conflict`,
`sync.row_deleted`, `sync.row_forbidden`, `sync.row_delete_forbidden`, `sync.caving_group_forbidden`,
`access.create_forbidden`, `sync.file_bytes_missing` and `sync.file_hash_mismatch` with rows and
documents. These are their own:

| `status` | `code` | Means | Action |
|---|---|---|---|
| `rejected` | `sync.cave_not_found` | On a map: the named cave is not on this server, is not a cave, or is not readable by this account — answered alike | `surface-to-user` — send the cave first if it is new |
| `rejected` | `sync.area_not_found` | On a map: the named area is not on this server, is not readable, or is not inside the map's cave — answered alike | `apply-and-resubmit` — send the area in `rows` of the same upload, or send the map without `caveAreaId` |
| `rejected` | `sync.map_cave_fixed` | On an edit: the map was sent with another `caveId` than the one it has | `stop` for this item — remove the map and send a new one |
| `rejected` | `sync.raster_not_an_image` | On a map: the staged bytes are not an image; nothing is stored | `surface-to-user` |
| `rejected` | `sync.raster_map_not_found` | On a pin: the map it is on is not on this server, or its cave is not readable by this account | `apply-and-resubmit` — send the map in the same upload; drop the pin if the map was removed |
| `rejected` | `sync.place_not_found` | On a pin: the named place is not on this server, is not readable by this account, or is not inside the map's cave — answered alike | `apply-and-resubmit` — send the place in `rows` of the same upload |
| `rejected` | `sync.pin_exists` | On a pin: another live pin already marks that place on the map | `apply-and-resubmit` — read the map from the rasters stream and edit that pin |

### 6.3 The write-service codes, named

Two rows above pass a code through from the shared write service rather than minting one in the
sync loop, so they are open-ended in the sense that they do not begin with `sync.`. They are not
open-ended in fact: this is the whole set, and they mean the same on this surface as they do in the
web interface.

| Code | Means |
|---|---|
| `feature.parent_required` | The kind must be contained and no container was resolved |
| `feature.parent_duplicate` | The same container was named twice |
| `feature.primary_parent` | The primary containment edge is wrong — absent, or named more than once |
| `feature.hierarchy_cycle` | The containment named would put the row inside itself |
| `feature.not_found` | A row the write referred to is not here |
| `feature.type_required` | The kind needs a feature type and none was resolved |
| `feature.geometry_invalid` | The geometry is unreadable or wrong for the kind, refused by the write service rather than by the sync loop |
| `feature.properties_invalid` | The property document failed the kind's schema, an undeclared key included — the schemas that exist are closed. **Only a kind that carries a schema can answer this**, which on this channel means `cave_place` and `surface_area`. A cave, an entrance or a generic row of a schema-less kind has its property document stored unread, so a coordinate smuggled into one of those earns no refusal at all; keeping positions out of the property document is the client's rule to hold, not this server's to enforce |
| `cave.not_found` | The cave a row hangs from is not here or not readable |
| `entrance.not_of_cave` | The entrance names a cave it does not belong to |

### 6.4 Sending file bytes ahead of an upload

The bytes a document row names travel first, through the resumable transfer routes opened with
`stageForSync: true` (protocol, *Sending photos and documents*). Their refusals are the transfer
routes' own:

| Status | Code | Means | Action |
|---|---|---|---|
| 400 | `validation.failed` | No file name, a size of zero, or a destination (cabinet, folder, attachment, batch or club) named beside `stageForSync` | `stop` — fix the client |
| 400 | `file.too_large` | The file is larger than this installation accepts | `surface-to-user` — the file cannot be sent as it is |
| 400 | `file.type_not_accepted` | The installation does not accept files of this type | `surface-to-user` |
| 403 | `access.create_forbidden` | This account may not add documents anywhere | `stop` — keep the file on the phone |
| 409 | `file.quota_exceeded` | Not enough storage room left for this account or for the installation; staged bytes already count | `surface-to-user` — send the rows that need no file, or withdraw staged files nothing will adopt |
| 409 | `upload.chunk_out_of_order` | The piece does not continue the file; the integer member `expectedOffset` names the offset to send from (the detail says the same in words) | `apply-and-resubmit` — send from `expectedOffset`, or, when the answer was lost, from the `receivedBytes` that `GET /api/v1/files/uploads/{id}` reports |
| 400 | `upload.size_mismatch` | The pieces add up to more than the declared size — including any piece sent after the bytes were staged | `stop` — open a new transfer with the right size |
| 409 | `upload.incomplete` | Completed before every byte arrived | `apply-and-resubmit` — ask how far it got (`GET`), send the rest, complete again |
| 404 | `upload.session_not_found` | No such transfer for this account: it never existed, belongs to another account, was withdrawn, or waited past its retention (or 24 hours idle while still arriving) | `apply-and-resubmit` — open a new transfer and send the bytes again |

## 7. The QR landing route

The public QR route is not part of sync and is reached by a scanner, not by a signed-in device, but
it belongs in one catalogue with the rest because the same application calls both.

| Status | Code | Means | Action |
|---|---|---|---|
| 404 | `qr.not_found` | Nothing resolves here. **Four different facts share this one answer on purpose**: the code is malformed, the code is longer than this server will look up, no cave carries it, or the cave that carries it is not published. They are indistinguishable so the address cannot be used to enumerate caves | `surface-to-user` — tell the visitor the code does not resolve at this installation, and say nothing about why |
| 429 | `rate.limited` | The per-address rate limit. The limiter answers before the route does; the body is `status`, `title` (`Too Many Requests`), a `traceId` and this code — the code since 2026-10-16, none from an older server | `retry` — after a wait. The window is one minute and the allowance is per installation |
| 200 | — | The code resolves and the cave is published. The answer is deliberately contentless: it says the code resolves at this installation and nothing else, so it carries no cave name and no coordinate | — |

## 8. Codes this document deliberately does not carry

The families below are documented elsewhere, or are a web route's rather than a device's, and the
boundary is stated so that a gap is not mistaken for an omission.

- **The review routes** — `/api/v1/review/…`, the web interface's routes for deciding about the
  uploads of an account under review (the protocol's section 14). A device does not call them; it
  reads the outcome on the reviews stream. Their codes, for whoever builds those screens:
  `review.batch_not_found` and `review.item_not_found` (404 — no such held batch or line, or the caller
  may neither read the review queue nor is the uploader), `acl.forbidden` (403 — may read the queue
  but not decide), `review.own_batch` (403 — nobody decides about their own uploads),
  `review.not_pending` (409 — nothing in it waits any more, or another decision reached one of its
  lines first; a turned-down line can still be accepted on its own while its bytes are kept and no later
  upload replaced it, and is answered this once that is no longer so), `review.source_unsupported` (409 —
  this server cannot decide that kind of held batch; its lines still wait), `review.changed_since` (409 — a held edit's
  row was written by somebody else since the phone read it; the problem lists the `reviewIds`, and
  nothing was decided — send `overCurrent: true` to write the accepted fields onto the current row),
  `review.fields_unsupported` (400 — a field list for a line that can only be decided whole, or a field
  it does not have; the line's `acceptableFields` lists the ones it has) and `validation.failed` (400 — a
  note longer than 2,000 characters). The picture of a held photo or raster map
  (`GET /api/v1/review/items/{reviewId}/preview?size=480|1200`, a rendering without metadata) answers
  `review.item_not_found` (404) also for a line whose content the caller is not shown,
  `review.preview_unavailable` (404 — the line keeps no picture: a file that is not an image, or bytes
  already adopted or swept), `review.preview_size_unsupported` (400) and `review.preview_failed` (400 —
  the bytes could not be drawn).
  **On the reviews stream**, a `failed` decision's `code` is the code the upload itself would have
  answered for that row (section 6.2), `review.dependency_failed` — a held row it depends on was
  not written, so it could not be either — or `review.proposal_unreadable` (a kept proposal this build
  can no longer read, after an upgrade that changed its shape). Undoing an accepted upload is the ordinary undo below; a
  held or turned-down batch is refused it with `import_batch.held` (409 — nothing of it was written).
  **A file import by an account under review** is not refused: its confirmation answers `held: true`
  and waits (section 14 of the protocol). On acceptance a held file import's line is decided `failed`
  with the code its source's commit would have answered — among them `import.file_gone` (the file it
  was made from is no longer here or no longer readable by the account), `photo_import.no_files`,
  `access.create_forbidden` / `access.caving_group_binding_forbidden` (a right lost since),
  `import.recording_already_converted` (the recording became a report meanwhile — another waiting
  confirmation of it was accepted first, or a phone's conversion made it one),
  `trip_recording.not_found` (a kept recording gone since) and `speleoloc_import.nothing_created` (only
  the trip line of a SpeleoLoc confirmation was accepted, and a trip with no scans is not created; a scan
  of it accepted later is then decided `review.dependency_failed`), and `review.write_refused` if a
  refusal of the replayed SpeleoLoc commit named no code of its own (every refusal this server makes
  names one, so it is a safety net, not an answer to expect). A
  catalogue that does not answer leaves the lines waiting and the decision is answered `503` with the
  catalogue's code.

- **The `auth.*` family and the OAuth `error` values** are in the sign-in document, with the calls
  that answer them. They are not repeated here.
- **`concurrency.if_match_required` (428) and `concurrency.version_mismatch` (412)** are how the
  rest of the API arbitrates a concurrent write. **No sync route uses either**: this surface
  arbitrates on `baseRevision` in the body instead, which is why `sync.set_conflict` and
  `sync.conflict` exist. A device that only calls sync routes will never see them; one that also
  calls an ordinary feature route will.
- **`acl.forbidden` and `cave.not_found` from the cave-publication routes** — `GET`, `POST` and
  `DELETE` on `/api/v1/caves/{id}/qr-publication`. Publishing a cave is an act of the web interface,
  performed by somebody with sharing rights; a device does not call these. `cave.not_found` there
  answers a cave that is absent, that is not a cave, or that the caller may not read, alike.
- **The undo of an upload** — `POST /api/v1/import-batches/{id}/revert`, the web interface's route
  for taking an upload back (the protocol's section 8.1). A device does not call it. Its codes, for
  whoever builds that screen: `import_batch.not_found` (404 — no such batch, or not the caller's),
  `import_batch.revert_forbidden` (403), `import_batch.already_reverted` (400),
  `import_batch.items_unknown` (400 — `itemIds` names a line that is not this batch's; nothing was
  undone), `import_batch.partial_undo_unsupported` (400 — `itemIds` sent for a file import, which is
  undone whole) and `validation.failed` (400). A `200` answers each line with `outcome` `undone`,
  `partly` or `not_undone`; a `not_undone` line's `reason` is one of `already_undone`, `purged`,
  `container_deleted`, `superseded`, `fields_left`, `no_history`, `forbidden`, or a write-service
  code from section 6.3 when putting the values back was refused; a field left on an edited line is
  `superseded`, `forbidden`, `redacted`, `no_pre_image` or `not_restorable`. `forbidden` on a deleted
  line means the caller may no longer delete the row, which is the right that restores it; `no_history`
  answers a line of an upload made before the history named its batch — nothing of it was taken back,
  and it is closed all the same. A line whose upload moved the cave's main entrance may also carry a
  skipped `IsMain` (`superseded` or `forbidden`) when the role could not be given back to the entrance
  that held it. A line about a phone recording or a scan names it in `tripRecordingId` or
  `tripRecordingPointId` instead of `featureId`; a recording or scan the upload created is deleted
  (`undone`); one it deleted comes back, and one it changed gets its fields back while they still hold
  what the upload wrote, exactly as a feature's do (`partly`, `fields_left` and `superseded` as above;
  a cave or place the row named before goes back only for a caller who may read it, `redacted`
  otherwise; `forbidden` when the caller may no longer change the recording, or delete it for a
  deleted one; `container_deleted` for a scan whose recording is deleted). A recording line whose
  recording had been made into a trip report names it in `tripLogId` (`null` when the caller may not
  read that trip), with `tripOutcome` `undone` (the report was deleted with it) or `not_undone` and
  `tripReason` `trip_changed` (somebody worked on the report since) or `converted_elsewhere` (an
  archive import made it).
- **The archive import's refusals of a recording the server already holds** — from
  `POST /api/v1/speleoloc-imports/{fileId}/preview` and `/commit` (a recording read out of an archive)
  and from `POST /api/v1/speleoloc-imports/recordings/{recordingId}/preview` and `/commit` (the same
  review over a recording a phone sent), the web interface's archive import. A device does not call
  these. `import.recording_already_converted` (409): the recording already has its trip report; the
  answer's `tripLogId` names it, or is `null` when the caller may not read that trip. Action: open the
  report instead of importing again; to import again, undo whatever made the report first.
  `import.recording_not_writable` (409): the recording is kept here under an account whose recordings
  the caller may not change — making it a report writes on it (and, for one deleted here, brings it
  back, which takes the right to delete it). Nothing is written. Action: ask somebody who may change
  the recording to import it, or import it onto nothing (the positions need the recording's report).
  Status of both: live.
- **The recording-sourced review** — `POST /api/v1/speleoloc-imports/recordings/{recordingId}/preview`
  and `/commit` answer exactly as the archive's routes do (the create rights, `trip_log.not_found`, the
  model and selection codes of the archive import, and the two refusals above), with one code of their
  own: `trip_recording.not_found` (404) — the recording is not here, is deleted, or the caller may not
  read it, alike. There is no saved review for a recording: the confirmation carries its decisions.
  Status: live. Action: `surface-to-user`.
- **Clearing a trip's attention flag** — `POST /api/v1/trip-logs/{id}/attention/clear`, the web
  interface's sign-off on a trip made automatically from a phone recording. A device does not call it.
  `401` without a session; `trip_log.not_found` (404) — no such trip, or one the caller may not read;
  `acl.forbidden` (403) — the caller may read the trip but not change it;
  `trip_attention.not_found` (404) — the trip has no flag; `trip_attention.changed` (409) — an upload
  raised the flag again between the caller reading it and the clearance, so nothing was cleared;
  `204` otherwise, including for a flag already cleared (the first clearance stands). Status: live.
  Action: `surface-to-user`; on `trip_attention.changed`, show the flag again and let the person clear
  it once they have seen the new reason.
- **Restoring a value from the history** — `POST /api/v1/history/restore`, the web interface's route
  for putting fields of a feature back to what they held before one recorded change. A device does not
  call it. Its codes: `history.entity_not_found` (404 — no such feature, or one the caller may not
  read), `history.entry_not_found` (404 — no such history row of that feature, or one that is not a
  feature's), `acl.forbidden` (403 — the caller may read the feature but not change it; an entrance
  asks this of its cave), `history.entry_not_restorable` (400 — the row is a creation or a deletion,
  not an edit), `validation.failed` (400 — a malformed request), and, as 400, `history.field_unknown`
  or a write-service code from section 6.3 when the values put back fail the ordinary edit's rules. A
  `200` lists the fields put back in `applied` and each field left in `skipped` with its `reason`:
  `not_restorable`, `redacted` (the caller's history hides it), `no_pre_image` (a large shape whose
  full copy was never kept) or `forbidden` (a position, for a caller without exact view).
- **The phone users screen** — `GET /api/v1/phone-users`, and `PUT` and `DELETE` on
  `/api/v1/phone-users/{deviceUserId}/link`: the web interface's routes for somebody keeping the
  roster to see which phone users the installation has met and to say, or take back, which caver each
  is. A device does not call them; they answer whether or not the people directory is switched on.
  Their codes: `access.forbidden` (403 — listing needs the right to be told who people are, linking
  and unlinking the roster keeper's), `sync.phone_user_not_found` (404 — no phone user with that
  identifier has been met), `sync.caver_not_found` (404 — the `caverId` names nobody on the roster)
  and `validation.failed` (400). A link made there is recorded as the roster keeper's, and a phone's
  later different choice does not replace it.

## 9. Where these codes appear in the served description

The server's own description at `/openapi/v1.json` declares, for each sync operation, statuses it
can answer with. The body is always the problem document in section 1, so the declared schema is the
same for all of them, and it declares the `code` member; **which codes a status can carry is not in
the description** and cannot be, because a code is a value rather than a type. This document is the list of codes, and it is written as each one is added
rather than assembled afterwards.

**The declaration is not a complete list of statuses either**, and this document is the authority
where they differ: the delete route declares its `404` and not the `409` described in section 4.
Treat the description as machine-readable orientation and this file as the contract.

Two further practical consequences:

- A generated client will give you a typed problem body with `type`, `title`, `status`, `detail`,
  `instance` and `code`, typed as a string. A server older than the one that declared it sends the
  same member undeclared; read it from the raw JSON if your generator drops unknown members and you
  must talk to one.
- Every operation on this server is named in the description, but the sync names are the ones
  chosen deliberately and pinned (`syncCapabilities`, `syncListSets`, `syncCreateSet`, `syncGetSet`,
  `syncReplaceSet`, `syncDeleteSet`, `syncDownload`, `syncUpload`, and for the second phase's routes
  `syncDocuments`, `syncFileLinks`, `syncPeople`, `syncTrips`, `syncReviews`, `syncRasters`). Error handling written against
  those names will not move; a name elsewhere follows the code that handles the route.

## 10. Refusals recorded as bytes

Prose about a payload drifts from the payload silently, so six of the failures above are also
committed as recorded exchanges under `contract/speleoloc-sync/v1/16-errors/` — the invalid resume
position, the stale one, a batch pinned to a contract version this server does not speak, an
account under review uploading from a build that cannot be told so, the people directory asked for
while it is switched off, and a document row naming bytes that were never staged. Each refusal of
the exchange carries a `status.txt` with the status line beside the problem body, because the status
is what a client branches on before it has read a field.

They are the three worth having as bytes rather than as description. The two cursor refusals arrive
as different statuses with different codes for what is nonetheless one decision on the device, which
is exactly the kind of distinction prose loses. And the whole-batch refusal is the shape a client is
most likely to get wrong: it carries **no `rows` array at all**, so code that reads per-row results
without first checking the status fails on the one answer an un-updated phone is most likely to
meet.

The correlation identifier a problem document carries is replaced in those recordings. It is a fresh
value on every request, it is for reading a server log with, and nothing on a device should branch
on it.

The three added with the second set of parts are `client-update-required` (`409`, refused whole,
no `rows`, like the version refusal), `feature-disabled` (`409` from `GET /api/v1/sync/people` while
the directory is off) and `file-bytes-missing`. The last is not a problem document at all: it is a
row of a `200` with status `rejected` (§6.2), so its recording has no `status.txt`, and a client
must not look for one.
