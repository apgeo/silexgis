# Signing a device in to a SilexGIS installation

This document is for whoever writes the client side. It describes how an installed application
obtains a token from a SilexGIS server and keeps it, with the real traffic pasted in.

**The examples are transcripts, not illustrations.** Every request and response below was recorded
by the server's own integration suite while it played the flow end to end, and was copied out of the
files that run produced. Long opaque values — tokens, cookies — are cut short with `…`; nothing else
is edited. Know what that does and does not guarantee: the facts the prose states — the status
codes, the form encoding, that redirects must not be followed, that `offline_access` is what yields
a refresh token, the shape of the two-factor response — are each asserted by that suite, so a server
that stopped doing them fails a test. The pasted bodies themselves are a snapshot of one run and are
compared with nothing, so a field added to a response, or a `Set-Cookie` renamed, would reach this
page only when somebody re-records it. Read the bodies for shape, not as a schema.

A SilexGIS installation is a private server that a caving group runs for itself. There is no
sign-up, no public data and no anonymous read of anything this document reaches: a device acts as a
signed-in account and sees exactly what that account would see in the web interface, no more.

---

## 1. What the server expects

| | |
|---|---|
| Client id | `silexgis-speleoloc` |
| Client secret | none — this is a public client, and a secret shipped inside an installed app is not a secret |
| Grants accepted | `authorization_code` and `refresh_token`, and no others |
| PKCE | **required**, `S256` only |
| Redirect URIs registered | `http://127.0.0.1/callback` (any port) and `speleoloc://auth` |
| Scopes to request | `openid profile email roles offline_access` |
| Access token | JSON web token, 15 minutes, sent as `Authorization: Bearer …` |
| Refresh token | 45 days by default, per installation, sliding — see §4 |

Both redirect forms are registered on every installation from the first start, so a client build may
use either without the server being touched. The loopback entry carries **no port**: an installed
application binds an ephemeral one at the moment it starts a sign-in, and the port is excluded from
the comparison. `http://127.0.0.1:54321/callback` and `http://127.0.0.1:8917/callback` both match.

The endpoints:

| Purpose | Route |
|---|---|
| Password login (yields a session cookie) | `POST /api/v1/auth/login` |
| Authorization request | `GET /connect/authorize` |
| Token endpoint (exchange and refresh) | `POST /connect/token` |
| Metadata | `GET /.well-known/openid-configuration` |

---

## 2. The flow, as recorded

Five steps: log in for a cookie, ask for a code, exchange the code for tokens, call the API, refresh
before the access token lapses.

### 2.1 Password login

The authorization endpoint consumes a session cookie, and this is the only thing that produces one.

```
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "someone@example.org",
  "password": "…"
}

--> 200 OK
Set-Cookie: silexgis.session=CfDJ8NvV1s5YgPJBoqqGChLgSfEkYoWehGqdITXpgrCy3arXEtzZ3rMiPRPIxP8wqcVc…

{
  "userId": "01a04471-869b-7d2e-8ed2-b1982a7e7d35",
  "email": "someone@example.org",
  "displayName": null
}
```

The cookie must be carried into the next request. If the account has two-factor sign-in turned on,
this step answers `401` instead — see §3.4.

### 2.2 The authorization request

Generate a PKCE verifier (43–128 random URL-safe characters) and its `S256` challenge — the
base64url encoding of the SHA-256 of the verifier's ASCII bytes. Keep the verifier; it is what
proves at step 2.3 that the code is being redeemed by whoever asked for it.

```
GET /connect/authorize
      ?client_id=silexgis-speleoloc
      &redirect_uri=http%3A%2F%2F127.0.0.1%3A54321%2Fcallback
      &response_type=code
      &scope=openid%20profile%20email%20roles%20offline_access
      &code_challenge=BEXDGLc8aJSz9P_yqoAiBXfx65WhbVmaWWokKbMCKGY
      &code_challenge_method=S256
      &state=device-state
Cookie: silexgis.session=…

--> 302 Found
Location: http://127.0.0.1:54321/callback
            ?code=7ZWvekTWrSlw9eG7GTGGgsxJUlBldItI72hr1RyiYKk
            &state=device-state
            &iss=http%3A%2F%2Flocalhost%2F
```

The response has **no body**. The code exists only in that header. Check `state` against what was
sent before going on.

### 2.3 The code exchange

A form body, not JSON, and no client secret.

