// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/** Dialogs whose modal-vs-side-panel placement the user can choose. */
export type DialogKind = 'cave-add' | 'feature-edit' | 'entrance-edit' | 'reslink-add-member';

export type DialogPlacement = 'modal' | 'drawer';

/** Where opening the app bare (no deep link) lands the user. */
export type LandingPage = 'map' | 'dashboard';

export type ThemePref = 'system' | 'light' | 'dark';

export type DensityPref = 'comfortable' | 'compact';

/** Appearance choices that must be honoured before anything is painted. */
export interface Appearance {
  theme: ThemePref;
  density: DensityPref;
  reduceMotion: boolean;
}

export const DEFAULT_APPEARANCE: Appearance = {
  theme: 'system',
  density: 'comfortable',
  reduceMotion: false,
};

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
  /**
   * Personal centerline-overlay limits, overriding the installation's. A fast workstation can
   * pull full detail in earlier (lower zoom) and raise the path budget; a phone can push both
   * the other way. Undefined means "follow the server". The server bounds whatever is asked for.
   */
  centerlineDetailZoom?: number;
  centerlineMaxPaths?: number;
  setCenterlineLimits: (limits: { detailZoom?: number; maxPaths?: number }) => void;
  /**
   * Local mirror of the appearance settings the server also holds. The mirror is what paints —
   * it rehydrates synchronously, so the first render already has the right theme — while the
   * server copy is what carries the choice to another machine.
   */
  appearance: Appearance;
  setAppearance: (patch: Partial<Appearance>) => void;
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
      centerlineDetailZoom: undefined,
      centerlineMaxPaths: undefined,
      setCenterlineLimits: ({ detailZoom, maxPaths }) =>
        set({ centerlineDetailZoom: detailZoom, centerlineMaxPaths: maxPaths }),
      appearance: DEFAULT_APPEARANCE,
      setAppearance: (patch) => set((state) => ({ appearance: { ...state.appearance, ...patch } })),
    }),
    {
      name: 'silexgis.uiPrefs',
      version: 2,
      // Without a migrate, raising the version makes zustand discard the whole stored blob —
      // wiping everyone's pinned types, landing page and centerline budgets to add one field.
      migrate: (persisted, from) =>
        from < 2
          ? { ...(persisted as UiPrefsState), appearance: DEFAULT_APPEARANCE }
          : (persisted as UiPrefsState),
    },
  ),
);
