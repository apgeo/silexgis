// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingState } from '../api/hooks.ts';

/**
 * Folding a trip's watch into the people a survey model can draw.
 *
 * Nothing here imports the viewer, so all of it is arithmetic over plain objects a test can drive
 * without a WebGL context — the same reason the anchor arithmetic next door is arranged this way.
 *
 * Two rules are carried over from the tracking table unchanged, because they are the same rules
 * and a second spelling of them is a second thing to get wrong:
 *
 * **A withheld position is said to have been withheld.** Station names are location data and are
 * kept from a reader without the right to place the cave; they arrive as absences, exactly as they
 * do for somebody nobody has reported yet. Drawing both as "nothing known" would tell a rescue
 * co-ordinator that nobody knows where a caver is, when what is true is that *they* are not being
 * told. No marker is invented for a position that was withheld — there is nowhere to put one —
 * but the person is still listed, and listed as withheld.
 *
 * **How strongly that is said follows the last report.** A report whose kind always carries a
 * place, arriving without one, can only be a withholding. Going in, coming out and a radio note
 * carry no place at all, so an absence after one of those is the ordinary early-trip state and is
 * said as the weaker thing.
 */

/** Where one person was last reported, as far as this reader is being told. */
export type TrackedCaverPosition =
  /** A station of the model on screen, which is where the marker goes. */
  | { kind: 'station'; station: string }
  /** A depth below the entrance, which no station names — so it is said, not drawn. */
  | { kind: 'depth'; depthM: number }
  /** A position exists and this reader may not be told it. `certain` is the stronger claim. */
  | { kind: 'withheld'; certain: boolean }
  /** Nobody has reported a place for this person. */
  | { kind: 'unreported' };

/** One person on the watch, ready to be drawn over the model. */
export interface TrackedCaver {
  caverId: string;
  /** How the person reads on screen — the trip's roster is what knows this, not the watch. */
  name: string;
  teamTitle: string | null;
  position: TrackedCaverPosition;
  /** ISO instant of the latest report of any kind, or null when there has been none. */
  lastRecordedAt: string | null;
  /** When this person went in, as whoever mounts the panel records it. */
  enteredAt: string | null;
  /** Reported out. Their last position is where they were, not where they are. */
  out: boolean;
}

/** What the roster knows about somebody the watch only knows by id. */
export interface TrackedCaverIdentity {
  name: string;
  enteredAt?: string | null;
}

/**
 * The people of one watch, placed against one survey model.
 *
 * <b>Nothing is returned when the watch resolves positions against another model.</b> A station
 * path means whatever the model it was measured in says it means, so `sala-mare.4` of one cave
 * names a place in a different cave with the same survey names — and a marker drawn from it would
 * be a confident statement about where somebody is, made from a name that happens to collide. A
 * panel showing a different model than the watch names therefore shows no watch at all.
 *
 * @param identify what the trip's own roster says about a caver id: the watch carries ids, and
 *   nothing on it knows what anybody is called.
 * @param surveyModelId the model the panel is showing, or undefined when it does not know.
 */
export function trackedCaversFrom(
  tracking: TrackingState,
  identify: (caverId: string) => TrackedCaverIdentity,
  surveyModelId: string | undefined,
): TrackedCaver[] {
  if (
    surveyModelId === undefined
    || tracking.surveyModelId === null
    || tracking.surveyModelId !== surveyModelId
  ) {
    return [];
  }

  const teamTitles = new Map(tracking.teams.map((team) => [team.id, team.title]));

  return tracking.participants.map((participant) => {
    const identity = identify(participant.caverId);
    return {
      caverId: participant.caverId,
      name: identity.name,
      teamTitle:
        participant.teamId === null ? null : (teamTitles.get(participant.teamId) ?? null),
      position: positionOf(participant, tracking.positionsWithheld),
      lastRecordedAt: participant.lastRecordedAt,
      enteredAt: identity.enteredAt ?? null,
      out: participant.out,
    };
  });
}

function positionOf(
  participant: TrackingState['participants'][number],
  positionsWithheld: boolean,
): TrackedCaverPosition {
  if (participant.stationName !== null && participant.stationName.length > 0) {
    return { kind: 'station', station: participant.stationName };
  }
  // A depth is a position and is not a station: it is somewhere on a line the model does not
  // draw, so it is reported in words rather than placed at a station it might not be at.
  if (participant.depthM !== null) {
    return { kind: 'depth', depthM: participant.depthM };
  }
  if (participant.lastRecordedAt === null || !positionsWithheld) {
    return { kind: 'unreported' };
  }
  return {
    kind: 'withheld',
    certain: participant.lastKind === 'atStation' || participant.lastKind === 'atDepth',
  };
}
