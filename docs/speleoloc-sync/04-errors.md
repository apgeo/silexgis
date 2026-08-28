# What a failure looks like, and what to do about it

This document is for whoever writes the client side. It is the list of ways a sync request can fail,
what each one means, and — the part that matters on a phone in a cave — whether retrying the same
request could ever produce a different answer.

It is written as the server is built rather than reconstructed afterwards, because a catalogue
assembled at the end is how codes end up undocumented. **A code that is not here is not one the
sync surface emits today.** Sections marked *not yet emitted* name codes the write direction will
add; they are listed so a client can be written once.

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
| 403 | `sync.caving_group_forbidden` | The account is not a member of the caving group it tried to bind the set to | No |
| 409 | `sync.set_conflict` | Somebody else wrote this set between the read and the write — the settings page and the caver's own phone hold the same set and both post the whole of it | **Yes**: re-read the set and re-send. This is an ordinary race, not a fault |

## 5. Transport failures, which are not codes

These have no `code` because they never reached the application. On a mobile connection they are the
common case, not the exception.

- **A timeout or a dropped connection mid-page.** The download holds no server-side state, so the
  request is repeatable exactly as sent: re-issue it with the same cursor. A page is either received
  whole or not at all.
- **A `502`/`503`/`504` with an HTML body.** Something in front of the server. Back off and retry.
- **A redirect.** Never follow one on a sync route. The API does not redirect; something else did.

## 6. Not yet emitted

The write direction is not built. These codes are named now so a client can be written once and so
that nobody invents a different name for the same condition later:

| Status | Code | Will mean |
|---|---|---|
| 409 | `sync.contract_unsupported` | The device sent a `contractVersion` this server does not speak. The server's own version is in the payload, so the application can say "update" instead of corrupting a dataset |

Until the capabilities answer names `upload` in its `features` list, no route accepts rows and none
of the above can be seen.

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
  `syncDeleteSet`, `syncDownload`). Error handling written against those names will not move.