```
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code
&code=7ZWvekTWrSlw9eG7GTGGgsxJUlBldItI72hr1RyiYKk
&redirect_uri=http%3A%2F%2F127.0.0.1%3A54321%2Fcallback
&client_id=silexgis-speleoloc
&code_verifier=6cazsqv4ong37ex0AooYjW_XVjhBLSuzXudPVeVs0o8inLYr9nu1UPnrtnxYqk0w

--> 200 OK

{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6Ijk4RTI4OEMzOUZFNTY4NjdGMUQ4QjE4OUFDMUFCNDQ…",
  "token_type": "Bearer",
  "expires_in": 900,
  "scope": "openid profile email roles offline_access",
  "id_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6Ijk4RTI4OEMzOUZFNTY4NjdGMUQ4QjE4OUFDMUFCNDQ…",
  "refresh_token": "eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6IkEyNTZDQkMtSFM1MTIiLCJraWQiOiI5N0Iy…"
}
```

`redirect_uri` must be byte-identical to the one sent at 2.2, port included.

### 2.4 Calling the API

```
GET /api/v1/me/
Authorization: Bearer eyJhbGciOiJSUzI1NiIsImtpZCI6…

--> 200 OK

{
  "id": "01a04471-869b-7d2e-8ed2-b1982a7e7d35",
  "userName": "someone@example.org",
  "email": "someone@example.org",
  "emailConfirmed": true,
  "locale": "en",
  "visibility": {"realName":"private","bio":"private","email":"private", …},
  "addresses": [],
  "createdAt": "2026-08-27T18:18:05.934786+00:00"
}
```

`GET /api/v1/me/` is the cheapest way to confirm a token works and to learn who it belongs to. Every
route takes the same header; nothing about a bearer-authenticated request differs from the web
interface's, and the account sees exactly what it would see there.

### 2.5 Refresh

```
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&refresh_token=eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6IkEyNTZDQkMtSFM1MTIi…
&client_id=silexgis-speleoloc

--> 200 OK

{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6Ijk4RTI4OEMzOUZFNTY4NjdGMUQ4QjE4OUFDMUFCNDQ…",
  "token_type": "Bearer",
  "expires_in": 899,
  "scope": "openid profile email roles offline_access",
  "id_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6Ijk4RTI4OEMzOUZFNTY4NjdGMUQ4QjE4OUFDMUFCNDQ…",
  "refresh_token": "eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6IkEyNTZDQkMtSFM1MTIiLCJraWQiOiI5N0Iy…"
}
```

**The refresh token in the answer is a new one and the old one is spent.** Store what comes back
before doing anything else with it. Discarding the response after reading the access token out of it
leaves the client holding a token that has already been redeemed — which the next section explains
is not a harmless mistake.

---

## 3. Five things a client gets wrong, and the traffic that shows why

None of these are visible in the generated API description: it documents the JSON surface and not
the OAuth endpoints, which publish neither a request body nor a response shape there.

### 3.1 Without `offline_access` there is no refresh token, and nothing says so

The exchange succeeds, returns an access token, and looks entirely healthy. It simply has no
`refresh_token` field. A client that never asked would conclude the server does not issue refresh
tokens at all and would send the user back to a password prompt every fifteen minutes.

Asking for `openid profile email roles`:

```
--> 200 OK
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6…",
  "token_type": "Bearer",
  "expires_in": 899,
  "scope": "openid profile email roles",
  "id_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6…"
}
```

The same exchange, the same account, one scope added:

```
--> 200 OK
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6…",
  "token_type": "Bearer",
  "expires_in": 899,
  "scope": "openid profile email roles offline_access",
  "id_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6…",
  "refresh_token": "eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6…"
}
```

Read the `scope` field of the answer: it is the server's statement of what was actually granted, and
comparing it to what was asked for is how a client notices this rather than debugging it.

### 3.2 The HTTP stack must not follow redirects

The authorization code arrives in a `302`'s `Location` header, and the response body is empty. Most
HTTP clients follow redirects by default; one that does will issue a request to the callback address
and hand the code to whatever answers it — or to nothing — and will never see the header. Drive that
redirect manually: ask for the `302`, read `Location`, parse `code` and `state` out of its query.

In Dart's `package:http` this means `Request(...)..followRedirects = false` sent through
`Client.send`, rather than the convenience `get`.

### 3.3 `POST /connect/token` is form-encoded

Every other route on this server takes JSON. This one does not, and refuses a JSON body outright:

```
POST /connect/token
Content-Type: application/json

{ "grant_type": "authorization_code", "code": "-p0q4aL1Tnjqybm3xF3Y3diRmANYOzWf8eWDJ-0DhS4", … }

--> 400 BadRequest

{
  "error": "invalid_request",
  "error_description": "The specified 'Content-Type' header is invalid.",
  "error_uri": "https://documentation.openiddict.com/errors/ID2082"
}
```

The identical values as `application/x-www-form-urlencoded`, with the same unspent code, return
`200`. Note the error shape: the token endpoint answers with `error` / `error_description` /
`error_uri`, not with the Problem Details body the rest of the API uses.

### 3.4 The two-factor refusal carries three fields, not two

