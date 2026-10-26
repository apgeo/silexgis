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

A second factor needs no special handling on that path either. An account that has one switched on
and signs in through an external provider is now asked for its code on the server's own sign-in page,
in the browser, before the authorization completes. The application never sees that step: it gets
the redirect with a `code` once the person has passed it, exactly as for an account without a second
factor.

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

What this section lists waited for its own reason when the contract was first published; an entry
marked *built* has since been added to the contract, additively, and says where it is described.

**Photos and documents — built.** They are files rather than rows, so they got a transfer shape of
their own (`01-protocol.md` §11): a catalogue stream (`GET /sync/sets/{id}/documents`) that names each
file with its hash, size and how much of it this reader may have (`reach`), short-lived addresses from
`POST /sync/files/links`, and, upward, bytes staged through the resumable transfer routes before the
upload's `documents` and `documentLinks` sections name them. A photo whose position this reader may
not be told comes down as a copy without it (`locationFree`); the stored file is never changed.
`documents` and `documentUpload` in `features`. What a device does when it has less storage than the
set it carries remains the device's decision: the catalogue gives it the sizes to decide with.

**Raster maps — built.** `01-protocol.md` §15: the `GET /sync/sets/{id}/rasters` stream, the upload's
`rasterMaps` and `rasterPins` sections, and `rasters` in `features`. A raster's **pixel pins never
become coordinates** — a pin is a position in an image, and unless the image is georeferenced there is
nothing to turn it into — but a pin names the place it marks, so a pin on a place this reader may not
see exactly is withheld with that place.

**The people directory — built.** `01-protocol.md` §12: `GET /sync/people` lists the club's people a
phone may attribute trips and photos to, and the upload's `people` section reports the phone's own
users. An installation can switch it off, and `people` is in `features` only while it is on.

**Review of uploads — built.** `01-protocol.md` §14: an account can be marked so that what it uploads
waits for a reviewer. A device opts in with `clientFeatures: ["held"]` and gets such rows back `held`;
one that does not is refused with `sync.client_update_required` rather than surprised. The decisions
come down on `GET /sync/sets/{id}/reviews`; `reviews` in `features`. A *file* import by such an
account (an archive, a spreadsheet, a vector file or photographs confirmed in the browser) waits for a
reviewer too, but its decisions are not in the phone's reviews stream: they are seen in the browser.

**Trips — built.** Phones now send their trip recordings through this protocol and receive the
recordings of the caves they carry (`01-protocol.md` §13): `tripRecordings` and `tripPoints` upload
sections, the `GET /sync/sets/{id}/trips` stream, and `trips` in `features`. The server makes a trip
report from each recording automatically when everything in it settles, by the same rule the archive
import's review uses, and flags the report for a person when something does not.

The question this section used to leave open — what happens when the same recording arrives twice, once
through an archive and once through the wire — is answered by identity: the recording's own identifier
is the row on both paths, the archive import keeps the recording as well as the trip, and a recording
that already has its report is refused by the other path (`import.recording_already_converted`) rather
than recorded twice (`01-protocol.md` §13.4).

A recording the server leaves waiting — the installation keeps recordings that do not settle as
recordings, or the uploader may no longer make the trip — is made a trip by hand through the archive
import's review reading the recording (`01-protocol.md` §13.3); the screen that opens it from a
recording is the browser's to add.

Photos hang on recordings and on their scans as well (`01-protocol.md` §11 and §13). Still not built:
any tool for settling what a flagged report names — the flag is cleared by a person who may change the
trip, and the fixing is theirs to do by hand.

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
  which are the current ones at the moment of copying. The application effort has since committed
  that copy, with a note of its own naming the upstream commit it was taken at; refreshing it is that
  effort's job, and the `digest` in the two `manifest.json` files tells whether it is current. The
  client side is a separate piece of work.
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
