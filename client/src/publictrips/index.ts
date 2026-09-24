// SPDX-License-Identifier: AGPL-3.0-or-later
/**
 * The published-trip fold, as one module a viewer of any kind can hold.
 *
 * <b>Why this file exists at all.</b> Two quite different pages now draw the same party: this
 * application's own followed page, written in React, and a club's own article, which carries a
 * plain-JavaScript viewer and no build step of any kind. Both have to answer the same questions —
 * where was each member of this party at this instant, which of them may be drawn on the survey
 * being shown, what is this team called, where does a replay begin and end. Answering them twice
 * would put two spellings of one rule in two languages, and the second spelling is the one nobody
 * runs the tests against: a withholding honoured on one page and forgotten on the other is exactly
 * the failure the whole publication surface is built to prevent.
 *
 * So the rule keeps one home. What follows is a curated surface over modules that already existed
 * and that this application already imports directly; this file adds no logic of its own, and
 * deliberately re-exports less than those modules hold.
 *
 * <b>What is deliberately absent, and it is the point.</b> None of the wording helpers
 * (`sinceInWords`, `positionAgeInWords`, `formatTripDate`, `tripDateRange`) are here. They reach
 * for `Intl.RelativeTimeFormat` and `toLocaleDateString`, which is to say they decide how a
 * language says a thing — and the two hosts do not share a language, a catalogue or a timezone
 * policy. Every function below returns instants as numbers and takes the words it needs as
 * arguments (`unnamed(ordinal)`, `names.unteamed`), so each host says things its own way over one
 * set of facts. A formatter added here later would be a rule about English quietly shipped to a
 * Romanian page.
 *
 * <b>Nothing here knows about the network.</b> These are pure functions over the shapes the
 * anonymous routes already answer with. How a host obtains an envelope — a fetch, a relayed
 * message, a file — is the host's business and is deliberately not decided here.
 *
 * <b>Two things that have already cost a host an afternoon, recorded here because both are places
 * where a mistake is silent rather than loud:</b>
 *
 * 1. <b>A reported place comes back on `station`, not `stationName`.</b> What goes into
 *    {@link publicPlaceReported} is a row as the server sends it, whose field is `stationName`;
 *    what comes out is a position whose field is `station`. Both shapes are in scope at the same
 *    call site and they differ by exactly that one name, so reading the wrong one yields
 *    `undefined` for every row and a caller that quietly draws nothing at all. Check the `kind`
 *    discriminator first and take the field off the branch.
 * 2. <b>A depth carries its own sign.</b> `{ kind: 'depth', depthM }` is already negative below an
 *    entrance, so a host that prefixes a minus prints `−-138 m` the first time a real reading
 *    arrives. Render the number as given.
 */

// ---- where a report may be drawn -----------------------------------------------------------

export { drawableOn, placeOnModel } from '../caveview/drawableOn.ts';
export type { ReportedPlace, DrawablePlace } from '../caveview/drawableOn.ts';

// ---- a party, as a published surface is told it ---------------------------------------------

export {
  publicTrackedCavers,
  publicPlaceReported,
  envelopeCrsLookup,
} from '../caveview/publicTrackedCavers.ts';
export type { PublicReportedPlace } from '../caveview/publicTrackedCavers.ts';

export {
  trackedCaverTeams,
  sharedTeamTitle,
  undergroundFirst,
  teamStation,
} from '../caveview/trackedCavers.ts';
export type {
  TrackedCaver,
  TrackedCaverPosition,
  TrackedCaverTeam,
} from '../caveview/trackedCavers.ts';

export { standingOf, partyStandings, partyByTeam, instantOf } from '../pages/public/publicTripParty.ts';
export type { PublicTripStanding, PublicTripPartyGroup } from '../pages/public/publicTripParty.ts';

// ---- a finished trip, played back ------------------------------------------------------------

export {
  pastEnvelopeAt,
  pastReplayWindow,
  pastReportMoments,
  momentBefore,
  momentAfter,
  followedStation,
  followedName,
  firstPlacedMoment,
} from '../pages/public/pastTrackReplay.ts';
export type { PastFollow } from '../pages/public/pastTrackReplay.ts';

export type { ReplayWindow } from '../caveview/trackingReplay.ts';

// ---- the same request written into an address ------------------------------------------------

export { PAST_LINK_PARAMS, readPastLink, writePastLink } from '../pages/public/pastTripLink.ts';
export type { PastTripLink } from '../pages/public/pastTripLink.ts';
