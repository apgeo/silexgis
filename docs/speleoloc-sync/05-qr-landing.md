# A code printed on a cave label, and what happens when somebody scans it

This document is for whoever writes the client side. It describes the one address a printed code
resolves to on a SilexGIS installation, what that address answers, what has to be true on the
device for the existing scanner to reach it unchanged, and the one change the label generator does
need before any of it is reachable from a printed square.

It is a separate document from the sync protocol on purpose. **This is not part of sync.** Nothing
here requires a device to have synced with anything, no account is involved at either end, and a
device that never talks to a SilexGIS installation is unaffected by every word of it.

---

## 1. The address

```
https://<installation>/q/<code>
```

`<code>` is the code as printed: either the QR code resource identifier (QCRI) or the place code
identifier (PCI). Both are matched, and both are matched without regard to case — which mirrors
what the device does when it reads one back, and for the same reasons: a hashed QCRI is lowercase
by construction, but a PCI carries whatever case somebody typed, and a QR reader may report a
different case than the one stored.

A trailing slash is fine. Leading and trailing whitespace is trimmed. Anything over 64 characters
is not a code and is answered as an unknown one is.

**This is a path route, not a hash route.** See §4 — a code carried in a `#` fragment is discarded
by the scanner before anything could resolve it, so the fragment form must never be printed.

**The address above is the page a person opens.** The SPA serves it and asks the API behind it:

```
GET /api/v1/public/qr/<code>
```

That is the call to make from anything that is not a browser. It is anonymous — no bearer token, and
sending one changes nothing — and it is the only route in this package that carries cave data and is.
The sign-in routes are anonymous too, by nature: `POST /api/v1/auth/login`, `GET /connect/authorize`,
`POST /connect/token` and `POST /api/v1/auth/2fa/send`, all described in `03-auth.md`. Everything
else this package documents requires a bearer token. The two answers in
§2 are its answers; the page is a rendering of them.

**There is a third response, and it carries no `code`**: `429`, from the rate limiter described in
§3, which answers before the route does. It is still a problem document — `application/problem+json`,
carrying `status`, `title` (`Too Many Requests`) and a `traceId` — but nothing mints a code for it,
so a client that branches on `code` must branch on the status here instead. Back off and retry; the
window is a minute.

## 2. What it answers

Two answers, and only two.

**It resolves.** `200`, and a body carrying **one field**: the name this installation calls itself.

```json
{ "instanceName": "Clubul Speo Example" }
```

Opened in a browser — which is what a camera app does with it — the visitor gets a rendered page
saying the code is registered with that installation, and nothing else.

**It does not resolve.** `404`, `application/problem+json`, `"code": "qr.not_found"`.

```json
{ "type": "…", "title": "Not Found", "status": 404, "code": "qr.not_found" }
```

### The four causes and the one answer

A code that is not a code, a code nobody ever issued, a code on a cave nobody published, and a code
on a cave somebody published and then withdrew are four different facts about the world and **one
answer here, byte for byte**. There is no cheap timing difference between them either.

This is not fastidiousness. Telling them apart is telling somebody which codes exist and which
caves an installation holds, and the address is guessable — see §3.

**Do not write a client that branches on why.** There is nothing to branch on, and if a later
version of this route ever gave you something to branch on it would be a defect in that version.

### What is deliberately absent

There is **no name field and no coordinate field** in the response type, and their absence is
structural rather than filtered. The route does not obtain a position and blank it; there is no
field to put one in. Two consequences for the client side:

- Do not write code that reads a name or a position off this response "when present". It will never
  be present, and code that anticipates it is an argument for adding it.
- Do not treat a `200` as a statement that any particular cave or place exists. It says the code is
  registered here. That is all it says.

### Two matches answer as one

Nothing anywhere constrains a code to be unique. Two datasets syncing into one installation can
legally land the same code on two features, and no index forbids it. When that happens the route
answers exactly as it would for one match, because both matches produce the same answer: the code
resolves, and this is the installation.

