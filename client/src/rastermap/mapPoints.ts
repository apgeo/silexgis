// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { readRegion, type ImageRegion } from '../imagelink/regions.ts';
import { MAP_STATION_POINT_CODE } from './vocabulary.ts';

/**
 * The station pins defined on one map, read out of the links the model already has.
 *
 * A pin is one link under the seeded pin code, pairing a point on the map document
 * (an image-region anchor in fractions, pinned to the file it was measured against) with a
 * station of the survey model (a model-station anchor). The pair *is* the pin: neither half
 * means anything alone.
 *
 * <b>Code AND structure are both required.</b> The code alone must not turn a mislabeled
 * link into a marker host, and structure alone must not promote a casual region↔station
 * annotation — authored through the generic link flows without the pin code — into a
 * calibration-grade position claim. Links that fail either check remain ordinary links and
 * keep working everywhere links work; they are simply not pins.
 *
 * <b>Markers derive only from point-shaped pins on the file being looked at.</b> Rect,
 * circle and polygon payloads are legal under the pin code — reserved for a future
 * "approximate area" variant — and are never drawn as station markers. A point measured
 * against a superseded scan is surfaced as a count, never as a silently mislocated marker:
 * fractions of last year's scan mean nothing on this year's.
 */

/** One pin as stored: a region of a specific file of the map document, naming a station. */
export interface MapPin {
  linkId: string;
  /** The document member carrying the region — the pin's identity within its link. */
  memberId: string;
  station: string;
  region: ImageRegion;
  /** The file the region's fractions were measured against. */
  anchorFileId: string;
  createdAt: string;
}

/** One drawable marker: a station at a point of the picture on screen, in fractions. */
export interface MapStationMarker {
  station: string;
  x: number;
  y: number;
  linkId: string;
  memberId: string;
}

/** Whether a member anchors its link to a station of the model on screen, and which. */
function stationOf(member: ResLinkMember, surveyModelId: string): string | null {
  if (
    member.targetType !== 'surveyModel'
    || member.targetId !== surveyModelId
    || member.anchorKind !== 'modelStation'
  ) {
    return null;
  }
  // The anchor payload is withheld from a reader who may not read it, and arrives as null;
  // a marker placed at a guessed station is the failure this module is arranged to avoid.
  const anchor = (typeof member.anchor === 'object' && member.anchor !== null ? member.anchor : {}) as Record<
    string,
    unknown
  >;
  const station = anchor.station;
  return typeof station === 'string' && station.length > 0 ? station : null;
}

/**
 * Every pin defined on one map document for one model. Pins whose region payload is
 * withheld or unreadable are dropped here rather than guessed at, exactly as the generic
 * region highlights drop them.
 */
export function mapPinsFromLinks(
  links: readonly ResLink[],
  surveyModelId: string,
  documentId: string,
): MapPin[] {
  const pins: MapPin[] = [];

  for (const link of links) {
    if (link.relationType?.code !== MAP_STATION_POINT_CODE) {
      continue;
    }

    // The structural pair, gathered defensively: the designed shape is one region member
    // and one station member, but a link is free-form membership, so every valid pairing
    // it happens to carry is read as a pin rather than one of them being silently chosen.
    const stations: string[] = [];
    const regionMembers: { memberId: string; region: ImageRegion; anchorFileId: string }[] = [];
    for (const member of link.members) {
      const station = stationOf(member, surveyModelId);
      if (station !== null) {
        stations.push(station);
        continue;
      }
      if (
        member.targetType !== 'document'
        || member.targetId !== documentId
        || member.anchorKind !== 'imageRegion'
        || member.anchorFileId == null
      ) {
        continue;
      }
      const region = readRegion(member.anchor);
      if (region === null) {
        continue;
      }
      regionMembers.push({ memberId: member.id, region, anchorFileId: member.anchorFileId });
    }

    for (const regionMember of regionMembers) {
      for (const station of stations) {
        pins.push({
          linkId: link.id,
          memberId: regionMember.memberId,
          station,
          region: regionMember.region,
          anchorFileId: regionMember.anchorFileId,
          createdAt: link.createdAt,
        });
      }
    }
  }

  return pins;
}

/**
 * The markers to draw over one rendering of the map: point-shaped pins measured against
 * the file on screen, one per station.
 *
 * One pin per (station, map) is a fold rule rather than a database constraint — the
 * generic link schema has no home for a per-pair unique index — so duplicates are a state
 * this fold has to answer for: the newest pin wins, deterministically, and the older ones
 * stay visible in the generic links panel rather than fighting over one marker.
 */
export function stationMarkers(pins: readonly MapPin[], fileId: string): MapStationMarker[] {
  const byStation = new Map<string, MapPin>();
  for (const pin of pins) {
    if (pin.region.shape !== 'point' || pin.anchorFileId !== fileId) {
      continue;
    }
    const held = byStation.get(pin.station);
    if (
      held === undefined
      || pin.createdAt > held.createdAt
      || (pin.createdAt === held.createdAt && pin.linkId > held.linkId)
    ) {
      byStation.set(pin.station, pin);
    }
  }

  return [...byStation.values()]
    .map((pin) => ({
      station: pin.station,
      x: (pin.region as { x: number }).x,
      y: (pin.region as { y: number }).y,
      linkId: pin.linkId,
      memberId: pin.memberId,
    }))
    .sort((a, b) => a.station.localeCompare(b.station));
}

/**
 * How many point pins were measured against some other file of this document — points
 * placed on an older (or newer) scan than the rendering on screen. A count and nothing
 * more: which file each was measured against is version-history information the fold has
 * no business surfacing, and a marker drawn from another file's fractions would sit in a
 * right-looking place it was never measured at.
 */
export function supersededPointCount(pins: readonly MapPin[], fileId: string): number {
  return pins.filter((pin) => pin.region.shape === 'point' && pin.anchorFileId !== fileId).length;
}
