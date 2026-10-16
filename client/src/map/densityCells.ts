// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../api/client.ts';

// Which cell size to ask the density grid for, shared by every screen that draws it — and the
// one search that asks for it, so the map overlay and an area's own card cannot drift into two
// ideas of what to do when the floor does not fit a window.
//
// The server decides the finest cell it will publish — its location-protection grid — and how
// many cells one answer may hold. Neither number is a client constant: both arrive in an answer
// (or, for a first request over a window too wide to answer at the floor, in the refusal), and
// everything here works from those two published numbers. A screen offers or chooses a multiple
// of the floor, never a size of its own, so it cannot ask for something finer than the server
// publishes.
//
// Multiples are powers of two. The server's lattice is anchored at zero rather than at the
// window, so a cell twice the floor holds exactly the cells of the floor below it only when every
// step doubles; a reader zooming out then sees cells merge rather than shift sideways.

/** Two numbers the server publishes about its grid, enough to choose a cell that fits a window. */
export interface DensityGridLimits {
  /** The finest cell this installation publishes, in metres. */
  minimumCellMetres: number;
  /** The most cells one answer holds. */
  maxCells: number;
}

/** Window in degrees: west, south, east, north. */
export type LonLatBox = readonly [number, number, number, number];

/**
 * The same metres-to-degrees conversion the server's protection grid uses (one size on both
 * axes, from a degree of latitude). Not a better one: a client count that disagreed with the
 * server's would choose a cell the server then refuses.
 */
const METRES_PER_DEGREE = 111_320;

/** The multiples a reader may choose between on an area's own card. */
export const DENSITY_CELL_MULTIPLES = [1, 2, 4, 8] as const;

/**
 * How far the map overlay will step before saying the window is too wide. 2^12 times a
 * kilometre-scale floor is already a cell wider than a country; a window that needs more is one
 * the overlay asks the reader to zoom into.
 */
const MAX_DOUBLINGS = 12;

/** Which of the three readings the map's density cells are filled by. */
export type KarstDensityMode = 'density' | 'kernel' | 'hotSpot';

export const KARST_DENSITY_MODES: readonly KarstDensityMode[] = ['density', 'kernel', 'hotSpot'];

export function isKarstDensityMode(value: unknown): value is KarstDensityMode {
  return typeof value === 'string' && (KARST_DENSITY_MODES as readonly string[]).includes(value);
}

/** A cell size, in metres, that is a multiple of the published floor. */
export function densityCellMetres(minimumCellMetres: number, multiple: number): number {
  return minimumCellMetres * multiple;
}

function cellRange(min: number, max: number, cellDegrees: number): number {
  // Index i spans (i - 0.5) .. (i + 0.5) cells, so each end of the window falls inside a cell.
  const first = Math.ceil(min / cellDegrees - 0.5);
  const last = Math.floor(max / cellDegrees + 0.5);
  return Math.max(last, first) - first + 1;
}

/** How many cells the server will enumerate for a window at a cell size — its own count. */
export function densityCellCount(box: LonLatBox, cellMetres: number): number {
  const cellDegrees = cellMetres / METRES_PER_DEGREE;
  const [west, south, east, north] = box;
  return cellRange(west, east, cellDegrees) * cellRange(south, north, cellDegrees);
}

/**
 * The finest power-of-two multiple of the floor at which a window fits in one answer, or null
 * when no multiple within reach does (the reader is looking at too much of the world at once).
 */
export function fittingCellMultiple(box: LonLatBox, limits: DensityGridLimits): number | null {
  for (let step = 0, multiple = 1; step <= MAX_DOUBLINGS; step += 1, multiple *= 2) {
    if (densityCellCount(box, densityCellMetres(limits.minimumCellMetres, multiple)) <= limits.maxCells) {
      return multiple;
    }
  }
  return null;
}

/** The next coarser multiple, for a server that still found the window too large. */
export function coarserCellMultiple(multiple: number): number | null {
  const next = multiple * 2;
  return next <= 2 ** MAX_DOUBLINGS ? next : null;
}

/**
 * The limits a too-many-cells refusal carries, when it carries both. A first request over a wide
 * window has no answer to learn them from, so the refusal states them instead.
 */
