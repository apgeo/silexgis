// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { FlybyDocument } from '../flyby/flybyDocument.ts';
import { normalizeFlybyLibrary, withFlybySaved, withoutFlyby } from '../flyby/flybyLibrary.ts';

interface FlybyStoreState {
  /** The flybys kept in this browser, newest first. */
  library: FlybyDocument[];
  /** Whether a flyby's route is visited before it is played, so the flight finds it loaded. */
  loadRoute: boolean;
  save(document: FlybyDocument): void;
  remove(name: string): void;
  setLoadRoute(loadRoute: boolean): void;
}

/**
 * The flybys a person has kept, per browser.
 *
 * Apart from the other per-browser preferences on purpose: a flyby is a document, not a setting,
 * and a list of them is far larger than everything else remembered here put together — kept under
 * the same key, saving one flyby would rewrite every preference, and a full storage would lose
 * both. What is read back is checked entry by entry, so a list written by an older or a newer
 * build opens with the flybys this build can play rather than not at all.
 */
export const useFlybyStore = create<FlybyStoreState>()(
  persist(
    (set) => ({
      library: [],
      loadRoute: true,
      save: (document) => set((state) => ({ library: withFlybySaved(state.library, document) })),
      remove: (name) => set((state) => ({ library: withoutFlyby(state.library, name) })),
      setLoadRoute: (loadRoute) => set({ loadRoute }),
    }),
    {
      name: 'silexgis.flybys',
      version: 1,
      partialize: (state) => ({ library: state.library, loadRoute: state.loadRoute }),
      merge: (persisted, current) => {
        const stored = (persisted ?? {}) as { library?: unknown; loadRoute?: unknown };
        return {
          ...current,
          library: normalizeFlybyLibrary(stored.library),
          loadRoute: typeof stored.loadRoute === 'boolean' ? stored.loadRoute : current.loadRoute,
        };
      },
    },
  ),
);