When the account has two-factor sign-in on, the login step answers `401` with a body that names both
what would be accepted and — the field most likely to be missed — that recovery codes always will
be:

```
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "someone@example.org", "password": "…" }

--> 401 Unauthorized
Set-Cookie: Identity.TwoFactorUserId=CfDJ8NvV1s5YgPJBoqqGChLgSfGtU_OOPzzickWsATX3Oj0Uywr9CkgC…

{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Unauthorized",
  "status": 401,
  "detail": "A two-factor code is required.",
  "methods": ["authenticator"],
  "preferredMethod": "authenticator",
  "recoveryAccepted": true,
  "code": "auth.mfa_required",
  "traceId": "00-ae482e49bc8b75415e123c5e6814ebde-8b1a296096d91b5d-00"
}
```

`methods` lists what this account can currently use — `authenticator`, `email`, `sms` — and
`preferredMethod` is the one to offer first. **`recoveryAccepted` is always `true`, and a client
that offers only the listed methods can strand somebody with no way in**: every method can be taken
away by a person other than the one signing in — they turn one off, an administrator disallows it, a
channel stops working. Recovery codes are what stops that from being a lockout.

Retry the same request with the code added. A recovery code is sent in the same field with a
`recovery:` prefix.

```
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "someone@example.org", "password": "…", "twoFactorCode": "136778" }

--> 200 OK
Set-Cookie: silexgis.session=CfDJ8NvV1s5YgPJBoqqGChLgSfGWIWUt…
```

From there the flow is the ordinary one: the two-factor gate is on the login step alone and changes
nothing about the authorization request or the exchange.

**A code that has to be *sent* is asked for on a separate call.** `authenticator` is read from the
caver's own application and needs nothing; `email` and `sms` do not arrive on their own. Ask for one
with the half-finished sign-in's cookie — the one the `401` above set — and no account named in the
body, because naming one would let this call be used to discover addresses or to send mail to
somebody else's:

```
POST /api/v1/auth/2fa/send
Content-Type: application/json
Cookie: Identity.TwoFactorUserId=…

{ "method": "email" }

--> 200 OK
{ "method": "email", "destination": "s•••@example.org", "expiresMinutes": 10 }
```

Then send the login again with `twoFactorCode` set, exactly as above. The call is rate-limited per
address like the rest of `/auth/*`, and it is throttled a second time per account and method, so a
caver hammering "resend" is refused by the second limit long before the first.

### 3.5 A spent refresh token must not be presented twice

Refresh tokens roll: each refresh redeems the one presented and issues a new one. Presenting a
redeemed token again is treated as evidence that a copy of it is loose, and **the whole chain is
revoked** — the successor the client is holding stops working too, and the only way back is a
password login.

There is a deliberate grace period of about thirty seconds after a redemption during which the spent
token is still accepted, so that a client whose connection dropped mid-refresh can retry the same
request without losing its session. That window exists for a retry, not as a licence to keep using
a token after a successful refresh. Refresh from one place in the client, serialise it against other
requests, and persist the new pair before continuing.

---

## 4. Lifetimes, and the key that sets them

| Token | Lifetime | Set by |
|---|---|---|
| Access token | 15 minutes | server-wide, not configurable per installation |
| Refresh token (`silexgis-speleoloc`) | **45 days**, sliding | `SILEXGIS__Auth__SpeleoLocRefreshTokenDays` |
| Refresh token (web interface) | 30 days | server-wide |

**Sliding means measured from the last successful refresh, not from sign-in.** A phone that syncs
once a month never has to sign in again; a phone left dormant for 46 days does. The distinction
matters when deciding how often the client refreshes: a background refresh on any successful sync is
enough, and there is no fixed ceiling counting down from the day a caver typed their password.

`SILEXGIS__Auth__SpeleoLocRefreshTokenDays` is an installation's choice and defaults to `45`. It is
**clamped to 1–365**: a value outside that range is brought back inside rather than refused, so a
slipped digit cannot mint a credential lasting years and a zero cannot mean "expires immediately".
A client must therefore treat 45 days as a default, never as a guarantee, and must not assume any
particular window from the value it saw last time.

**A lapsed refresh token means signing in again. It never means data loss.** The local database
remains the source of truth for everything the device holds; a session that has run out is a reason
to ask for a password, not a reason to discard or re-download anything.

---

## 5. Failures a client can meet

### At `POST /api/v1/auth/login`

