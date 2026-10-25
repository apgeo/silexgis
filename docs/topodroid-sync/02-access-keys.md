# Signing in: personal access keys

A survey tool on a phone does not sign in with the caver's password and does not run an OAuth
flow. The caver makes a **personal access key** on the SilexGIS web site, copies it once, and
pastes it into the tool's settings. The tool sends it on every call:

```
Authorization: Bearer sgx_3Jm9…
```

That is the whole of signing in. There is no token exchange, no refresh, no browser hand-off, and
nothing in the tool that needs a library.

---

## 1. What a key is

- The text is `sgx_` followed by 43 characters of base64url — 32 random bytes, 47 characters in all.
  The `sgx_` marker is how the server tells a key from a sign-in token, so it must be sent exactly as
  copied.
- The server keeps only a SHA-256 of the whole key and its first 12 characters (the **prefix**, shown
  in lists so the caver can tell their keys apart). It cannot show a key again: a lost key is revoked
  and a new one made.
- Every key has a **purpose**. The only purpose today is *survey upload*, and a key opens **only the
  routes that declare that purpose** (see "What a key opens"). A pasted key is therefore never a credential for the whole
  account: it cannot change a password, read the caver's messages, make another key or administer
  anything.
- Every key **expires**. The web form offers 30, 90 and 365 days (365 by default); the installation
  caps the lifetime with `SILEXGIS__Auth__AccessKeyMaxDays` (default 365). There is no key that never
  expires.

## 2. When a key stops working

A key is looked up on **every request**, so each of these takes effect on the very next call — there
is no grace period:

| What happened | Effect |
|---|---|
| The caver revoked the key on the web | refused |
| The key reached its expiry date | refused |
| The caver changed or reset their password | **every** key of the account is revoked |
| An administrator reset the account's two-factor sign-in | every key of the account is revoked |
| An administrator locked (disabled) the account | refused while the lock lasts |
| The account was deleted | refused |

A refused key is answered exactly like any invalid bearer value: **`401`** with code
`auth.unauthenticated` (recorded in `contract/topodroid-sync/v1/11-errors/unauthenticated/`). The
answer does not say which of the reasons above applied. A tool that gets a 401 should open its
settings and say *"The access key was refused — paste a new one."*

## 3. What a key opens

These routes accept a survey-upload key. The list is pinned by a test that walks the server's
routing table, so a route cannot start or stop accepting a key without that test changing.

| Route | Why a device needs it |
|---|---|
| `GET /api/v1/me` | show which account the key acts as; the tool's *Test* button |
| `GET /api/v1/caves` | search for the cave a survey belongs to |
| `GET /api/v1/survey-uploads/capabilities` | what the server accepts, and whether it compiles |
| `GET /api/v1/survey-uploads` | list uploads the account may import |
| `POST /api/v1/survey-uploads` | open an upload |
| `GET /api/v1/survey-uploads/{id}` | follow a run |
| `PUT /api/v1/survey-uploads/{id}/files/{name}` | send one file |
| `POST /api/v1/survey-uploads/{id}/complete` | say every file is in; queues the run |
| `POST /api/v1/survey-uploads/{id}/rerun` | run a finished upload again |
| `DELETE /api/v1/survey-uploads/{id}` | give up an upload still receiving files |

**Every other route that needs an account answers `403 auth.access_key_not_accepted`** to a key
(recorded in `11-errors/access-key-not-accepted/`). That is a different answer from a 401 on
purpose: the key is good, it is just not for that.

Routes that need no account at all — `GET /api/v1/about`, and the signed download addresses the
upload routes hand out (`/api/v1/files/{id}/content?token=…`) — are served as they are to anybody;
a key sent along with them is ignored rather than refused, so a tool may send its header on every
call without special cases.

## 4. Making and revoking keys (the web's side)

The routes below are for the SilexGIS web site and are **refused to a key** — a key cannot make,
list or revoke keys. They are described so that a tool's documentation can tell the caver where to
go.

| Route | |
|---|---|
| `GET /api/v1/me/access-keys` | the account's keys (name, prefix, purpose, created, last used, expires, revoked, state) and the lifetimes the form offers (`maxDays`, `offeredDays`, `defaultDays`) |
| `POST /api/v1/me/access-keys` `{ "name": "TopoDroid Pixel 7", "expiresInDays": 365 }` | makes a key; the answer's `secret` is the key text, **shown this once** |
| `DELETE /api/v1/me/access-keys/{id}` | revokes it; repeating it is harmless |

A name is at most 100 characters. A lifetime over the installation's ceiling is refused with
`400 access_key.lifetime_invalid`; another account's key answers `404 access_key.not_found`.

On the web this is *Settings › Access keys*.

## 5. Keeping a key safe in the tool

- Store it as a password: a password-type field, never printed in a log line, a crash report or a
  settings export.
- Show only its first 12 characters once saved, as the web does.
- *Forget* on the tool clears it locally; only the web revokes it. Tell the caver so.
- Send it only over HTTPS to the installation the caver typed. A key is a bearer credential: whoever
  holds it may upload surveys as its owner until it expires or is revoked.
