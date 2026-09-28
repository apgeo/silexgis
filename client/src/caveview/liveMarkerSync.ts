// SPDX-License-Identifier: AGPL-3.0-or-later
import type {
  CaveViewer,
  CaveViewLabelText,
  CaveViewLiveMarkerOptions,
} from './loadCaveView.ts';
import { sharedTeamTitle, undergroundFirst, type TrackedCaver } from './trackedCavers.ts';

/**
 * Bringing the live markers standing on a model into line with the party that should be drawn,
 * and the words a collapsed marker is headed with.
 *
 * <b>Two surfaces draw the party with these, and that is why they are here.</b> The tracking
 * panel re-reads the watch every half minute and moves whoever moved; an exported movie replays a
 * log frame by frame and does the same thing hundreds of times. Written twice, the two would sooner
 * or later disagree about when a marker is moved rather than left alone, or about how a group of
 * people at one station reads — and the movie is a copy people keep, so it is the one place such a
 * disagreement would outlive the screen it was noticed on.
 */

/** One marker as it was last handed to the viewer — what the next pass is compared against. */
export interface DrawnMarker {
  station: string;
  label: CaveViewLabelText;
  color: string;
}

/**
 * The marker options with the one the viewer is gaining for exported movies: how long a move
 * takes, in milliseconds, where 0 places the marker without sliding it.
 *
 * Declared here, as an intersection over the viewer's own typings, only until those typings carry
 * the option themselves; then this collapses to the viewer's type and the intersection goes. A
 * viewer that does not know the option ignores it, which is the behaviour it has today.
 */
type LiveMarkerOptionsWithDuration = CaveViewLiveMarkerOptions & { duration?: number };

/** Two labels as the viewer would draw them: a single line, or the same lines in the same order. */
function sameLabel(left: CaveViewLabelText, right: CaveViewLabelText): boolean {
  if (typeof left === 'string' || typeof right === 'string') {
    return left === right;
  }
  return left.length === right.length && left.every((line, index) => line === right[index]);
}

/**
 * Adds, moves and removes markers so that what the viewer holds is `wanted`, touching only the
 * markers that differ from `drawn`.
 *
 * A move replaces only the options it is given, which is no longer a trap: every option these
 * markers carry is given on every call, so none of them can be left behind by one. A marker whose
 * station, label and colour are all unchanged is not touched at all — which is what keeps a poll
 * that changed nothing from rebuilding every label on the model.
 *
 * `duration` is passed on moves only, and only when it is given: a marker being added has nowhere
 * to slide from, and a caller that says nothing about time keeps the viewer's own transition.
 *
 * @returns what is now drawn, to be handed back as `drawn` on the next pass.
 */
export function syncLiveMarkers(
  viewer: Pick<CaveViewer, 'addLiveMarker' | 'moveLiveMarker' | 'removeLiveMarker'>,
  drawn: ReadonlyMap<string, DrawnMarker>,
  wanted: ReadonlyMap<string, DrawnMarker>,
  options?: { duration?: number },
): Map<string, DrawnMarker> {
  for (const [id, marker] of wanted) {
    const before = drawn.get(id);
    const given = { label: marker.label, color: marker.color };
    if (before === undefined) {
      viewer.addLiveMarker(id, marker.station, given);
    } else if (
      before.station !== marker.station
      || !sameLabel(before.label, marker.label)
      || before.color !== marker.color
    ) {
      const moved: LiveMarkerOptionsWithDuration =
        options?.duration === undefined ? given : { ...given, duration: options.duration };
      viewer.moveLiveMarker(id, marker.station, moved);
    }
  }
  for (const id of drawn.keys()) {
    if (!wanted.has(id)) {
      viewer.removeLiveMarker(id);
    }
  }
  return new Map(wanted);
}

/**
 * The block of lines a collapsed marker standing for these people is labelled with, or null where
 * none of them is known — which leaves the viewer's own count, the honest thing to draw when there
 * is nothing to say about them.
 *
 * <b>The heading is read from everybody collapsed, including whoever is out.</b> It is the claim
 * that the names under it are one team, so it is answered by the whole set the marker stands for:
 * a caver of another team who has come out at this station is still a second team at it, and
 * heading the block with the first team's name because the mixture only shows below the fold
 * would be the same false claim the shared-title rule exists to refuse. Team ids are the identity
 * compared, so two trips' teams that happen to share a title are still two teams.
 *
 * The names follow, whoever is still underground first, each spelled by `lineOf` — the same line
 * the person's own marker carries, so a party gathering at one station and separating again does
 * not add and drop facts about them as it goes.
 */
export function clusterLabelFor(
  members: readonly TrackedCaver[],
  lineOf: (member: TrackedCaver) => string,
): string[] | null {
  if (members.length === 0) {
    return null;
  }
  const title = sharedTeamTitle(members);
  return [...(title === null ? [] : [title]), ...undergroundFirst(members).map(lineOf)];
}
