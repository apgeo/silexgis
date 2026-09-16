// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CaveViewLiveMarker } from './loadCaveView.ts';

/**
 * Which stations the drawing on screen turns out not to hold.
 *
 * <b>This is a different question from the one next door, and the two must not be merged.</b>
 * `drawableOn.ts` answers <em>which model a report belongs to</em>: a station path is a name
 * inside one survey, so a place measured in survey A may not be drawn on survey B. That is a rule
 * about two recorded identifiers, it is decided from the row alone, and the server applies the
 * same rule before it hands a published page anything. This answers <em>whether the drawing on
 * screen actually holds the station that was named</em> — which no identifier can tell anybody,
 * because it depends on what is inside the file the viewer parsed. Only the viewer knows, and the
 * only way to learn it is to ask the viewer.
 *
 * <b>Both can be true at once and they fail differently.</b> A report drawable on this model can
 * still name a station this model has no node for: a survey re-exported with its stations renamed
 * leaves every earlier report pointing at names that are gone, under the same model id, with
 * nobody having done anything. That is why this is the general case of the re-pointed-watch
 * failure rather than a second spelling of it — it needs no administrator action at all.
 *
 * <b>The answer is about the drawing, not about the party, and that is what makes it safe to
 * carry.</b> "This survey holds no station called `cave.deep.3`" is a fact about one parsed file:
 * it is true of whoever is standing there, of a person nobody has drawn yet, and of a person taken
 * off the model a moment ago. Keyed by who was drawn it would be none of those — it would be an
 * answer about one party, and the surfaces that read it show another. The coordinator's model
 * panel is the case that proves it: engaging its replay hands the viewer the party as it stood at
 * some past moment, while the table above goes on listing the party as it stands now. An answer
 * keyed by person would empty itself the instant the scrubber moved and put every station on that
 * table back to reading as a place somebody is.
 *
 * <b>The viewer keeps an unplaced marker rather than refusing it</b>, and hands back
 * `resolved: false` for it, because a model containing the station may yet be loaded. So "not
 * resolved" is the viewer's own word for "asked for, held, and drawn nowhere", which is exactly
 * the state a reader must be told about: the overlay beside the model lists the person and their
 * station either way, and without this the list says where somebody is over a model that is
 * showing nobody.
 */

/**
 * What the drawing is now known not to hold: everything already known, plus every station the
 * viewer has just reported a marker unresolved at.
 *
 * <b>Learned and kept rather than recomputed, because the viewer can only be asked about markers
 * it is holding.</b> A marker taken off the model — somebody who came out, a whole party replaced
 * by a replay of an earlier moment — takes the evidence about its station with it, and an answer
 * rebuilt from scratch each time would forget the station as soon as nobody was standing at it.
 * What was learned stays true for as long as the same survey is loaded: the viewer re-resolves
 * every marker it holds when a survey is parsed, and it parses one only when the panel loads one,
 * at which point the panel starts this again from nothing.
 *
 * <b>What it cannot answer is a station nothing has ever asked about.</b> The viewer can only be
 * asked through a marker, so a station no marker has stood at on this drawing is unknown rather
 * than held or missing, and a reader is told nothing about it. That is a silence and never a false
 * mark, and there is one way to reach it: a report landing while the coordinator's replay is
 * engaged, naming a station nobody has been drawn at since the model was loaded. It closes itself
 * the moment the replay is left and the live party is drawn again. Closing it sooner would mean
 * placing a marker nobody asked for, on the model somebody is reading, to see where it landed.
 *
 * <b>Only `resolved: false` teaches anything, and a marker the viewer is not holding teaches
 * nothing.</b> A missing marker means the panel and the viewer disagree about what was asked for,
 * which is a different failure and a recoverable one; recorded here it would condemn a station the
 * drawing holds perfectly well, permanently, on the strength of a bookkeeping slip.
 *
 * Answers the very set it was given when there is nothing new, so that a panel re-reading its
 * watch twice a minute re-renders the list beside the model, the table above it and the published
 * page only when the answer has actually changed.
 */
export function stationsNotOnModel(
  known: ReadonlySet<string>,
  drawn: readonly Pick<CaveViewLiveMarker, 'ref' | 'resolved'>[],
): ReadonlySet<string> {
  let learned: Set<string> | null = null;
  for (const marker of drawn) {
    // Only the dotted form is recorded. Every marker this application places is asked for by the
    // string the anchor stores, and the viewer hands that same string back; a reference given as
    // split components would have to be joined to be compared, and a dotted path is ambiguous when
    // a name inside it contains a dot of its own — so joining one would invent a station name
    // rather than report one.
    if (marker.resolved || typeof marker.ref !== 'string' || known.has(marker.ref)) {
      continue;
    }
    learned ??= new Set(known);
    learned.add(marker.ref);
  }
  return learned ?? known;
}

/** The answer for a panel with no model loaded: no station is claimed to be missing from one. */
export const noStationsMissing: ReadonlySet<string> = new Set<string>();
