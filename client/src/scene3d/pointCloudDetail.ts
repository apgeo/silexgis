// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DPointCloudDetail } from './scene3dEngine.ts';

/**
 * How much point detail a browser asks the scene for, as a person chooses it.
 *
 * The engine has two dials for a tiled point cloud and no point cap: the screen-space error a
 * tile may show before its children are fetched, and how many bytes of tiles are kept loaded.
 * Nobody wants to type either, so a person picks one of three words and each word is a pair.
 * The pairs are a fact about the machine in front of the person — a phone's graphics memory, a
 * workstation's — which is why the choice is kept per browser and not on the account.
 */
export type PointDetailLevel = 'low' | 'normal' | 'high';

export const POINT_DETAIL_LEVELS: readonly PointDetailLevel[] = ['low', 'normal', 'high'];

const MEBIBYTE = 1024 * 1024;

/**
 * The pairs. `normal` is the engine's own default (16 pixels, 512 MiB). `low` doubles the error
 * a tile may show and keeps a quarter of the tiles, which on a phone is the difference between a
 * scan that can be turned and one that stalls; `high` halves the error and keeps twice the tiles,
 * for a workstation reading a dense wall up close.
 */
export const POINT_DETAIL_BY_LEVEL: Readonly<Record<PointDetailLevel, Scene3DPointCloudDetail>> = {
  low: { maximumScreenSpaceError: 32, cacheBytes: 128 * MEBIBYTE },
  normal: { maximumScreenSpaceError: 16, cacheBytes: 512 * MEBIBYTE },
  high: { maximumScreenSpaceError: 8, cacheBytes: 1024 * MEBIBYTE },
};

/**
 * The level a browser that has chosen none gets: low on a coarse-pointer device, which is how a
 * phone or a tablet announces itself, and normal everywhere else. Decided from the pointer rather
 * than the screen size because a phone's constraint is its graphics memory, and a small window on
 * a workstation has the workstation's.
 */
export function defaultPointDetailLevel(coarsePointer: boolean): PointDetailLevel {
  return coarsePointer ? 'low' : 'normal';
}

/** The pair in force: the chosen level's, or the default's for this kind of device. */
export function pointCloudDetailInForce(
  chosen: PointDetailLevel | undefined,
  coarsePointer: boolean,
): Scene3DPointCloudDetail {
  return POINT_DETAIL_BY_LEVEL[chosen ?? defaultPointDetailLevel(coarsePointer)];
}