| Status | `code` | What it means, and what the client should do |
|---|---|---|
| `401` | `auth.invalid_credentials` | Wrong email or password. Ask again. |
| `401` | `auth.mfa_required` | §3.4. Prompt for a code, and always offer the recovery-code path. |
| `401` | `auth.mfa_invalid` | The code was wrong or has expired. Ask again; a new authenticator code appears every thirty seconds. |
| `401` | `auth.locked_out` | Too many failed attempts. The lock lifts by itself after a few minutes — say so, and do not retry in a loop. |
| `401` | `auth.email_not_confirmed` | The installation requires a confirmed address and this one is not. Only the account holder can fix it, from their mailbox. |
| `429` | — | Too many requests from this address in a minute. Back off; the window is one minute. This limit is per installation and can be tightened by an operator, so a client that retries hard can lock itself out of a shared connection. |

### At `POST /api/v1/auth/2fa/send`

| Status | `code` | What it means, and what the client should do |
|---|---|---|
| `401` | — | The half-finished sign-in has expired or its cookie was not sent. Start again from the password. |
| `400` | `auth.mfa_method_not_delivered` | `authenticator` was asked for. Nothing is sent for it — read the code from the caver's application. A client that offers a "send code" button for every method meets this one. |
| `400` | `auth.mfa_method_unavailable` | The method is not one this account can currently use — turned off, disallowed by the installation, or its channel is not configured. Offer what the `401` listed in `methods`, and always the recovery path. |
| `400` | `auth.mfa_resend_too_soon` | A code for this method was sent a moment ago. Disable the button for a while rather than retrying; the throttle is per account, so retrying from another address does not help. |
| `400` | `auth.mfa_send_failed` | The installation could not deliver it — mail or SMS is misconfigured or refused it. Offer another method, and the recovery path. Only an administrator can fix the channel. |
| `429` | — | Too many requests from this address in a minute. Back off. |

### At `GET /connect/authorize`

| Response | What it means |
|---|---|
| `302` to the callback with `code` | Success. |
| `302` to a path containing `/login` | **There is no session.** The cookie was missing, or it expired. Go back to §2.1. Note this is not an error status — a client that checks only the status code will parse the sign-in page's URL as a callback and find no `code` in it. |
| `302` to the callback with `error` | The request itself was rejected — usually an unregistered `redirect_uri`, a missing challenge, or a `client_id` this installation has not seeded. Surface `error_description`. |

### At `POST /connect/token`

The body is always `{"error", "error_description", "error_uri"}`.

| Status | `error` | What it means |
|---|---|---|
| `400` | `invalid_request` | Malformed — most often a JSON body (§3.3) or a missing parameter. |
| `400` | `invalid_grant` | The code or refresh token was rejected: spent, expired, replayed (§3.5), issued to a different client, or redeemed with a `redirect_uri` or `code_verifier` that does not match. **Also what a client meets after the account's password was changed or reset**, which revokes every refresh token and authorization that account holds, on every device, deliberately — it is the action somebody takes when a phone has been lost. Ask for a password. |
| `400` | `invalid_grant` with "the account's email address is not confirmed" | The account's state changed after the token was issued. An administrator can turn on confirmed-email enforcement at any time, and refreshes then start failing for an account that had been working. There is no client-side symptom and nothing the client can do: **show the server's `error_description` verbatim** rather than inventing a diagnosis, or the resulting support call has nothing to go on. |
| `400` | `unsupported_grant_type` | Only `authorization_code` and `refresh_token` exist. There is no password grant, no client-credentials grant and no device-code grant on this server, and asking for one is not a configuration problem to be solved at the far end. |

### On an API call

| Status | What it means |
|---|---|
| `401` | The access token has expired — they last 15 minutes. Refresh once, then retry the request. If the refresh also fails, sign in again — do not loop. Note that revoking a session does **not** produce this immediately: an access token is a signed document checked against its signature rather than looked up in a table, so one already issued keeps returning `200` until it expires. What revocation stops is the *next* one (§6). |
| `403` | The token is valid and the account is not allowed to do this. Not a credential problem; refreshing changes nothing. |

---

## 6. What ends a session

Worth knowing because none of it is visible from the device:

- **The refresh token lapses** — 45 days by default since the last successful refresh.
- **The account's password is changed or reset.** Every refresh token and every authorization that
  account holds is revoked, on every device, and the browser session that could authorize a fresh
  one is refused from that moment. This is deliberate and account-wide rather than confined to the
  device that acted: somebody whose phone has been taken cannot enumerate their sessions and cannot
  name the one to end, so the action they can reach has to end all of them. **One residual window,
  stated rather than glossed:** an access token already in the lost device's hands is not recalled —
  it is validated from its signature, not against the server's store — so it keeps working until it
  expires, at most 15 minutes after it was issued. Nothing can be minted after that.
- **A redeemed refresh token is presented outside the retry window** (§3.5) — the chain is revoked.
- **The account is deleted, or confirmed-email enforcement is turned on while the address is
  unconfirmed.**

There is no server-side session list and no per-device sign-out for an account holder to use today.
Signing out on the device is a local act: discard the tokens.
