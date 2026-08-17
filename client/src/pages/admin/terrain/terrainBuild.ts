// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TerrainBuild, TerrainBuildPhase, TerrainBuildStatus } from '../../../api/hooks.ts';
import type { TerrainBbox } from './terrainArea.ts';

/** How many builds a page of the list holds. Shared so the page above it asks for the same page. */
export const TERRAIN_PAGE_SIZE = 10;

/**
 * What an operator has to do to give this installation something that can make tiles.
 *
 * Written out here, in the two lines they are actually typed as, rather than described. The
 * setting goes in the environment file beside the compose files; the command is run from that same
 * directory. A person told only that "the worker is not enabled" has been told nothing they can
 * act on, and this is the whole difference between a first-class state and an error message.
 */
export const TERRAIN_BAKE_SETTING = 'SILEXGIS__Terrain__BakeEnabled=true';
export const TERRAIN_WORKER_COMMAND =
  'docker compose -f docker-compose.yml -f docker-compose.terrain-worker.yml up -d';

/**
 * Whether a build stopped because this installation has nothing that can turn rasters into tiles.
 *
 * This is the only signal there is. Nothing the server publishes says in advance whether the bake
 * service is running, so the absence is learned from a build that reached the bake and found
 * nothing there — which is why this is read off a row rather than caught from a refused request.
 * A plain installation does not run that service, so this is the ordinary case rather than a rare
 * one, and it is presented as a state of the installation instead of as a failure of the build.
 */
export function bakeWorkerMissing(build: TerrainBuild): boolean {
  return build.status === 'failed' && build.errorCode === 'terrain_build.bake_unavailable';
}

/**
 * The steps a build goes through, in the order it goes through them.
 *
 * "Pending" is deliberately absent: it is not a step, it is the state of a build that has not
 * started one, and drawing it as the first bead would make a queued build look as though something
 * were already happening to it. Each step consumes what the one before produced, so the order is
 * the pipeline rather than a presentation choice.
 */
export const TERRAIN_PHASE_ORDER: readonly TerrainBuildPhase[] = [
  'fetch',
  'prepare',
  'bake',
  'validate',
  'publish',
];

/**
 * Which bead of the pipeline is lit, or -1 for a build that has not started.
 *
 * A phase only ever moves forward, and a build can stop at any of them, so a step the build never
 * entered is simply behind the current one. Nothing here assumes every step was visited.
 */
export function phaseIndex(phase: TerrainBuildPhase): number {
  return TERRAIN_PHASE_ORDER.indexOf(phase);
}

/** A run that has stopped, whichever way it stopped. */
export function isSettled(status: TerrainBuildStatus): boolean {
  return status === 'succeeded' || status === 'failed';
}

/**
 * Whether this build can be made the terrain the scene draws.
 *
 * Finishing is not the test. A build is choosable only if it stamped a version on what it
 * published and those tiles are still where terrain is served from — the server checks both and
 * refuses with a code of its own. The version is the half of that test this side can see, so a
 * build without one is not offered; the other half is why the refusal still has to be worded.
 */
export function mayActivate(build: TerrainBuild): boolean {
  return !build.isActive && build.pyramidVersion !== null;
}

/**
 * The rectangle a build covers, read back out of the geometry the server returns.
 *
 * The submit request takes four numbers and the answer comes back as a geometry, so the two ends
 * of the same rectangle have different shapes. Coordinates arrive as arrays nested to whatever
 * depth the geometry type implies, so this walks them rather than assuming a ring.
 */
export function geometryBbox(geometry: { coordinates?: unknown } | null): TerrainBbox | null {
  const bounds = { west: Infinity, south: Infinity, east: -Infinity, north: -Infinity };
  let seen = false;

  const walk = (node: unknown): void => {
    if (!Array.isArray(node)) {
      return;
    }
    if (typeof node[0] === 'number' && typeof node[1] === 'number') {
      const [lon, lat] = node as number[];
      bounds.west = Math.min(bounds.west, lon);
      bounds.east = Math.max(bounds.east, lon);
      bounds.south = Math.min(bounds.south, lat);
      bounds.north = Math.max(bounds.north, lat);
      seen = true;
      return;
    }
    for (const child of node) {
      walk(child);
    }
  };

  walk(geometry?.coordinates);
  return seen ? [bounds.west, bounds.south, bounds.east, bounds.north] : null;
}

/**
 * How long a build took, or how long it has been going.
 *
 * The units are written as symbols rather than words because a bake ranges from eleven seconds to
 * several hours and the row has to stay narrow either way; "s", "min" and "h" read the same in
 * every language this application ships in, which is why this one string is not translated.
 */
export function formatBuildDuration(build: TerrainBuild, now: number = Date.now()): string | null {
  if (build.startedAt === null) {
    return null;
  }
  const started = Date.parse(build.startedAt);
  const ended = build.finishedAt === null ? now : Date.parse(build.finishedAt);
  const seconds = Math.max(0, Math.round((ended - started) / 1000));
  if (seconds < 60) {
    return `${seconds} s`;
  }
  if (seconds < 3600) {
    return `${Math.floor(seconds / 60)} min`;
  }
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  return minutes === 0 ? `${hours} h` : `${hours} h ${minutes} min`;
}

/**
 * Bytes as the shortest unit that keeps the number readable.
 *
 * Written here rather than borrowed from the document side, whose version rounds everything below
 * a megabyte up to at least one kilobyte — harmless for an attachment, wrong for a build that has
 * so far written nothing.
 */
export function formatBuildSize(bytes: number | null): string {
  if (bytes === null) {
    return '';
  }
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
}
