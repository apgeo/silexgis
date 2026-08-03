// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DPick } from './scene3dEngine.ts';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';

// What clicking something in the scene means.
//
// The scene hands back, by reference, whatever object was attached to the item when it was added
// to a source. So the loaders attach one of the payloads below, and clicking is then a decision
// table over a discriminated union rather than a lookup through the renderer: no map from a
// graphics object to a database row, and no chance of the two drifting apart.
//
// The payloads are deliberately not `WorkspaceSelection` values themselves. Two of the four would
// be identical, but a centerline has no selection kind of its own — clicking a survey line selects
// the cave it belongs to — and the store's contract is that it holds bare references, so anything
// carried for the scene's own use has to be dropped on the way in rather than stored.

/** A single cave entrance, as the entrance overlay serves it. */
export interface EntrancePick {
  kind: 'entrance';
  entranceId: string;
  caveId: string;
}

/** A feature from the cross-kind overlay. */
export interface FeaturePick {
  kind: 'feature';
  featureId: string;
}

/** One line of a cave's survey; selecting it selects the cave, which is what a viewer means. */
export interface CenterlinePick {
  kind: 'centerline';
  caveId: string;
  centerlineId: string;
}

/**
 * A server-side aggregation of several entrances. It carries the zoom the aggregation was
 * requested at, because that zoom is what decided the size of the cell the server summed over:
 * asking for the members at any other zoom covers a different patch of ground and answers with a
 * different set of caves.
 */
export interface ClusterPick {
  kind: 'cluster';
  lon: number;
  lat: number;
  count: number;
  zoom: number;
}

export type Scene3DPickPayload = EntrancePick | FeaturePick | CenterlinePick | ClusterPick;

/**
 * The workspace selection a pick stands for, or null when the click landed on nothing the
 * application knows about — bare ground, the sky, or an item from some other source.
 */
export function selectionFromPick(pick: Scene3DPick | null): WorkspaceSelection | null {
  const payload = pickPayload(pick);
  if (!payload) {
    return null;
  }
  switch (payload.kind) {
    case 'entrance':
      return { kind: 'entrance', entranceId: payload.entranceId, caveId: payload.caveId };
    case 'feature':
      return { kind: 'feature', featureId: payload.featureId };
    case 'centerline':
      return { kind: 'cave', caveId: payload.caveId };
    case 'cluster':
      return {
        kind: 'cluster',
        lon: payload.lon,
        lat: payload.lat,
        count: payload.count,
        zoom: payload.zoom,
      };
  }
}

/**
 * The payload behind a pick, if it is one of ours. Everything a scene can be asked to pick against
 * ends up here, so the shape is checked rather than asserted — a stray primitive from some later
 * batch must read as "nothing selectable", not crash the click handler.
 */
export function pickPayload(pick: Scene3DPick | null): Scene3DPickPayload | undefined {
  const id = pick?.id;
  if (typeof id !== 'object' || id === null || !('kind' in id)) {
    return undefined;
  }
  // Read as a bare bag rather than as the union: the four members disagree about every field but
  // `kind`, so narrowing has to come from the checks below and not from the declared type.
  const bag = id as Record<string, unknown>;
  const { entranceId, caveId, featureId, centerlineId, lon, lat, count, zoom } = bag;
  switch (bag.kind) {
    case 'entrance':
      return typeof entranceId === 'string' && typeof caveId === 'string'
        ? { kind: 'entrance', entranceId, caveId }
        : undefined;
    case 'feature':
      return typeof featureId === 'string' ? { kind: 'feature', featureId } : undefined;
    case 'centerline':
      return typeof caveId === 'string' && typeof centerlineId === 'string'
        ? { kind: 'centerline', caveId, centerlineId }
        : undefined;
    case 'cluster':
      return typeof lon === 'number' &&
        typeof lat === 'number' &&
        typeof count === 'number' &&
        typeof zoom === 'number'
        ? { kind: 'cluster', lon, lat, count, zoom }
        : undefined;
    default:
      return undefined;
  }
}
