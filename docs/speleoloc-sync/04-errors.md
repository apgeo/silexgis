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

**Two answers are problem documents without a `code`, and they are the exceptions to everything
below.** A `401`, and the QR route's `429`. Both are produced before the route runs — the first by
authentication, the second by the rate limiter — so there is no handler to mint a code. They arrive
as `application/problem+json` all the same, carrying `status`, `title` and a `traceId` and no
`detail`. The `401` carries a `type` as well; the `429` does not, which is one more reason not to
key anything off that field:

```json
{"type": "https://tools.ietf.org/html/rfc9110#section-15.5.2", "title": "Unauthorized", "status": 401,
 "traceId": "00-f755a89a14e9ed9515a25af937676607-36e31dc4d28ba6ed-00"}
```

**So branch on the status first, and on `code` only afterwards.** A client that reaches for `code`
before looking at the status finds none on these two and falls through to whatever it does with an
answer it does not understand — which for a `401` means never refreshing the credential.

**On any other status, a response with no `code` is not from this API.** A proxy, a captive portal
or a load balancer between the device and the server produces its own errors, usually as HTML. On a
`4xx` or `5xx` that is neither `401` nor `429`, treat a body that does not parse as a problem
document as a transport failure, not as a server verdict — but never let that rule reach a `401`,
because a token that has expired would then be retried for ever instead of refreshed, and sync would
stop within the access token's lifetime with nothing in the log but retries.

## 2. Authentication and authorisation

| Status | Code | Means | Action |
|---|---|---|---|
| 401 | *(none)* | No token, an expired token, or a token this server did not issue. **A problem document with no `code`** — `status`, `title` (`Unauthorized`) and a `traceId`, nothing more. **Every route in this slice answers this** — none of them is anonymous | `re-auth` — refresh the credential, then send the request again. Never resend with the same token, and never treat this as a transport failure to retry |

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

**The read routes carry no field validation, so `validation.failed` is not among them.** A
`pageSize` outside the range the capabilities answer names is clamped rather than refused, and a
cursor that cannot be read answers `sync.cursor_invalid` rather than a validation failure. The
code exists on this surface only on the two set-writing routes and on the upload.

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
already applied returns its stored answer here, before anything else is asked — then the caving-group
binding, and only then the rows. A replayed batch is therefore answered even by an account whose
group membership has since been withdrawn, deliberately: it is being told what already happened, not
being allowed to write again.

### 6.1 The whole batch

| Status | Code | Means | Action |
|---|---|---|---|
| 409 | `sync.contract_unsupported` | The device sent a `contractVersion` this server does not speak. Asked before the rows are looked at, so nothing was written. The server's own version is in `detail` | `stop` — and tell the caver the application needs updating, or the installation does. Retrying cannot help |
| 400 | `sync.batch_too_large` | More rows than `uploadRowsMax` from the capabilities answer | `apply-and-resubmit` — split the batch. **Mint a new batch identifier for each part**; a batch identifier stands for one attempt |
| 404 | `sync.set_not_found` | No sync set with that id belongs to this account | `surface-to-user` — as in section 3 |
| 409 | `sync.batch_conflict` | The same batch identifier arrived twice at once, and the first copy is still being applied | `retry` — after a short wait. The winner's answer is what a later send returns |
| 400 | `validation.failed` | The body failed validation — a row named twice, an empty batch, a property document that is not an object | `stop` — fix the client |
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
| `conflict` | `sync.conflict` | Somebody wrote the row after the device last read it. The device's version was **not** applied | `apply-and-resubmit` — take the server's row from `conflicts`, merge, and send again with the revision that row carries. **A row that lost and is missing from `conflicts` is a row whose position this account may not have** — the same answer a download gives, absence rather than a blurred stand-in. Re-read it instead |
| `rejected` | `sync.row_not_found` | A `baseRevision` was sent for a row that is not on this server | `apply-and-resubmit` — send it as a new row, with `baseRevision` null |
| `rejected` | `sync.id_conflict` | The identifier is in use here by a row this account may not read | `surface-to-user` — do not re-key the row silently. Mint a new identifier only if a human establishes that the row is genuinely a different one |
| `rejected` | `sync.row_deleted` | The row was removed on the server | `apply-and-resubmit` — the ordinary answer is to drop it locally. Sending it again as a new row, under a new identifier, is a caver's decision, not the client's |
| `rejected` | `sync.row_forbidden` | The account may read the row but not write it | `stop` for this row |
| `rejected` | `sync.row_delete_forbidden` | The account may write the row but not remove it. Editing and removing are separate rights here exactly as they are in the web interface, because a delete takes the row's whole containment subtree with it | `stop` for this row |
| `rejected` | `access.create_forbidden` | The account may not create a row of this shape in this place. This is the only non-`sync.` code the row loop mints itself | `stop` for this row |
| `rejected` | `sync.parent_required` | On **a new row**: it named no container, and **this kind** only exists inside one. Whether a kind does is a property of the kind: `cave_area` and `cave_place` do, `surface_area` does not and is written with nothing above it | `apply-and-resubmit` — send the container's identifier. Protection and visibility are inherited along containment and along nothing else, so a row of such a kind with nothing above it would be unguarded whatever guards the cave it belongs to |
| `rejected` | `sync.parent_not_found` | On **a new row**: the named container is not on this server, or is not readable by this account — answered alike, so the field cannot be used to ask whether a cave exists | `apply-and-resubmit` — send the container in the same batch, before the row, or drop the row |
| `rejected` | `sync.parent_forbidden` | On **a new row**: the account may read the container but not add to it | `stop` for this row |
| `rejected` | `sync.location_forbidden` | **A new entrance** was sent for a cave whose position this account may not see exactly. It is refused rather than half-applied, because a cave's own map point is its main entrance's and writing one would move the other | `stop` for this row. The coordinate may be one this server handed the device blurred or not at all, and it must not travel back as truth |
| `rejected` | `sync.geometry_invalid` | The geometry could not be read, or is the wrong shape for the kind — an entrance carries a point | `apply-and-resubmit` after fixing it, if the client can; otherwise `surface-to-user` |
| `rejected` | `sync.type_unknown` | On **a new row**, `caveTypeCode`, `entranceTypeCode` or `featureTypeCode` names a kind this installation does not have. **Codes, never numeric identifiers**: the number standing for "cave" here stands for something else on the next server | `surface-to-user` — an administrator adds the kind, or the caver picks a different one |
| `rejected` | `sync.kind_unsupported` | This `kind` is not carried by sync yet | `stop` for this row, permanently in this contract version |
| `rejected` | *(a write-service code)* | The write service refused this row — a broken containment shape, a property document that fails its schema. The `detail` says which, and section 6.3 names them | `apply-and-resubmit` after fixing it, if the client can; otherwise `surface-to-user` |

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