> **This is a precondition, not an observation.** It is harmless only while the answer names
> nothing. The day this route's answer names the thing it found, a second match stops being a
> duplicate and becomes an ambiguity that has to be resolved before anything can be shown — and at
> the same instant, sweeping the code space stops yielding one integer and starts yielding a roster.
> Both are consequences of one content decision.

## 3. Why the address being guessable is not a hole

Every other anonymous address on a SilexGIS installation is opened by a high-entropy token, where
holding the token is the whole claim. **A printed code is not that.** It is short, meant to be
transcribed by a human, and reproducible outside the installation by anybody holding the same place
code and settings. So:

**A code is an address, never a capability.** Treat it as public — it is printed on a wall.

The route is rate-limited (`SILEXGIS__Qr__RateLimitPerMinute`, default 60, per address, its own
window and deliberately not the sign-in one). **That limiter is abuse and cost control, not a
confidentiality control, and no client-side or server-side document should present it as one.** At
60 a minute a three-character mirror-mode space is exhausted from one address in well under an
hour, and from a /24 in minutes. Any argument that leans on the limiter is refuted by the first
arithmetic anybody does.

What makes exhausting the space pointless is that there is nothing here worth enumerating for. A
sweep of every possible code learns **one number** — how many codes this installation has published
— and nothing whatever that ties any one of them to a cave, a place, a position or a person. That
is a property of the response's field list and it lasts exactly as long as the field list does.

**Publication is a deliberate, revocable act, and it is off for every cave that has ever existed.**
An absent decision means not published; there was no backfill and there never will be one. Somebody
with the right to share a cave publishes it, and can stop. Withdrawal is immediate: labels already
bolted to the wall answer nothing from that moment.

## 4. The three conditions under which today's scanner needs no change

The device's scanner already turns `https://host/q/<code>` into `<code>` with no configuration
change, no new delimiter and no `sp://` deep-link prefix. It does so by taking the text after the
**last** of its strip delimiters (`/` and `=` by default) within the URL's **path and query**, and
it trims trailing delimiters first, so `/q/<code>/` works too. Three things must hold, and only the
third is enforced by anything:

**(1) The code contains no `/` and no `=` — and neither does the hierarchical strategy's
user-configurable segment separator.** The split is on the rightmost delimiter, so either character
*inside* a code silently truncates it. For hash-mode QCRIs this is free: the output alphabet is
`[0-9a-z]` and contains neither. **For mirror mode it is not free** — the code is then the PCI
verbatim, and the PCI is assembled from segments joined by `segment_separator`, which is a plain
free-text settings field with no validation, no allow-list and no character filter. A user can type
`/` or `=` into it. **This is the one condition nothing enforces**, and it is the one to fix on the
device side (validate the separator on entry) or to defend on the server side (reject such a code
at ingest) if mirror-mode codes are ever printed.

**(2) The code is never carried in a URL fragment.** The scanner builds its searchable text from
the path and the query only; `#` and everything after it is discarded by URL parsing and cannot be
recovered. `https://host/q#abc` strips to `q` and loses the code entirely. The landing route is
therefore a **path** route, and the web client routes it through history rather than a hash router
for exactly this reason. Never print a fragment form.

**(3) The code is never percent-encoded.** The scanner reads the *encoded* path and query and
nothing decodes them, so `%2D` arrives as three literal characters and fails the lookup. The QCRI
alphabet is `[0-9a-z]`, none of which any conforming generator escapes, so this holds automatically
for hash mode — and is one more reason a mirror-mode PCI with an exotic separator is the only real
risk of the three. The web client's own square is built with the code escaped into the path, which
is the identity for every code that satisfies (1) and (3); for one that does not, it shows a
warning beside the square saying the label will open in a browser and will not resolve in the app,
rather than printing an unscannable label silently. **That warning is the web client's; the device
enforces nothing**, so (1) remains unenforced where labels are generated.

