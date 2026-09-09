// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { SelectorPrefs } from '../filters/selectorPrefs.ts';
import type { DensityPref, PanelLayout, PanelPrefs, PanelScope } from './panelPrefs.ts';

export type { DensityPref };

/** Dialogs whose modal-vs-side-panel placement the user can choose. */
export type DialogKind = 'cave-add' | 'feature-edit' | 'entrance-edit' | 'reslink-add-member';

export type DialogPlacement = 'modal' | 'drawer';

/** Where opening the app bare (no deep link) lands the user. */
export type LandingPage = 'map' | 'dashboard';

export type ThemePref = 'system' | 'light' | 'dark';

/**
 * What an interchange export may be asked to do with a cave position this installation
 * protects. The same three words the server's closed vocabulary uses, and deliberately no
 * fourth: the surveyed position is not among them, and a value stored here is only ever a
 * suggestion the server re-checks before it writes a byte.
 */
export type ProtectedPositionTreatment = 'no_position' | 'omit' | 'grid_position';

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
  /**
   * The answer this person settled on for protected positions when exporting for interchange,
   * so somebody who has already decided is not asked the same question every time. Undefined
   * means they have not decided and are asked. It is a remembered preference and never an
   * authority: the server refuses an export that names nothing valid, whatever is stored here.
   */
  karstLinkTreatment?: ProtectedPositionTreatment;
  setKarstLinkTreatment: (treatment: ProtectedPositionTreatment | undefined) => void;
  /**
   * How each selection panel is arranged, keyed by which panel it is. Every mount keeps its own,
   * so a pop-out somebody set up to show one thing is not rearranged by the main window.
   */
  panels: Partial<Record<PanelScope, PanelPrefs>>;
  setPanelPrefs: (scope: PanelScope, patch: Partial<PanelPrefs>) => void;
  /**
   * How each object selector was last arranged, keyed by where it is mounted.
   *
   * Per mount, because the picker in the links card and the one in the permissions dialog are used
   * for different things by the same person — sharing would mean fixing one every time you used
   * the other. What is kept is how somebody likes to look, never what they searched for: a stored
   * query would be a record of what a person was looking for, sitting in a browser they may share.
   */
  selectors: Record<string, SelectorPrefs>;
  setSelectorPrefs: (key: string, patch: Partial<SelectorPrefs>) => void;
  /** Named arrangements of the whole workspace — chrome, not place. */
  layouts: PanelLayout[];
  saveLayout: (name: string, mapChromeHidden: boolean) => void;
  applyLayout: (id: string) => void;
  deleteLayout: (id: string) => void;
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
      karstLinkTreatment: undefined,
      setKarstLinkTreatment: (treatment) => set({ karstLinkTreatment: treatment }),
      selectors: {},
      setSelectorPrefs: (key, patch) =>
        set((state) => ({
          selectors: { ...state.selectors, [key]: { ...state.selectors[key], ...patch } },
        })),
      panels: {},
      setPanelPrefs: (scope, patch) =>
        set((state) => ({
          panels: { ...state.panels, [scope]: { ...state.panels[scope], ...patch } },
        })),
      layouts: [],
      saveLayout: (name, mapChromeHidden) =>
        set((state) => {
          // Saving under a name that already exists replaces it, which is what "save" means to
          // somebody who just pressed it twice; a second entry with the same name would be a
          // list nobody can tell apart.
          const layout: PanelLayout = {
            id: state.layouts.find((l) => l.name === name)?.id ?? crypto.randomUUID(),
            name,
            panels: structuredClone(state.panels),
            mapChromeHidden,
          };
          return {
            layouts: [...state.layouts.filter((l) => l.name !== name), layout].slice(-MAX_LAYOUTS),
          };
        }),
      applyLayout: (id) =>
        set((state) => {
          const layout = state.layouts.find((l) => l.id === id);
          if (!layout) {
            return {};
          }

          // Replaced wholesale rather than merged: a layout is a complete arrangement, and
          // merging would leave a panel the layout says nothing about sitting at whatever the
          // last one left it — which is exactly the state loading a layout is meant to end.
          return {
            panels: structuredClone(layout.panels),
            mapChromeHidden: layout.mapChromeHidden ?? state.mapChromeHidden,
          };
        }),
      deleteLayout: (id) => set((state) => ({ layouts: state.layouts.filter((l) => l.id !== id) })),
    }),
    {
      name: 'silexgis.uiPrefs',
      version: 5,
      // Without a migrate, raising the version makes zustand discard the whole stored blob —
      // wiping everyone's pinned types, landing page and centerline budgets to add one field.
      migrate: (persisted, from) => {
        const state = persisted as UiPrefsState;
        return {
          ...state,
          appearance: from < 2 ? DEFAULT_APPEARANCE : state.appearance,
          panels: from < 3 ? {} : (state.panels ?? {}),
          layouts: from < 3 ? [] : (state.layouts ?? []),
          selectors: from < 4 ? {} : (state.selectors ?? {}),
          karstLinkTreatment: from < 5 ? undefined : state.karstLinkTreatment,
        };
      },
    },
  ),
);

/**
 * A ceiling on saved arrangements. Not a storage limit — the point is that a list somebody picks
 * from stops being useful long before this, and the whole blob travels to the server.
 */
const MAX_LAYOUTS = 20;
