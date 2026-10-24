// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { MovieGifCalibration } from '../caveview/movie/movieOutput.ts';
import type { MovieSettings } from '../caveview/movie/movieSettings.ts';
import { DEFAULT_SAVED_FILTER_AUDIENCE, type SavedFilterAudience } from '../filters/savedFilterAudience.ts';
import type { SelectorPrefs } from '../filters/selectorPrefs.ts';
import {
  DEFAULT_COORDINATE_FORMAT,
  DEFAULT_READING_RULES,
  defaultFormatSettings,
  FORMAT_IDS,
  type CoordinateFormat,
  type FormatId,
  type FormatSettings,
  type ReadingRules,
} from '../geo/formats/settings.ts';
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

/** Whether each of the map workspace's two docks is folded away. */
export interface DockPrefs {
  leftHidden: boolean;
  rightHidden: boolean;
}

export const DEFAULT_DOCKS: DockPrefs = { leftHidden: false, rightHidden: false };

interface UiPrefsState {
  /** Feature types pinned as one-click draw shortcuts on the edit toolbar, in pin order. */
  pinnedTypeIds: number[];
  togglePinnedType: (id: number) => void;
  /**
   * Saved filters pinned as one-click chips on the map and the cave registry, in pin order, at
   * most `MAX_PINNED_FILTERS`. Pinning one more than that does nothing: the screens offering a pin
   * say the row is full first.
   */
  pinnedFilterIds: string[];
  togglePinnedFilter: (id: string) => void;
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
   * Personal limits for the 3D view's "walls of every cave in view", overriding the
   * installation's: the zoom it starts from, how many caves it draws and how many bytes of mesh
   * it holds. Per browser for the reason the centerline limits are — the right budget is a fact
   * about the graphics card in front of the person, not about the person. Undefined means
   * "follow this installation's default". Stored as typed and bounded where they are used: the
   * ceilings are the installation's and can change after a value was stored.
   */
  meshesInViewMinZoom?: number;
  meshesInViewMaxCaves?: number;
  meshesInViewMaxBytes?: number;
  setMeshesInViewLimits: (limits: { minZoom?: number; maxCaves?: number; maxBytes?: number }) => void;
  /**
   * The size above which the 3D view asks before downloading the walls of a selected cave, in
   * bytes; 0 asks every time. Per browser, because whether fifty megabytes is a lot is a fact
   * about the connection in front of the person. Undefined follows the installation's default.
   */
  meshAskAboveBytes?: number;
  setMeshAskAboveBytes: (bytes: number | undefined) => void;
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
   *
   * The person's, not the browser's: it is an answer about what to disclose, and on a shared
   * laptop the next member must be asked again rather than handed the last one's answer — so it
   * travels with the account like the workspace arrangement.
   */
  karstLinkTreatment?: ProtectedPositionTreatment;
  setKarstLinkTreatment: (treatment: ProtectedPositionTreatment | undefined) => void;
  /**
   * Who a filter this person saves is shared with unless they choose otherwise at the save — only
   * themselves until they pick something else in Settings. The account's, not the browser's: on a
   * shared laptop the next member must not start out sharing with the last one's club.
   * A default only: every save dialog shows the choice, and the server stores what is sent.
   */
  savedFilterAudience: SavedFilterAudience;
  setSavedFilterAudience: (audience: SavedFilterAudience) => void;
  /**
   * How this person last set up a tracking movie — format, size, motion, labels, layers,
   * captions — so the next movie starts from it. Undefined until one has been set up.
   *
   * Stored as it was chosen and read back only through the movie settings' own repair, which
   * falls each unreadable or outdated field back to its default: a value saved by an older
   * version, or one a newer version no longer offers, never reaches the dialog as it is. Which
   * trips were in the movie is deliberately not kept — that is a choice about one movie, not a
   * preference, and a remembered list would be a record of which trips somebody looked at. Nor is
   * the title written for it (the dialog stores it empty): it names one cave and one trip, and
   * kept, it would caption and name the next movie, of any other.
   */
  movieSettings?: MovieSettings;
  setMovieSettings: (settings: MovieSettings | undefined) => void;
  /**
   * What the GIF movies made in this browser really came to, per pixel and frame, so the size the
   * movie dialog estimates for the next one follows this person's own caves instead of one built-in
   * guess. Undefined until a GIF has been made. Two numbers and nothing about any movie: not which
   * trips, which cave or when. Read back only through the estimate's own repair, which believes no
   * figure outside its bounds.
   */
  movieGifCalibration?: MovieGifCalibration;
  setMovieGifCalibration: (calibration: MovieGifCalibration | undefined) => void;
  /**
   * How large the names of a 3D model's stations are written, as a multiple of the viewer's own
   * size. One choice for every model this person opens: it is about their screen and their eyes,
   * not about any one survey. Undefined is the viewer's own size.
   */
  modelLabelScale?: number;
  setModelLabelScale: (scale: number | undefined) => void;
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
  /** The map workspace's docks as they were last left, on a desktop layout. */
  docks: DockPrefs;
  setDocks: (patch: Partial<DockPrefs>) => void;
  /** How positions are written wherever one is shown or copied. */
  coordinateFormat: CoordinateFormat;
  setCoordinateFormat: (format: CoordinateFormat) => void;
  /** Each format's own settings — places, precision, zone, code length — whether or not it is the one chosen. */
  coordinateSettings: FormatSettings;
  /** Replaces one format's settings (with a new object, so the change is seen and saved). */
  setFormatSettings: <K extends FormatId>(format: K, settings: FormatSettings[K]) => void;
  /** The formats a position shown in detail is also listed in, under "other formats", in this order. */
  coordinateFormatsListed: FormatId[];
  setCoordinateFormatsListed: (formats: FormatId[]) => void;
  /** Whether the map's overview inset is open. */
  overviewMap: boolean;
  setOverviewMap: (open: boolean) => void;
  /**
   * Which account the account preferences held here belong to — those `accountPrefDefaults`
   * lists. They are a cache of what that account has stored; undefined when no account has
  /** How a typed or pasted position is read where the text itself leaves it open. */
  coordinateReading: ReadingRules;
  setCoordinateReading: (patch: Partial<ReadingRules>) => void;
   * claimed them.
   */
  accountId?: string;
  /**
   * The account's preferences as this browser last read or wrote them — what "changed here" is
   * measured from. A save sends only what differs between this and the preferences held here, so
   * a value another browser or tab wrote since is never sent back over it; and a page reloaded
   * before a change went out still knows which preferences are its own to write. Kept with the
   * cache; undefined when nothing has been read for the account yet.
   */
  accountPrefsBaseline?: Record<string, unknown>;
  /**
   * The named layouts kept in this browser only, by id: those the account's stored preferences
   * could not hold when they were written, so the layouts list can say that a layout will not be
   * found in another browser, and why. A fact about this browser's copy, never sent; emptied with
   * the cache whenever another account claims it.
   */
  layoutsKeptHere: string[];
  setLayoutsKeptHere: (ids: string[]) => void;
  /**
   * Makes the account preferences this account's: unless they already were, they go back to their
   * defaults first, so a member signing in on this browser starts from nothing of whoever used it
   * before. Says which it was: already this account's, or reset.
   */
  adoptAccount: (accountId: string) => AccountClaim;
  /** Puts the account preferences back to their defaults and leaves them unclaimed — on sign-out. */
  releaseAccount: () => void;
  /**
   * Takes account preferences from the account — the values to hold, and the account's document
   * as now known, which becomes what later changes here are measured from.
   */
  applyAccountPrefs: (prefs: Partial<AccountPrefsState>, baseline: Record<string, unknown>) => void;
}