## 7. The QR landing route

The public QR route is not part of sync and is reached by a scanner, not by a signed-in device, but
it belongs in one catalogue with the rest because the same application calls both.

| Status | Code | Means | Action |
|---|---|---|---|
| 404 | `qr.not_found` | Nothing resolves here. **Four different facts share this one answer on purpose**: the code is malformed, the code is longer than this server will look up, no cave carries it, or the cave that carries it is not published. They are indistinguishable so the address cannot be used to enumerate caves | `surface-to-user` — tell the visitor the code does not resolve at this installation, and say nothing about why |
| 429 | *(none)* | The per-address rate limit. **A problem document with no `code`** — the limiter answers before the route does, so the body is `status`, `title` (`Too Many Requests`) and a `traceId` | `retry` — after a wait. The window is one minute and the allowance is per installation |
| 200 | — | The code resolves and the cave is published. The answer is deliberately contentless: it says the code resolves at this installation and nothing else, so it carries no cave name and no coordinate | — |

## 8. Codes this document deliberately does not carry

Three families a device can meet are documented elsewhere or not at all, and the boundary is stated
so that a gap is not mistaken for an omission.

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

## 9. Where these codes appear in the served description

The server's own description at `/openapi/v1.json` declares, for each sync operation, statuses it
can answer with. The body is always the problem document in section 1, so the declared schema is the
same for all of them; **the `code` is not in the description** and cannot be, because it is a value
rather than a type. This document is the list of codes, and it is written as each one is added
rather than assembled afterwards.

**The declaration is not a complete list of statuses either**, and this document is the authority
where they differ: the delete route declares its `404` and not the `409` described in section 4.
Treat the description as machine-readable orientation and this file as the contract.

Two further practical consequences:

- A generated client will give you a typed problem body with `type`, `title`, `status`, `detail` and
  `instance`. `code` arrives as an extra member on the same object; read it from the raw JSON if
  your generator drops unknown members.
- The sync operations are the only ones on this server with stable names in the description
  (`syncCapabilities`, `syncListSets`, `syncCreateSet`, `syncGetSet`, `syncReplaceSet`,
  `syncDeleteSet`, `syncDownload`, `syncUpload`). Error handling written against those names will
  not move.

## 10. Three refusals recorded as bytes

Prose about a payload drifts from the payload silently, so three of the failures above are also
committed as recorded exchanges under `contract/speleoloc-sync/v1/16-errors/` — the invalid resume
position, the stale one, and a batch pinned to a contract version this server does not speak. Each
carries a `status.txt` with the status line beside the problem body, because the status is what a
client branches on before it has read a field.

They are the three worth having as bytes rather than as description. The two cursor refusals arrive
as different statuses with different codes for what is nonetheless one decision on the device, which
is exactly the kind of distinction prose loses. And the whole-batch refusal is the shape a client is
most likely to get wrong: it carries **no `rows` array at all**, so code that reads per-row results
without first checking the status fails on the one answer an un-updated phone is most likely to
meet.

The correlation identifier a problem document carries is replaced in those recordings. It is a fresh
value on every request, it is for reading a server log with, and nothing on a device should branch
on it.
