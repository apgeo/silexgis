// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink } from '../api/hooks.ts';
import { readRegion } from '../imagelink/regions.ts';
import type { SheetMarker } from './coordinates.ts';
import { MAP_PLACE_POINT_CODE } from './vocabulary.ts';

/**
 * The places phones pinned on one picture, read out of the picture's links.
 *
 * A place pin is one link under the place-pin code, pairing a point on the picture (an
 * image-region anchor pinned to the file it was measured on) with one place. The server shows the
 * link whole or not at all, so a link that arrives here is one this reader may be shown; the fold
 * still asks for the whole shape, code and structure both, so a mislabelled link is never drawn as
 * a place.
 *
 * Besides the fraction it is drawn at, the point carries the pixel the phone measured. A pixel that
 * does not fall on the picture as it is drawn — or one on a picture of unknown size — comes without
 * a fraction, and is counted rather than drawn at a guessed spot; so is a point measured on an
 * earlier picture, whose fractions mean nothing on this one.
 */

/** One place pin as stored. */
export interface PlacePin {
  linkId: string;
  /** The picture member carrying the point. */
  memberId: string;
  placeId: string;
  /** The place's name as this reader is given it; null when it has none. */
  placeName: string | null;
  /** The point to draw, or null when the stored pixel has no place on the picture as drawn. */
  point: { x: number; y: number } | null;
  /** The file the point was measured on — null when this reader is not told which. */
  anchorFileId: string | null;
  /** Whether the point was measured on a picture the document no longer serves. */
  earlier: boolean;
  /** Whether this reader may move or remove it: the right to change the map's cave. */
  mayEdit: boolean;
}

/** One place drawn on the sheet. */
export interface PlaceMarker extends SheetMarker {
  place: true;
  linkId: string;
  placeId: string;
  mayEdit: boolean;
}

/** Every place pin on one picture document. */
export function placePinsFromLinks(links: readonly ResLink[], documentId: string): PlacePin[] {
  const pins: PlacePin[] = [];
  for (const link of links) {
    if (link.relationType?.code !== MAP_PLACE_POINT_CODE) {
      continue;
    }
    const picture = link.members.find(
      (member) =>
        member.isMain
        && member.targetType === 'document'
        && member.targetId === documentId
        && member.anchorKind === 'imageRegion',
    );
    const places = link.members.filter((member) => member.targetType === 'feature');
    if (picture === undefined || places.length !== 1) {
      continue;
    }
    const region = readRegion(picture.anchor);
    pins.push({
      linkId: link.id,
      memberId: picture.id,
      placeId: places[0].targetId,
      placeName: places[0].display?.title ?? null,
      point: region?.shape === 'point' ? { x: region.x, y: region.y } : null,
      anchorFileId: picture.anchorFileId ?? null,
      earlier: picture.anchorState === 'degraded',
      mayEdit: link.mayEdit,
    });
  }
  return pins;
}

/** Whether a pin was measured on the file on screen. */
function onFile(pin: PlacePin, fileId: string): boolean {
  return !pin.earlier && pin.anchorFileId === fileId;
}

/** The places to draw over the file on screen, named for the sheet. */
export function placeMarkers(
  pins: readonly PlacePin[],
  fileId: string,
  unnamed: string,
): PlaceMarker[] {
  return pins
    .filter((pin) => onFile(pin, fileId) && pin.point !== null)
    .map((pin) => ({
      key: pin.linkId,
      label: pin.placeName ?? unnamed,
      x: pin.point!.x,
      y: pin.point!.y,
      place: true as const,
      linkId: pin.linkId,
      placeId: pin.placeId,
      mayEdit: pin.mayEdit,
    }))
    .sort((a, b) => a.label.localeCompare(b.label) || a.linkId.localeCompare(b.linkId));
}

/** Points measured on an earlier picture of the document — counted, never drawn. */
export function earlierPicturePins(pins: readonly PlacePin[], fileId: string): PlacePin[] {
  return pins.filter((pin) => !onFile(pin, fileId));
}

/** How many points measured on the file on screen do not fall on it as it is drawn. */
export function offPictureCount(pins: readonly PlacePin[], fileId: string): number {
  return pins.filter((pin) => onFile(pin, fileId) && pin.point === null).length;
}