export type AccountClaim = 'same' | 'reset';

/**
 * How many saved filters one account may pin as quick chips. Few on purpose: a chip row is for the
 * filters used every day, and every pinned filter is one more name to read past. Twelve is what
 * the row still holds without taking the map over: a chip's name is cut to a fixed width, so the
 * row wraps to two or three lines on a desktop map and scrolls sideways in one line on a phone.
 */
export const MAX_PINNED_FILTERS = 12;

/** The part of the state that is the account's rather than the browser's. */
export interface AccountPrefsState {
  appearance: Appearance;
  panels: Partial<Record<PanelScope, PanelPrefs>>;
  layouts: PanelLayout[];
  selectors: Record<string, SelectorPrefs>;
  docks: DockPrefs;
  pinnedTypeIds: number[];
  pinnedFilterIds: string[];
  coordinateFormat: CoordinateFormat;
  coordinateSettings: FormatSettings;
  coordinateFormatsListed: FormatId[];
  overviewMap: boolean;
  karstLinkTreatment: ProtectedPositionTreatment | undefined;
  savedFilterAudience: SavedFilterAudience;
}

/**
  coordinateReading: ReadingRules;
 * THE list of account preferences, with what each one is when nobody has chosen anything.
 *
 * Everything that treats the account's preferences as a set — which keys are written to the
 * account and read back from it, what a sign-out or another member's sign-in puts back, what the
 * store starts with — takes its keys and defaults from here, so a preference added to the account
 * is added once. The empty objects and lists are fresh each call; the two default records are
 * shared constants, which nothing changes in place.
 */
