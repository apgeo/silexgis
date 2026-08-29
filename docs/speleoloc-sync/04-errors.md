# What a failure looks like, and what to do about it

This document is for whoever writes the client side. It is the list of ways a sync request can fail,
what each one means, and — the part that matters on a phone in a cave — whether retrying the same
request could ever produce a different answer.

It is written as the server is built rather than reconstructed afterwards, because a catalogue
assembled at the end is how codes end up undocumented. **A code that is not here is not one the
sync surface emits today.**

A write fails at two levels — the batch as a whole, and each row inside it — and section 6 is where
that distinction lives. A row's verdict is not an HTTP status: it rides a `200` beside the rows
that were written.

---

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

**A response with no `code` is not from this API.** A proxy, a captive portal or a load balancer
between the device and the server produces its own errors, usually as HTML. Treat a body that does
not parse as a problem document as a transport failure, not as a server verdict.

## 2. Authentication and authorisation

| Status | Code | Means | Retry? |
|---|---|---|---|
| 401 | *(none)* | No token, an expired token, or a token this server did not issue. **Every route in this slice answers this** — none of them is anonymous | After refreshing the credential. Never with the same token |
| 403 | `acl.forbidden` | The account is authenticated but not entitled to what it asked for | No. Nothing the device can do changes it |

A `401` on `GET /api/v1/sync/capabilities` is the cheapest way to find out a stored credential has
lapsed, which is one reason to ask it first. Refreshing is described in the sign-in document.

**There is no `403` for a sync set somebody else owns.** It is a `404` — see the next section.

## 3. Reading

| Status | Code | Means | Retry? |
|---|---|---|---|
| 404 | `sync.set_not_found` | No sync set with that id belongs to this account. **A set that exists but belongs to somebody else answers exactly this**, so the route cannot be used to count other people's devices | No, unless the set is re-created |
| 400 | `sync.cursor_invalid` | The `cursor` was not issued by this server: truncated, re-encoded, hand-made, or from an older contract | Not as sent. Drop the cursor and start the set again from the beginning — accepting that this is a full re-read |
| 409 | `sync.cursor_stale` | The `cursor` was issued by this server, but against an older revision of the sync set: the selection or its settings have been edited since | Not as sent. Drop the cursor and read the set from the beginning. Retrying the same cursor will never succeed |
| 400 | `validation.failed` | The request body failed validation. Carries an `errors` object keyed by field | Only after fixing the request |

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

| Status | Code | Means | Retry? |
|---|---|---|---|
| 400 | `sync.root_not_found` | A named root feature does not exist **or is not readable by this account** — the two are answered alike on purpose, so the route cannot be used to ask whether a cave exists | No, unless the selection changes |
| 400 | `sync.caving_group_not_found` | The named caving group does not exist | No |
| 403 | `sync.caving_group_forbidden` | The account is not entitled to the caving group named — when writing a set, because it is not a member; when uploading, because it is no longer entitled to bind rows to the group the set carries. The question is asked again at every upload rather than only when the set was written, because that is when the binding is actually made | No, until the membership is restored |
| 400 | `sync.set_revision_required` | A replacement stated no `baseRevision`. A write here replaces the whole set — the selection and the settings document together — so it is arbitrated on the revision the caller last read, exactly as an uploaded row is | Re-read the set and send its `revision` back |
| 409 | `sync.set_conflict` | The `baseRevision` sent is not the set's current revision — somebody else wrote it since this caller last read it. The settings page and the caver's own phone hold the same set and both post the whole of it, so this is an ordinary race and not a fault | **Yes**: re-read the set, re-apply the change, and re-send with the revision that read returned |

## 5. Transport failures, which are not codes

These have no `code` because they never reached the application. On a mobile connection they are the
common case, not the exception.

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

### 6.1 The whole batch

| Status | Code | Means | Retry? |
|---|---|---|---|
| 409 | `sync.contract_unsupported` | The device sent a `contractVersion` this server does not speak. Asked before the rows are looked at, so nothing was written. The server's own version is in the message | No. Update the application; retrying cannot help |
| 400 | `sync.batch_too_large` | More rows than `uploadRowsMax` from the capabilities answer | Not as sent. Split the batch. **Mint a new batch identifier for each part** — a batch identifier stands for one attempt |
| 404 | `sync.set_not_found` | No sync set with that id belongs to this account | No, unless the set is re-created |
| 409 | `sync.batch_conflict` | The same batch identifier arrived twice at once, and the first copy is still being applied | **Yes**, after a short wait. The winner's answer is what a later send returns |
| 400 | `validation.failed` | The body failed validation — a row named twice, an empty batch, a property document that is not an object | Only after fixing the request |
| 403 | `sync.caving_group_forbidden` | The set is bound to a caving group this account is no longer entitled to bind rows to. Nothing was written | No, until the membership is restored |
| 400 | *(a write-service code)* | The write service refused something the loop could not attribute to one row. Nothing is kept | No, not as sent |

