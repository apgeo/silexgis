// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useRef } from 'react';
import type { PublicTripRasterMap } from '../api/hooks.ts';
import type { MapStationMarker } from './mapPoints.ts';
import type { MapViewKind } from './vocabulary.ts';

/**
 * The map sheets of a *published* trip, shaped for the panes that draw them.
 *
 * <b>The one surface that cannot read links.</b> Every signed-in sheet is folded out of the
 * model's links (`rasterMaps.ts`, `mapPoints.ts`); a visitor holding a follow link is refused
 * the route those folds read, so the sheets arrive in the envelope already resolved — which
 * map, which rendering, which points on that very rendering, duplicates already folded. What
 * is left here is shaping, and it lives beside the folds it mirrors rather than inside either
 * page, so the followed page and the embed cannot come to disagree about how a sheet is built.
 *
 * <b>Nothing here filters.</b> A page that dropped a sheet the envelope carried would be
 * re-deciding on the client what the server decided on the server, and nothing a stranger's
 * browser decides could be trusted anyway. Every withholding is upstream; this draws what it
 * was sent. The one defensive reading is of the view kind, whose only use is icon and order:
 * an unknown spelling from a future server falls to 'other' rather than to a crash.
 */

/** One published sheet: a tab on the public page and the embed. */
export interface PublishedSheet {
  /**
   * The sheet's identity across re-reads of the envelope: which rendering (the delivery URL
   * with its expiring signature left off) as which view. Stable while it is the same sheet,
   * which is what tab keys and the restamp below both need; the signature is precisely what
   * is expected to differ between two readings of one sheet.
   */
  key: string;
  title: string | null;
  viewKind: MapViewKind;
  /** The rendering's signed URL — restamped in place across re-reads, see below. */
  imageUrl: string;
  /**
   * The envelope's points as the drawing components consume them. The link identities a
   * signed-in marker carries do not exist here — the envelope names no links, on purpose —
   * so the member id is the station (unique per sheet: the server folds duplicates) and
   * `mayEdit` is false, which for a visitor is not a placeholder but the truth.
   */
  markers: MapStationMarker[];
}

const VIEW_KINDS: readonly MapViewKind[] = ['plan', 'profile', 'other'];

function viewKindOf(sent: string): MapViewKind {
  return (VIEW_KINDS as readonly string[]).includes(sent) ? (sent as MapViewKind) : 'other';
}

/** Which rendering a delivery URL delivers, ignoring the signature that expires. */
function sheetIdentity(imageUrl: string): string {
  return imageUrl.split('?')[0];
}

/** The envelope's sheets, in the order the envelope states — the server already sorts them. */
export function sheetsFromEnvelope(maps: readonly PublicTripRasterMap[]): PublishedSheet[] {
  return maps.map((map) => ({
    key: `${map.viewKind}:${sheetIdentity(map.imageUrl)}`,
    title: map.title,
    viewKind: viewKindOf(map.viewKind),
    imageUrl: map.imageUrl,
    markers: map.points.map((point) => ({
      station: point.station,
      x: point.x,
      y: point.y,
      linkId: '',
      memberId: point.station,
      mayEdit: false,
    })),
  }));
}

/**
 * The same sheets again, as the objects the page is already holding.
 *
 * The problem is the station pictures' problem and this is the same answer (see
 * `restampStationMedia`): a published page re-reads its envelope for as long as it carries
 * signed URLs, every read re-signs them, and a derivation alone would hand the tabs a new
 * array of new objects once a minute. So while a read carries <em>the same sheets with the
 * same points</em>, the array the page holds is kept and each sheet's `imageUrl` is restamped
 * in place — a not-yet-opened tab reads the freshest signature at the moment it first mounts,
 * and nothing that consumes the array's identity recomputes for a poll that changed nothing.
 *
 * What this deliberately does <b>not</b> keep fresh is a sheet already drawn: the pane pins
 * the URL it built its picture from (the model-URL rule, `usePinnedModelUrl`) because the
 * picture is already in the browser and reloading it would throw away the reader's pan for a
 * signature nothing will ever spend again.
 *
 * Anything else — a sheet declared or undeclared, a point placed, moved or re-measured, a
 * re-titled map — is a different set of sheets and is handed over as one, tabs rebuilt.
 */
export function restampPublishedSheets(
  held: PublishedSheet[] | undefined,
  fresh: PublishedSheet[],
): PublishedSheet[] {
  if (held === undefined || !sameSheets(held, fresh)) {
    return fresh;
  }
  fresh.forEach((sheet, index) => {
    held[index].imageUrl = sheet.imageUrl;
  });
  return held;
}

/** Whether two derivations are the same sheets, in the same order, with the same points. */
function sameSheets(held: readonly PublishedSheet[], fresh: readonly PublishedSheet[]): boolean {
  if (held.length !== fresh.length) {
    return false;
  }
  return fresh.every((sheet, index) => {
    const holding = held[index];
    return (
      holding.key === sheet.key
      && holding.title === sheet.title
      && holding.markers.length === sheet.markers.length
      && sheet.markers.every((marker, at) => {
        const held_ = holding.markers[at];
        return held_.station === marker.station && held_.x === marker.x && held_.y === marker.y;
      })
    );
  });
}

/**
 * The derivation and the restamp together, for the two pages that need them — the same
 * arrangement `usePublishedStationMedia` gives the pictures, for the same reason: memoising
 * the derivation alone would still yield a new object per envelope read.
 */
export function usePublishedSheets(
  maps: readonly PublicTripRasterMap[] | undefined,
): PublishedSheet[] {
  const held = useRef<PublishedSheet[] | undefined>(undefined);
  return useMemo(() => {
    held.current = restampPublishedSheets(held.current, sheetsFromEnvelope(maps ?? []));
    return held.current;
  }, [maps]);
}
