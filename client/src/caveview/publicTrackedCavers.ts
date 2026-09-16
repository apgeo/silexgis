// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PublicTripEnvelope, PublicTripModel, PublicTripParticipant } from '../api/hooks.ts';
import type { TrackedCaver, TrackedCaverPosition } from './trackedCavers.ts';

/**
 * Folding a *published* trip into the people a survey model can draw.
 *
 * The signed-in fold lives next door in `trackedCavers.ts` and this is deliberately not it. The
 * two answers are different shapes on purpose — the published envelope is keyed by a place in the
 * party and carries no caver id at all, which is the one thing that must never travel to a
 * follower — so a fold serving both would have to be handed the identity as a parameter, and the
 * surface that must never produce one would be one argument away from doing so.
 *
 * Three rules differ from the signed-in fold, and each of them is a fact about the envelope rather
 * than a choice made here:
 *
 * **The key is the ordinal.** Everything downstream — the marker ids the viewer holds, the card
 * the overlay opens — keys on `caverId`, and here that string is a place in the party. It is
 * stable across reads of the same trip (the server counts by the row somebody was first written
 * on), so a marker slides as reports arrive rather than being taken off and put back.
 *
 * **The name is the administrator's, or a number.** The envelope carries a label only where
 * somebody typed one, and the server deliberately renders no default: it has no reader's language
 * to render one in. So the caller supplies the words for an unnamed place in the party, in the
 * language the page is being read in.
 *
 * **A withholding can only be said as the weaker claim.** The signed-in fold can tell "this
 * position exists and you may not be told it" from "nobody has reported a place yet", because it
 * is sent the kind of the last report and a station or depth report always carries a place. The
 * published envelope carries no kind — not because it would be personal data, but because it is
 * one more fact about a party than a follower needs — so that distinction cannot be computed here,
 * and it is not guessed. It falls to the weaker of the two, which reads as "not shown, or not
 * reported": over-claiming would tell a stranger that something is being kept from them when the
 * ordinary answer is that the party has only just set off.
 */
export function publicTrackedCavers(
  envelope: Pick<PublicTripEnvelope, 'participants' | 'teams' | 'positionsWithheld'>,
  /** What to call somebody the administrator did not name — the reader's language decides. */
  unnamed: (ordinal: number) => string,
): TrackedCaver[] {
  const teamTitles = new Map(envelope.teams.map((team) => [team.id, team.title]));

  return envelope.participants.map((participant) => ({
    caverId: String(participant.ordinal),
    name: participant.label ?? unnamed(participant.ordinal),
    teamId: participant.teamId,
    teamTitle: participant.teamId === null ? null : (teamTitles.get(participant.teamId) ?? null),
    position: positionOf(participant, envelope.positionsWithheld),
    lastRecordedAt: participant.lastRecordedAt,
    // The position's own moment, which the envelope now carries. It used to be null here on the
    // grounds that nothing in the envelope could say whether the time beside somebody dated their
    // station or a later word that named no place — so every followed team fell back to comparing
    // last words, and every place on this page was as old as the last thing anybody said. The
    // envelope answers that question itself now, and null here means what it means everywhere
    // else: nothing placed this person, or this reader may not be told where.
    positionAt: participant.positionRecordedAt,
    // Not on the envelope, and not invented from the trip's own start: the moment somebody went
    // in survives only on the report log, which a follower is not sent. The card says "—" rather
    // than a time that would be a guess presented as a record.
    enteredAt: null,
    out: participant.out,
  }));
}

function positionOf(
  participant: PublicTripParticipant,
  positionsWithheld: boolean,
): TrackedCaverPosition {
  // Measured in a survey other than the one this page draws, and the server has already taken the
  // station and the depth off the row — so this bit is the only thing that tells the difference
  // between "a place is known and cannot be shown here" and "nobody has reported one". Read first,
  // because every other branch below would answer the second of those.
  //
  // <b>Decided on the server, not here.</b> The signed-in fold compares two ids because it is given
  // both; a follower is given neither, deliberately — which survey a report was measured in is a
  // fact about the cave's surveying and no part of what a page like this hands out.
  if (participant.positionOnOtherModel) {
    return { kind: 'otherModel' };
  }
  if (participant.stationName !== null && participant.stationName.length > 0) {
    return { kind: 'station', station: participant.stationName };
  }
  // A depth is a position and is not a station: somewhere on a line the model does not draw, so
  // it is said in words rather than placed at a station it might not be at.
  if (participant.depthM !== null) {
    return { kind: 'depth', depthM: participant.depthM };
  }
  if (participant.lastRecordedAt === null || !positionsWithheld) {
    return { kind: 'unreported' };
  }
  return { kind: 'withheld', certain: false };
}

/**
 * How a page with no account resolves the survey's coordinate system: out of the answer it was
 * already given, and never over the network.
 *
 * The viewer resolves a system named in a survey file by asking for its definition, and the route
 * that answers those is not on the anonymous list and is deliberately not being put there for one
 * page — opening an enumerable route widens the anonymous surface for every caller on the
 * internet, where a string in a response widens it by nothing. So the definition travels in the
 * envelope and this looks it up in that string.
 *
 * <b>A code that is not the one the envelope described answers null</b>, which is what an unknown
 * code has always answered and what makes a survey load unreferenced rather than not at all.
 * Answering the definition regardless would place a cave with the wrong datum, confidently and
 * with nothing on screen to say so.
 *
 * One home for both public mounts: the followed page and the embedded viewer make exactly the same
 * decision, and two spellings of it is one of them being fixed later than the other.
 */
export function envelopeCrsLookup(
  model: Pick<PublicTripModel, 'proj4' | 'sourceEpsg'> | null,
): (code: string) => Promise<string | null> {
  const proj4 = model?.proj4 ?? null;
  const epsg = model?.sourceEpsg ?? null;
  return (code: string) => {
    if (proj4 === null || epsg === null) {
      return Promise.resolve(null);
    }
    // The viewer asks with whatever the survey file wrote — a bare number, or one prefixed by its
    // register. Only the number decides, and a code carrying none matches nothing.
    const asked = Number.parseInt(code.replace(/\D+/gu, ''), 10);
    return Promise.resolve(asked === epsg ? proj4 : null);
  };
}
