// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLinkCreate } from '../api/hooks.ts';
import { regionAnchor } from '../imagelink/regions.ts';
import { toMapCoordinate, type ImageSize } from './coordinates.ts';
import { newestPinForStation, type MapPin, type MapStationMarker } from './mapPoints.ts';

/**
 * What authoring a pin writes and decides — the single home for the shapes the paired
 * mode sends and for the rules it applies before sending anything.
 *
 * Everything here is arithmetic over plain objects: the components arm, click and
 * confirm; this decides what those acts mean and what goes on the wire, so a test can
 * hold the whole authoring contract without a map, a modal or a network.
 */

/** The station the next map click will pin — armed from a 3D press, the typeahead, or a
 * re-place row. */
export interface ArmedStation {
  /** The station path in the viewer's own spelling, exactly as the anchor will store it. */
  station: string;
  /**
   * The pin link this placement corrects, when the arm came from a correction flow
   * (re-place of a superseded pin, or "move here" on a pressed marker). Null means the
   * placement intends a new pin — though a duplicate check still decides (below).
   */
  replaceLinkId: string | null;
}

/** A point of the picture in the stored frame — fractions, top-left origin. */
export interface FractionPoint {
  x: number;
  y: number;
}

/**
 * The one POST a new pin is: two members in one link — the point on the map document,
 * pinned to the very file the click was measured against, and the station of the model.
 *
 * The document member is the main member on purpose: the relation is directed and reads
 * out of the map, and the server's curation rule follows the main member — so whoever
 * may edit the map document may correct its pins, rather than only the pin's author.
 */
export function pinCreateBody(
  relationTypeId: number,
  documentId: string,
  fileId: string,
  at: FractionPoint,
  surveyModelId: string,
  station: string,
): ResLinkCreate {
  return {
    relationTypeId,
    description: null,
    members: [
      {
        targetType: 'document',
        targetId: documentId,
        isMain: true,
        sortOrder: 0,
        note: null,
        anchorKind: 'imageRegion',
        anchor: regionAnchor({ shape: 'point', x: at.x, y: at.y }),
        anchorFileId: fileId,
      },
      {
        targetType: 'surveyModel',
        targetId: surveyModelId,
        isMain: false,
        sortOrder: 1,
        note: null,
        anchorKind: 'modelStation',
        anchor: { station },
        anchorFileId: null,
      },
    ],
  };
}

/** The one POST a map declaration is: document (main, whole) onto model (whole). */
export function mapDeclarationBody(
  relationTypeId: number,
  documentId: string,
  surveyModelId: string,
): ResLinkCreate {
  return {
    relationTypeId,
    description: null,
    members: [
      {
        targetType: 'document',
        targetId: documentId,
        isMain: true,
        sortOrder: 0,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
      },
      {
        targetType: 'surveyModel',
        targetId: surveyModelId,
        isMain: false,
        sortOrder: 1,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
      },
    ],
  };
}

/** What one placement click should do, decided before anything is written. */
export type PlacementPlan =
  /** No pin for this station on this map: one POST creates it. */
  | { kind: 'create' }
  /** The arm itself named the link to correct: rewrite it, no questions. */
  | { kind: 'replace'; oldLinkId: string }
  /**
   * The station already has a pin on this map (on whatever scan). Writing a second link
   * beside it is the duplicate the fold would then have to newest-wins away, so the UI
   * warns first and offers rewriting the standing pin — which is only offered when the
   * caller may actually edit it (`pin.mayEdit`), because an offer the server refuses is
   * as wrong as a missing one.
   */
  | { kind: 'occupied'; pin: MapPin };

/**
 * Decides a placement click for an armed station against the pins this map already has.
 * The duplicate check runs across every file of the document deliberately — see
 * {@link newestPinForStation} — so a pin stranded on an older scan is corrected, not
 * doubled.
 */
export function placementPlan(pins: readonly MapPin[], armed: ArmedStation): PlacementPlan {
  if (armed.replaceLinkId !== null) {
    return { kind: 'replace', oldLinkId: armed.replaceLinkId };
  }
  const standing = newestPinForStation(pins, armed.station);
  return standing === null ? { kind: 'create' } : { kind: 'occupied', pin: standing };
}

/**
 * Moves or re-places a pin as the two writes the API offers: create the corrected link,
 * then delete the old one.
 *
 * <b>Create first, deliberately.</b> The member PATCH cannot carry a new anchor payload
 * — the server updates only a member's marker, order and note — so a correction is a new
 * link and the retirement of the old one, and the order decides what a failure between
 * the two calls leaves behind. Delete-first would leave the station unpinned if the
 * create then failed; create-first leaves at worst a duplicate pair, which the newest-wins
 * fold already answers for (the new pin shows) and the duplicate warning surfaces for
 * cleanup. The new link's own creation also re-runs the authoring floor, and its newer
 * `createdAt` is what makes newest-wins pick it before the delete has even landed.
 */
export async function rewritePin(
  create: (body: ResLinkCreate) => Promise<unknown>,
  remove: (linkId: string) => Promise<unknown>,
  body: ResLinkCreate,
  oldLinkId: string,
): Promise<void> {
  await create(body);
  await remove(oldLinkId);
}

/**
 * The marker a click landed on, if any — nearest within the pointer's tolerance, or null
 * when the click is the sheet itself.
 *
 * Hit-testing is done in map coordinates against the same conversion the markers were
 * drawn through, rather than through OL's rendered-frame hit test, so the answer is a
 * pure function of what the caller drew: `tolerancePx * resolution` is the on-screen
 * radius a finger or a cursor is owed, translated into map units at the current zoom.
 */
export function markerHit(
  markers: readonly MapStationMarker[],
  size: ImageSize,
  coordinate: readonly number[],
  resolution: number,
  tolerancePx: number,
): MapStationMarker | null {
  const reach = tolerancePx * resolution;
  let best: MapStationMarker | null = null;
  let bestDistance = Infinity;
  for (const marker of markers) {
    const [mx, my] = toMapCoordinate(marker, size);
    const distance = Math.hypot(coordinate[0] - mx, coordinate[1] - my);
    if (distance <= reach && distance < bestDistance) {
      best = marker;
      bestDistance = distance;
    }
  }
  return best;
}