export function accountPrefDefaults(): AccountPrefsState {
  return {
    appearance: DEFAULT_APPEARANCE,
    panels: {},
    layouts: [],
    selectors: {},
    docks: DEFAULT_DOCKS,
    pinnedTypeIds: [],
    pinnedFilterIds: [],
    coordinateFormat: DEFAULT_COORDINATE_FORMAT,
    coordinateSettings: defaultFormatSettings(),
    coordinateFormatsListed: [...FORMAT_IDS],
    overviewMap: false,
    karstLinkTreatment: undefined,
    savedFilterAudience: DEFAULT_SAVED_FILTER_AUDIENCE,
  };
}

    coordinateReading: DEFAULT_READING_RULES,
/**
 * Cross-session personal UI preferences, persisted per browser. Deliberately separate
 * from the session-scoped workspace store: saved views and the workspace bus must never
 * carry — or impose on someone opening a shared view — another user's personal toolbar
 * and chrome setup.
 */
export const useUiPrefsStore = create<UiPrefsState>()(
  persist(
    (set) => ({
      ...accountPrefDefaults(),
      togglePinnedType: (id) =>
        set((state) => ({
          pinnedTypeIds: state.pinnedTypeIds.includes(id)
            ? state.pinnedTypeIds.filter((x) => x !== id)
            : [...state.pinnedTypeIds, id],
        })),
      togglePinnedFilter: (id) =>
        set((state) => {
          const key = id.toLowerCase();
          if (state.pinnedFilterIds.includes(key)) {
            return { pinnedFilterIds: state.pinnedFilterIds.filter((x) => x !== key) };
          }
          return state.pinnedFilterIds.length >= MAX_PINNED_FILTERS
            ? {}
            : { pinnedFilterIds: [...state.pinnedFilterIds, key] };
        }),
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
      meshesInViewMinZoom: undefined,
      meshesInViewMaxCaves: undefined,
      meshesInViewMaxBytes: undefined,
      setMeshesInViewLimits: ({ minZoom, maxCaves, maxBytes }) =>
        set({
          meshesInViewMinZoom: minZoom,
          meshesInViewMaxCaves: maxCaves,
          meshesInViewMaxBytes: maxBytes,
        }),
      meshAskAboveBytes: undefined,
      setMeshAskAboveBytes: (bytes) => set({ meshAskAboveBytes: bytes }),
      setAppearance: (patch) => set((state) => ({ appearance: { ...state.appearance, ...patch } })),
      setKarstLinkTreatment: (treatment) => set({ karstLinkTreatment: treatment }),
      setSavedFilterAudience: (audience) => set({ savedFilterAudience: audience }),
      movieSettings: undefined,
      setMovieSettings: (settings) => set({ movieSettings: settings }),
      movieGifCalibration: undefined,
      setMovieGifCalibration: (calibration) => set({ movieGifCalibration: calibration }),
      modelLabelScale: undefined,
      setModelLabelScale: (scale) => set({ modelLabelScale: scale }),
      setSelectorPrefs: (key, patch) =>
        set((state) => ({
          selectors: { ...state.selectors, [key]: { ...state.selectors[key], ...patch } },
        })),
      setPanelPrefs: (scope, patch) =>
        set((state) => ({
          panels: { ...state.panels, [scope]: { ...state.panels[scope], ...patch } },
        })),
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
      setDocks: (patch) => set((state) => ({ docks: { ...state.docks, ...patch } })),
      setCoordinateFormat: (format) => set({ coordinateFormat: format }),
      setFormatSettings: (format, settings) =>
        set((state) => ({ coordinateSettings: { ...state.coordinateSettings, [format]: { ...settings } } })),
      setCoordinateFormatsListed: (formats) => set({ coordinateFormatsListed: [...formats] }),
      setOverviewMap: (open) => set({ overviewMap: open }),
      accountId: undefined,
      accountPrefsBaseline: undefined,
      layoutsKeptHere: [],
      setLayoutsKeptHere: (ids) => set({ layoutsKeptHere: ids }),
      adoptAccount: (accountId) => {
      setCoordinateReading: (patch) =>
        set((state) => ({ coordinateReading: { ...state.coordinateReading, ...patch } })),
        let claim: AccountClaim = 'same';
        set((state) => {
          if (state.accountId === accountId) {
            return {};
          }
          // Never handed on — not from another account, and not from nobody: a cache no account
          // claimed is the defaults a sign-out left, or this browser's arrangement from before
          // preferences belonged to an account, which on an expedition's shared laptop is anybody's.
          claim = 'reset';
          return { ...accountPrefDefaults(), accountId, accountPrefsBaseline: undefined, layoutsKeptHere: [] };
        });
        return claim;
      },
      releaseAccount: () =>
        set({ ...accountPrefDefaults(), accountId: undefined, accountPrefsBaseline: undefined, layoutsKeptHere: [] }),
      applyAccountPrefs: (prefs, baseline) => set({ ...prefs, accountPrefsBaseline: baseline }),
    }),
    {
      name: 'silexgis.uiPrefs',
      version: 8,
      // Without a migrate, raising the version makes zustand discard the whole stored blob —
      // wiping everyone's pinned types, landing page and centerline budgets to add one field.
      migrate: (persisted, from) => {
        const state = persisted as UiPrefsState;
        // The browser's own settings, kept across every version.
        const browser = {
          movieSettings: from < 6 ? undefined : state.movieSettings,
          meshesInViewMinZoom: from < 7 ? undefined : state.meshesInViewMinZoom,
          meshesInViewMaxCaves: from < 7 ? undefined : state.meshesInViewMaxCaves,
          meshesInViewMaxBytes: from < 7 ? undefined : state.meshesInViewMaxBytes,
        };
        // Every version before this one predates preferences belonging to an account: such a
        // cache says nothing about whose they are, so its account preferences are dropped rather
        // than handed to whoever signs in next, and the account's own come back at sign-in. (A
        // later version keeps them when migrating from this one or after it.)
        return {
          ...state,
          ...browser,
          ...accountPrefDefaults(),
          accountId: undefined,
          accountPrefsBaseline: undefined,
          layoutsKeptHere: [],
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
