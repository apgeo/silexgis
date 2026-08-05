// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DPick, Scene3DPosition } from './scene3dEngine.ts';
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

/**
 * What a payload carries beyond the reference the store wants, for chrome drawn over the scene.
 *
 * Both fields are what the loader already had in its hands and nothing else has: the name comes
 * from the response's own properties, and the anchor from the position the item was drawn at.
 * Asking for either at the moment something is picked would mean a lookup from a graphics object
 * back to a database row, which is exactly what attaching the payload by reference exists to
 * avoid — and hover asks its question once per drawn frame, which is not a budget for a fetch.
 *
 * Both are optional because the item may genuinely have neither: an unnamed feature of an unknown
 * type has nothing to be called, and a payload shared by a whole cave's worth of survey lines
 * stands at one place rather than at each of them.
 */
export interface Scene3DPickChrome {
  /** What to call this, already composed the way the flat map composes it. */
  label?: string;
  /** Where on the globe to pin something to it. */
  anchor?: Scene3DPosition;
}

/** A single cave entrance, as the entrance overlay serves it. */
export interface EntrancePick extends Scene3DPickChrome {
  kind: 'entrance';
  entranceId: string;
  caveId: string;
}

/** A feature from the cross-kind overlay. */
export interface FeaturePick extends Scene3DPickChrome {
  kind: 'feature';
  featureId: string;
}

/** One line of a cave's survey; selecting it selects the cave, which is what a viewer means. */
export interface CenterlinePick extends Scene3DPickChrome {
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
export interface ClusterPick extends Scene3DPickChrome {
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
 * Whether a workspace selection is still the thing this pick found.
 *
 * Chrome pinned to a pick has to come down when the viewer selects something else, and the
 * selection is the only signal that reaches every way of doing that — a click in this scene, a
 * click on the flat map, a row in a table, another window entirely. Comparing the two is not the
 * same as comparing what was clicked: a survey line and the cave it belongs to are one selection
 * and two different picks.
 */
export function pickMatchesSelection(
  payload: Scene3DPickPayload | undefined,
  selection: WorkspaceSelection | null,
): boolean {
  if (!payload || !selection) {
    return false;
  }
  switch (payload.kind) {
    case 'entrance':
      return selection.kind === 'entrance' && selection.entranceId === payload.entranceId;
    case 'feature':
      return selection.kind === 'feature' && selection.featureId === payload.featureId;
    case 'centerline':
      return selection.kind === 'cave' && selection.caveId === payload.caveId;
    case 'cluster':
      return (
        selection.kind === 'cluster' &&
        selection.lon === payload.lon &&
        selection.lat === payload.lat &&
        selection.zoom === payload.zoom
      );
  }
}

/**
 * Whether two payloads are about the same thing, ignoring everything a reload can change.
 *
 * Chrome pinned to a pick holds the payload it was handed when the thing was clicked, and that
 * payload carries a name and a place — both of which the next load rebuilds. Renaming a feature or
 * moving its geometry therefore leaves a label showing the old name at the old spot beside a panel
 * showing the new one, unless the label can find its own thing again among the new payloads. This
 * is how it recognises it: the identity, and nothing that was composed for display.
 */
export function samePickTarget(a: Scene3DPickPayload, b: Scene3DPickPayload): boolean {
  if (a.kind !== b.kind) {
    return false;
  }
  switch (a.kind) {
    case 'entrance':
      return a.entranceId === (b as EntrancePick).entranceId;
    case 'feature':
      return a.featureId === (b as FeaturePick).featureId;
    case 'centerline':
      return a.centerlineId === (b as CenterlinePick).centerlineId;
    case 'cluster': {
      // A cluster has no identity of its own — it is a count over a patch of ground — so the patch
      // and the zoom that sized it are what makes two of them the same aggregation.
      const other = b as ClusterPick;
      return a.lon === other.lon && a.lat === other.lat && a.zoom === other.zoom;
    }
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
  // Checked the same way as everything else here rather than passed through: what the scene hands
  // back is whatever object was attached to an item, and a later batch attaching a half-built one
  // must read as a pick with no name to show, not put `undefined` on the screen.
  const chrome: Scene3DPickChrome = {
    ...(typeof bag.label === 'string' && bag.label ? { label: bag.label } : {}),
    ...(isPosition(bag.anchor) ? { anchor: bag.anchor } : {}),
  };
  switch (bag.kind) {
    case 'entrance':
      return typeof entranceId === 'string' && typeof caveId === 'string'
        ? { kind: 'entrance', entranceId, caveId, ...chrome }
        : undefined;
    case 'feature':
      return typeof featureId === 'string' ? { kind: 'feature', featureId, ...chrome } : undefined;
    case 'centerline':
      return typeof caveId === 'string' && typeof centerlineId === 'string'
        ? { kind: 'centerline', caveId, centerlineId, ...chrome }
        : undefined;
    case 'cluster':
      return typeof lon === 'number' &&
        typeof lat === 'number' &&
        typeof count === 'number' &&
        typeof zoom === 'number'
        ? { kind: 'cluster', lon, lat, count, zoom, ...chrome }
        : undefined;
    default:
      return undefined;
  }
}

function isPosition(value: unknown): value is Scene3DPosition {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const { longitude, latitude, height } = value as Record<string, unknown>;
  return (
    Number.isFinite(longitude) && Number.isFinite(latitude) && Number.isFinite(height)
  );
}