export function limitsFromRefusal(problem: Readonly<Record<string, unknown>> | undefined): DensityGridLimits | null {
  const floor = problem?.minimumCellMetres;
  const maxCells = problem?.maxCells;
  return typeof floor === 'number' && floor > 0 && typeof maxCells === 'number' && maxCells > 0
    ? { minimumCellMetres: floor, maxCells }
    : null;
}

/** A map window as the server accepts one: inside the world, west before east. */
export function clampToWorld(box: LonLatBox): LonLatBox {
  const clamp = (value: number, limit: number) => Math.min(limit, Math.max(-limit, value));
  return [clamp(box[0], 180), clamp(box[1], 90), clamp(box[2], 180), clamp(box[3], 90)];
}

/** A window as the density route takes it, formatted the one way every caller sends it. */
export function densityBboxParam(box: LonLatBox): string {
  return clampToWorld(box)
    .map((n) => n.toFixed(5))
    .join(',');
}

/** The part of a density request the step search decides; a caller adds the rest (an area). */
export interface DensityStepRequest {
  bbox: string;
  cellMetres?: number;
}

export type SteppedDensityAnswer<G extends DensityGridLimits> =
  | { kind: 'grid'; grid: G; limits: DensityGridLimits }
  | { kind: 'refused'; code: string; limits: DensityGridLimits | null };

/** The code the step search answers with when no multiple fits the window. */
export const TOO_MANY_CELLS = 'density.too_many_cells';

/**
 * Asks for a window's grid at the finest cell that fits it.
 *
 * Without known limits the first request names no cell, and the answer (or a too-many-cells
 * refusal, which carries the same two numbers) states the floor and the cell limit. With them the
 * cell is chosen before asking. A server that still finds the window too large — its count is the
 * authority, this one is a forecast — is asked again one doubling coarser, a bounded number of
 * times. Any other refusal is the answer and is reported as itself, never as an empty grid.
 */
export async function fetchSteppedDensity<G extends DensityGridLimits>(
  box: LonLatBox,
  limits: DensityGridLimits | null,
  fetchGrid: (request: DensityStepRequest, signal?: AbortSignal) => Promise<G>,
  signal?: AbortSignal,
): Promise<SteppedDensityAnswer<G>> {
  const bbox = densityBboxParam(box);
  let known = limits;
  let multiple: number | null = known ? fittingCellMultiple(clampToWorld(box), known) : null;
  if (known && multiple === null) {
    return { kind: 'refused', code: TOO_MANY_CELLS, limits: known };
  }

  for (let attempt = 0; attempt < 4; attempt += 1) {
    const cellMetres = known && multiple !== null ? densityCellMetres(known.minimumCellMetres, multiple) : undefined;
    try {
      const grid = await fetchGrid({ bbox, cellMetres }, signal);
      return {
        kind: 'grid',
        grid,
        limits: { minimumCellMetres: grid.minimumCellMetres, maxCells: grid.maxCells },
      };
    } catch (error) {
      if (!(error instanceof ApiError) || error.status < 400 || error.status >= 500 || !error.code) {
        throw error;
      }
      if (error.code !== TOO_MANY_CELLS) {
        return { kind: 'refused', code: error.code, limits: known };
      }
      const stated = limitsFromRefusal(error.problem);
      if (stated && !known) {
        known = stated;
        multiple = fittingCellMultiple(clampToWorld(box), stated);
      } else {
        multiple = multiple === null ? null : coarserCellMultiple(multiple);
      }
      if (!known || multiple === null) {
        return { kind: 'refused', code: TOO_MANY_CELLS, limits: known };
      }
    }
  }
  return { kind: 'refused', code: TOO_MANY_CELLS, limits: known };
}

/**
 * The multiple of the floor a stepped answer was taken at: one when the floor fitted, a power of
 * two above it when the window was too wide for the floor. Coarser choices are offered from here,
 * so none of them is a cell the search already found too fine for the window.
 */
export function answeredCellMultiple(grid: DensityGridLimits & { cellMetres: number }): number {
  return Math.max(1, Math.round(grid.cellMetres / grid.minimumCellMetres));
}