### 6.2 One row inside a `200`

Each entry in `rows` carries a `status`, and a `code` when there is a reason worth naming. Only
`rejected` and `conflict` carry one.

| `status` | `code` | Means | What the device does |
|---|---|---|---|
| `created` | — | The row was not here and now is, under the identifier the device gave it | Store the `revision` as the row's base revision |
| `updated` | — | The row was here, the device held the current version, its edit was written | Store the `revision` |
| `deleted` | — | The row was removed | Drop it locally |
| `unchanged` | — | Nothing was written and nothing was wrong: the row already said what was asked. A resent create whose identifier is already here lands here, and so does a delete of a row that was never received | Store the `revision` if one came back |
| `conflict` | `sync.conflict` | Somebody wrote the row after the device last read it. The device's version was **not** applied | Take the server's row from `conflicts`, merge, and send again with the revision that row carries. **A row that lost and is missing from `conflicts` is a row whose position this account may not have** — the same answer a download gives, absence rather than a blurred stand-in. Re-read it instead |
| `rejected` | `sync.row_not_found` | A `baseRevision` was sent for a row that is not on this server | Send it as a new row, with `baseRevision` null |
| `rejected` | `sync.id_conflict` | The identifier is in use here by a row this account may not read | No. Do not re-key the row: mint a new identifier only if the row is genuinely a different one |
| `rejected` | `sync.row_deleted` | The row was removed on the server | Drop it locally |
| `rejected` | `sync.row_forbidden` | The account may read the row but not write it | No |
| `rejected` | `sync.row_delete_forbidden` | The account may write the row but not remove it. Editing and removing are separate rights here exactly as they are in the web interface, because a delete takes the row's whole containment subtree with it | No |
| `rejected` | `access.create_forbidden` | The account may not create a row of this shape in this place. Not `acl.forbidden`, which is the whole-request 403 above and never appears inside `rows` | No |
| `rejected` | `sync.parent_required` | The row named no container, and **this kind** only exists inside one. Whether a kind does is a property of the kind: `cave_area` and `cave_place` do, `surface_area` does not and is written with nothing above it | Send the container's identifier. Protection and visibility are inherited along containment and along nothing else, so a row of such a kind with nothing above it would be unguarded whatever guards the cave it belongs to |
| `rejected` | `sync.parent_not_found` | The named container is not on this server, or is not readable by this account — answered alike, so the field cannot be used to ask whether a cave exists | Send the container in the same batch, before the row, or drop the row |
| `rejected` | `sync.parent_forbidden` | The account may read the container but not add to it | No |
| `rejected` | `sync.location_forbidden` | An entrance was sent for a cave whose position this account may not see exactly. It is refused rather than half-applied, because a cave's own map point is its main entrance's and writing one would move the other | No. The coordinate may be one this server handed the device blurred or not at all, and it must not travel back as truth |
| `rejected` | `sync.geometry_invalid` | The geometry could not be read, or is the wrong shape for the kind — an entrance carries a point | Only after fixing it |
| `rejected` | `sync.type_unknown` | `caveTypeCode`, `entranceTypeCode` or `featureTypeCode` names a kind this installation does not have. **Codes, never numeric identifiers**: the number standing for "cave" here stands for something else on the next server | No, unless the installation adds the kind |
| `rejected` | `sync.kind_unsupported` | This `kind` is not carried by sync yet | No |
| `rejected` | *(a write-service code)* | The write service refused this row — a broken containment shape, a property document that fails its schema. The `detail` says which | Only after fixing it |

**A field the device sent that is silently not applied is not an error.** An account that may write
a row but may not see its position exactly keeps its name and description edits and loses only the
geometry, the altitude and the position quality — the row comes back `updated`, and it is
`updated` honestly. The alternative is losing a caver's rename to protect a coordinate that was
never going to move.

## 7. Where these codes appear in the served description

The server's own description at `/openapi/v1.json` declares, for each sync operation, which of the
statuses above it can answer with. The body is always the problem document in section 1, so the
declared schema is the same for all of them; **the `code` is not in the description** and cannot be,
because it is a value rather than a type. This document is the list of codes, and it is written as
each one is added rather than assembled afterwards.

Two practical consequences:

- A generated client will give you a typed problem body with `type`, `title`, `status`, `detail` and
  `instance`. `code` arrives as an extra member on the same object; read it from the raw JSON if
  your generator drops unknown members.
- The sync operations are the only ones on this server with stable names in the description
  (`syncCapabilities`, `syncListSets`, `syncCreateSet`, `syncGetSet`, `syncReplaceSet`,
  `syncDeleteSet`, `syncDownload`, `syncUpload`). Error handling written against those names will
  not move.
