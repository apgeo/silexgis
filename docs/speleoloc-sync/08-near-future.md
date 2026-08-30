# What was written down rather than built

Everything here was considered and deliberately not built in this first phase. It is listed so that a
later reader knows each one was a decision rather than an oversight, and so nobody spends a day
looking for a feature that is not there.

Each item says what would be needed to do it, and what it costs on the server side today. Where the
reason is the owner's rather than a technical one, the reason is quoted.

---

## 1. Sign-in through the system browser

**Deferred. Zero server work remains.**

Sign-in today is the in-app flow: the application collects the password itself, posts it to the login
endpoint, then drives the authorization request and the token exchange. That is the right shape for
an installation whose accounts live in SilexGIS.

It is the wrong shape for an installation that authenticates against an external identity provider
only — a club on a corporate or university sign-on. There the password is never SilexGIS's to
collect, and the sign-in has to happen in a real browser.

**Nothing on the server has to change for that.** The client is registered as a public, native
application with PKCE required, and **both** redirect forms are already seeded: a loopback callback
and a custom scheme. Whichever the application eventually uses, the server already accepts it. The
work is entirely client-side: open the authorization URL in the system browser instead of in the
application, and catch the redirect back.

## 2. Preview-then-confirm upload

**Not built. The shape is written down.**

The upload built here is **push-with-report**: the device sends a batch, the server applies what it
can and answers row by row with what happened, including any conflicts and a duplicate-proximity
report. One round trip, and the device holds the outcome.

The alternative considered was **preview-then-confirm**: a first call that says what *would* happen,
a human decision, then a second call that commits it. It is the right shape when an upload is large,
rare and consequential — a bulk import — and the server already has the machinery for it in the
staged-import preview the web interface uses.

It was not chosen because a device sync is the opposite of that: frequent, small, and usually
uncontroversial. Two round trips over a mobile connection for a batch of four edits is a cost paid
every time to benefit the rare case, and the report the single call already returns tells the caver
everything the preview would have.

If it is wanted later it is an additional endpoint, not a change to this one — the existing upload
keeps working exactly as documented.

## 3. The FTP relay path: documented, not guarded

**Not guarded. This is the owner's decision, and his reason is recorded verbatim.**

The concern: a device could receive a coordinate through some channel, hold it, and later send it
back to the server as if it were surveyed truth. The sync channel itself is closed against this —
positions the caller may not see exactly are withheld from sync entirely, never blurred — and the
write-right rule refuses to apply a coordinate from a caller without the right to set it, whatever
its provenance.

What is *not* guarded is the longer route: an FTP archive carrying a coordinate that originally came
from a SilexGIS export, imported into a second device, and uploaded from there. Guarding it would
require the device to declare each coordinate's provenance and the server to refuse any coordinate
the device marks approximate — a client-declared fact the server would be trusting, which is exactly
the kind of guard this project does not build.

The owner's reason for not building it:

> "You can forget about the harder guard and not implement it since it will be a rare case to have
> both SilexGIS server and FTP server (this setup will be used mostly for development) so there will
> be likely no data escape via that channel in the real world — you can just document it for a
> start."

So it is documented. If it is ever reconsidered, note that it needs **both** halves — the client-side
provenance marker and the server-side refusal — and that neither is useful without the other.

## 4. Listing and revoking sessions

**Not built, and honestly limited if it were.**

There is no server-side session list and no per-device sign-out for an account holder today. What
ends a session is described in the sign-in document: the refresh token lapsing, the account's password
being changed or reset — which revokes every token and authorization that account holds, on every
device, deliberately — a redeemed refresh token being replayed, or the account being deleted.

A list-and-revoke slice would help somebody whose phone was lost and who does not want to sign every
other device out. The honest caveat is that **it could not label the sessions usefully**: what the
server holds is a client identifier and a sign-in date, not a device name. A caver with two phones
would be choosing between two identical rows. The blunt instrument — change the password, end
everything — is what is available today, and it is the action somebody in that situation can actually
reach.

## 5. Phase two

None of the following is in this contract, and each waits for its own reason.

**Photos and documents.** They are files rather than rows, so they need a transfer shape this contract
does not have — content addressing, resumable transfer, and a decision about what a device does when
it has less storage than the set it carries. The row protocol was deliberately finished first so that
the file protocol can be designed against a working one.

**Raster maps.** The same file problem, and larger. Worth noting that a raster's **pixel pins can
never become coordinates** — a pin is a position in an image, and unless the image is georeferenced
there is nothing to turn it into. That makes them safe to carry, and it also makes them useless as a
way to move a position, which is why they are not a protection concern.

**Trips.** Waits on server work that has not merged. Syncing trips before that lands would mean
writing this contract against a shape that is still moving.

**Wider selection rules for a sync set.** A set names root features today, and everything contained
in a named root is carried. Selecting by caving group, by area, or by a saved filter are all
reasonable and none of them is in this version.

---

## 6. What this effort did not build at all

Stated so nobody goes looking:

- **No client code.** No Flutter, no Dart, no build, no continuous integration. Two things were
  copied into the application's repository and nothing else was: these documents, under
  `docs/integrations/silexgis/`, and the recorded traffic, under
  `test_data/silexgis_contract/v1/` — both byte-identical to the copies in the server repository,
  which are the current ones. **The copy is placed in a working tree and is not committed there**:
  the first commit in that repository for this integration belongs to the application effort, so a
  clone of it will not carry these files until that effort commits them. The client side is a
  separate piece of work.
- **No generated client library.** Deliberate, and the evidence is worth keeping: the served
  description has no operation identifiers outside this slice, declared no security scheme until this
  work added one, declares no error responses anywhere, and types the property document as an
  unconstrained object. A generator run over it produces a large surface, strongly typed on the parts
  that were never in doubt and untyped on the parts that matter. The recorded fixtures are the
  contract instead, and a hand-written client is the recommendation.
- **No wider work on the served description.** Naming every operation on this server and declaring
  its failures is worth doing and is its own effort. What was done here is scoped to this slice.
- **No long-lived server instance.** The development server is started per session from the script
  described in the dev-server document, and thrown away.