## 4a. The one app-side change: the label generator still prints a deep link

Everything in §4 is about the **scanner**, and the scanner is genuinely unchanged. The **label
generator** is the other half, and it is not.

`_qrDataForPlace` in `lib/utils/cave_place_qr_generator.dart` returns `'$deepLinkPrefix$raw'`
whenever `prefs?.includeDeepLinkPrefix` is set — and it defaults to **true**. So the square the app
prints today encodes `sp://<code>`, which no camera app and no browser resolves and which never
reaches `/q/<code>`. **Nothing on the server can make a `sp://` label resolve.**

Printing a label a stranger's phone can open therefore requires the generator preference to become
a **URL prefix** — `https://<installation>/q/` — in place of the boolean `sp://` toggle. This
costs the app nothing it currently has: a `https://<installation>/q/<code>` label still resolves
inside SpeleoLoc's own scanner, because the scanner strips the address down to the text after the
last `/` (§4) exactly as it strips `sp://`. A label printed in the URL form works in both worlds;
a label printed in the `sp://` form works only in one.

Two labels printed at different times are not interchangeable, so an installation that has already
printed `sp://` labels keeps them working through the app and gains nothing from this route until
they are reprinted.

## 5. The derivation, and the salt

The server carries a byte-for-byte port of the device's QCRI derivation, so the two ends agree
about what a given place code becomes:

```
QCRI = base36_lowercase( sha256( baseSalt ‖ utf8(userSalt) ‖ utf8(placeCode) ) )[:length]
```

- `baseSalt` is 16 bytes, `9c42a16f3bd755180eb67ac3e921845d`.
- `userSalt` sits **between** the base salt and the place code, and an absent or empty one
  contributes no bytes at all.
- The digest is read as one **unsigned big-endian integer** and spelt in base 36 with **no
  leading-zero padding** — so it is 50 characters for most inputs and **49** when the leading bytes
  are small. Padding to a fixed width agrees with the device on almost every input and disagrees on
  exactly those, which is why the server's unit tests pin five 49-character cases by literal value.
- `length` is 4–16, default 8. Truncation is from the front, so lengthening a code extends it.

**The salt is a compatibility constant, not a credential.** It keeps no secret and defends nothing.
It is fixed so that two datasets which never met derive the same code from the same place code —
which is the whole point. It is therefore **compiled in on the server, not an installation
setting**: there is no `SILEXGIS__*` key for it, deliberately. Rotating it would harden nothing and
would silently stop every already-printed label matching anything derived afterwards. If a
server-side derivation ever has to agree with a differently-built device, that is the change that
introduces a setting — along with recomputing every code that has to keep agreeing.

### The landing route never recomputes

**Resolution is a stored-value lookup and nothing else.** A stored code may legally disagree with
what today's settings would produce, and that is not an error:

- the generating dataset chose the length, and may have changed it since;
- it may have set a `userSalt`, and may have changed that too;
- the default mode is **mirror**, where the QCRI is the PCI verbatim and no hashing happens at all,
  and `entrance_hash` hashes only entrances;
- when a derived code collides the generator lengthens it, and on exhaustion returns a code it
  knows collides — so the value stored depends on what else existed at generation time.

A code bolted to a cave wall is not made wrong by any of that. There is also **no server-side
validation of a code's shape beyond its length**, deliberately: mode, length and salt all live in
the generating dataset's own configuration, a mismatch is legal data, and there is no uniqueness
constraint anywhere to defend.

## 6. Deployment

**Nothing changes on the server side.** The web client serves `/q/:code` from its existing
single-page fallback, and the API prefixes the reverse proxy passes through are unchanged. An
installation that upgrades gets the route with no configuration, no new port and no new container,
and there is no setting to set — see §5 on why the salt is not one.

This says nothing about the app: **§4a is an app-side change**, and without it no printed label
carries an address this route can answer.
