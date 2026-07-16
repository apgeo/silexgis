// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/** Dialogs whose modal-vs-side-panel placement the user can choose. */
export type DialogKind = 'cave-add' | 'feature-edit' | 'entrance-edit';

export type DialogPlacement = 'modal' | 'drawer';

/** Where opening the app bare (no deep link) lands the user. */
export type LandingPage = 'map' | 'dashboard';

interface UiPrefsState {
  /** Feature types pinned as one-click draw shortcuts on the edit toolbar, in pin order. */
  pinnedTypeIds: number[];
  togglePinnedType: (id: number) => void;
  /** Per-dialog placement preference; a missing key means modal (the default). */
  dialogPlacement: Partial<Record<DialogKind, DialogPlacement>>;
  setDialogPlacement: (kind: DialogKind, placement: DialogPlacement) => void;
  /** Hides the on-canvas chrome (search, edit toolbar, scale, pop-out buttons). */
  mapChromeHidden: boolean;
  setMapChromeHidden: (hidden: boolean) => void;
  /** Landing page for a bare app open; the map is the default. */
  landingPage: LandingPage;
  setLandingPage: (page: LandingPage) => void;
}

/**
 * Cross-session personal UI preferences, persisted per browser. Deliberately separate
 * from the session-scoped workspace store: saved views and the workspace bus must never
 * carry — or impose on someone opening a shared view — another user's personal toolbar
 * and chrome setup.
 */
export const useUiPrefsStore = create<UiPrefsState>()(
  persist(
    (set) => ({
      pinnedTypeIds: [],
      togglePinnedType: (id) =>
        set((state) => ({
          pinnedTypeIds: state.pinnedTypeIds.includes(id)
            ? state.pinnedTypeIds.filter((x) => x !== id)
            : [...state.pinnedTypeIds, id],
        })),
      dialogPlacement: {},
      setDialogPlacement: (kind, placement) =>
        set((state) => ({ dialogPlacement: { ...state.dialogPlacement, [kind]: placement } })),
      mapChromeHidden: false,
      setMapChromeHidden: (hidden) => set({ mapChromeHidden: hidden }),
      landingPage: 'map',
      setLandingPage: (page) => set({ landingPage: page }),
    }),
    { name: 'silexgis.uiPrefs', version: 1 },
  ),
);
