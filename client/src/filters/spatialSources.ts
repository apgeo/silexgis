// SPDX-License-Identifier: AGPL-3.0-or-later
import { createContext, useContext } from 'react';

/**
 * Places a surface can hand to a spatial row besides the ones typed or picked in the editor. The
 * map provides them (the ground it shows, an outline drawn on it, the object selected on it); every
 * other surface provides nothing, and its rows offer only the typed and picked forms.
 *
 * What is handed over is only ever a plain value for the row to hold — a ring of positions, or an
 * object's identifier — so the server runs it exactly as it would the same value typed in: the
 * outline through the shown-position test, the object through the placement gate.
 */
export interface SpatialSources {
  /** The ground now on screen, as the rings of an outline; null before the map has a size. */
  currentView: () => number[][][] | null;
  /**
   * Lets the person draw an outline on the surface. Resolves with its rings, or null when they gave
   * up. The editor stays mounted meanwhile, so the row that asked receives the answer.
   */
  drawOutline: () => Promise<number[][][] | null>;
  /** The feature selected on the surface, when one is. */
  selectedFeatureId: string | null;
}

export const SpatialSourcesContext = createContext<SpatialSources | null>(null);

export function useSpatialSources(): SpatialSources | null {
  return useContext(SpatialSourcesContext);
}
