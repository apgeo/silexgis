// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  DEFAULT_APPEARANCE,
  DEFAULT_DOCKS,
  MAX_PINNED_FILTERS,
  accountPrefDefaults,
  useUiPrefsStore,
} from './uiPrefsStore.ts';

afterEach(() => {
  useUiPrefsStore.setState({
    pinnedTypeIds: [],
    pinnedFilterIds: [],
    dialogPlacement: {},
    mapChromeHidden: false,
    landingPage: 'map',
    appearance: DEFAULT_APPEARANCE,
    karstLinkTreatment: undefined,
    savedFilterAudience: { visibility: 'private' },
    movieSettings: undefined,
    movieGifCalibration: undefined,
    centerlineDetailZoom: undefined,
    centerlineMaxPaths: undefined,
    meshesInViewMinZoom: undefined,
    meshesInViewMaxCaves: undefined,
    meshesInViewMaxBytes: undefined,
    panels: {},
    layouts: [],
    selectors: {},
    docks: DEFAULT_DOCKS,
    coordinateFormat: 'dd',
    overviewMap: false,
    accountId: undefined,
  });
  localStorage.removeItem('silexgis.uiPrefs');
});

describe('uiPrefsStore pinned saved filters', () => {
  const id = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`;

  it('pins in click order, unpins on a repeat toggle, and treats two spellings of an id as one', () => {
    const { togglePinnedFilter } = useUiPrefsStore.getState();
    togglePinnedFilter(id(2));
    togglePinnedFilter('AAAAAAAA-0000-0000-0000-000000000001');
    expect(useUiPrefsStore.getState().pinnedFilterIds).toEqual([id(2), 'aaaaaaaa-0000-0000-0000-000000000001']);
    togglePinnedFilter('aaaaaaaa-0000-0000-0000-000000000001');
    expect(useUiPrefsStore.getState().pinnedFilterIds).toEqual([id(2)]);
  });

  it('pins no more than the row holds, and still unpins when it is full', () => {
    const { togglePinnedFilter } = useUiPrefsStore.getState();
    for (let n = 1; n <= MAX_PINNED_FILTERS + 1; n += 1) {
      togglePinnedFilter(id(n));
    }
    expect(useUiPrefsStore.getState().pinnedFilterIds).toHaveLength(MAX_PINNED_FILTERS);
    expect(useUiPrefsStore.getState().pinnedFilterIds).not.toContain(id(MAX_PINNED_FILTERS + 1));
    togglePinnedFilter(id(1));
    expect(useUiPrefsStore.getState().pinnedFilterIds).toHaveLength(MAX_PINNED_FILTERS - 1);
  });

  it('belongs to the account: a sign-out puts the chips back to none', () => {
    useUiPrefsStore.getState().togglePinnedFilter(id(3));
    useUiPrefsStore.getState().releaseAccount();
    expect(useUiPrefsStore.getState().pinnedFilterIds).toEqual([]);
    expect(accountPrefDefaults().pinnedFilterIds).toEqual([]);
  });
});

describe('uiPrefsStore pinned types', () => {
  it('pins in click order and unpins on repeat toggle', () => {
    const { togglePinnedType } = useUiPrefsStore.getState();
    togglePinnedType(3);
    togglePinnedType(1);
    togglePinnedType(2);
    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([3, 1, 2]);

    togglePinnedType(1);
    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([3, 2]);
  });

  it('re-pinning moves a type to the end (re-pin to reorder)', () => {
    const { togglePinnedType } = useUiPrefsStore.getState();
    togglePinnedType(1);
    togglePinnedType(2);
    togglePinnedType(1); // unpin…
    togglePinnedType(1); // …and pin again
    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([2, 1]);
  });

  it('persists preferences to localStorage under the versioned key', () => {
    useUiPrefsStore.getState().togglePinnedType(7);
    useUiPrefsStore.getState().setMapChromeHidden(true);

    const raw = localStorage.getItem('silexgis.uiPrefs');
    expect(raw).not.toBeNull();
    const stored = JSON.parse(raw!) as { state: { pinnedTypeIds: number[]; mapChromeHidden: boolean }; version: number };
    expect(stored.version).toBe(8);
    expect(stored.state.pinnedTypeIds).toEqual([7]);
    expect(stored.state.mapChromeHidden).toBe(true);
  });
});

describe('uiPrefsStore landing page', () => {
  it('defaults to the map so the workspace stays the app default', () => {
    expect(useUiPrefsStore.getState().landingPage).toBe('map');
  });

  it('persists an opt-in to the dashboard', () => {
    useUiPrefsStore.getState().setLandingPage('dashboard');
    expect(useUiPrefsStore.getState().landingPage).toBe('dashboard');

    const stored = JSON.parse(localStorage.getItem('silexgis.uiPrefs')!) as {
      state: { landingPage: string };
    };
    expect(stored.state.landingPage).toBe('dashboard');
  });
});

describe('uiPrefsStore appearance', () => {
  it('starts on the system theme so nothing is imposed before the user chooses', () => {
    expect(useUiPrefsStore.getState().appearance).toEqual(DEFAULT_APPEARANCE);
  });

  it('patches one appearance field without disturbing the others', () => {
    useUiPrefsStore.getState().setAppearance({ theme: 'dark' });
    useUiPrefsStore.getState().setAppearance({ reduceMotion: true });

    expect(useUiPrefsStore.getState().appearance).toEqual({
      theme: 'dark',
      density: 'comfortable',
      reduceMotion: true,
    });
  });

  it('keeps the browser’s own settings when upgrading from an old version', () => {
    // The guard on the version bump: without a migration, zustand throws the whole stored blob
    // away, quietly wiping every user's landing page and centerline budgets. (Pinned types are the
    // account's now, and a cache from before that says nothing about whose they were.)
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({ version: 1, state: { pinnedTypeIds: [4, 9], landingPage: 'dashboard' } }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([]);
    expect(useUiPrefsStore.getState().landingPage).toBe('dashboard');
    expect(useUiPrefsStore.getState().appearance).toEqual(DEFAULT_APPEARANCE);
  });

  it('drops an appearance stored before preferences belonged to an account', () => {
    // The account holds the appearance and hands it back at sign-in; a browser's old copy could be
    // anybody's who used it.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 2,
        state: {
          pinnedTypeIds: [3],
          appearance: { theme: 'dark', density: 'compact', reduceMotion: true },
        },
      }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().appearance).toEqual(DEFAULT_APPEARANCE);
    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([]);
    expect(useUiPrefsStore.getState().panels).toEqual({});
    expect(useUiPrefsStore.getState().layouts).toEqual([]);
  });

  it('drops a panel arrangement and layouts stored before they belonged to an account', () => {
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 3,
        state: {
          pinnedTypeIds: [5],
          panels: { main: { width: 420, pinned: false } },
          layouts: [{ id: 'a', name: 'Survey', panels: {} }],
        },
      }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().panels).toEqual({});
    expect(useUiPrefsStore.getState().layouts).toEqual([]);
    expect(useUiPrefsStore.getState().selectors).toEqual({});
  });

  it('comes back undecided about protected positions from a version that had not asked', () => {
    // Somebody who has not decided must come back undecided rather than with a default treatment
    // nobody chose; the selector memory, the account's now, is dropped with the rest of it.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 4,
        state: {
          pinnedTypeIds: [6],
          selectors: { caves: { width: 300 } },
        },
      }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([]);
    expect(useUiPrefsStore.getState().selectors).toEqual({});
    expect(useUiPrefsStore.getState().karstLinkTreatment).toBeUndefined();
  });

  it('drops a remembered export answer from before it belonged to an account, and has no movie settings', () => {
    // The answer about what to disclose is the account's now: whoever gave it in this browser, the
    // next person is asked again. And no movie settings rather than somebody else's idea of them —
    // the dialog starts from its own private defaults when nothing is stored.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 5,
        state: { pinnedTypeIds: [2], karstLinkTreatment: 'omit' },
      }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([]);
    expect(useUiPrefsStore.getState().karstLinkTreatment).toBeUndefined();
    expect(useUiPrefsStore.getState().movieSettings).toBeUndefined();
  });

  it('keeps what the GIFs made here came to, and has nothing to say before one was made', () => {
    // The figure is an optional field of the current shape: a blob written before it existed is
    // read as it is, with nothing learnt yet, and one written since comes back whole.
    localStorage.setItem('silexgis.uiPrefs', JSON.stringify({ version: 7, state: { pinnedTypeIds: [4] } }));
    useUiPrefsStore.persist.rehydrate();
    expect(useUiPrefsStore.getState().movieGifCalibration).toBeUndefined();

    useUiPrefsStore.getState().setMovieGifCalibration({ turning: 0.03 });
    const stored = JSON.parse(localStorage.getItem('silexgis.uiPrefs')!) as {
      version: number;
      state: { movieGifCalibration?: unknown };
    };
    expect(stored.version).toBe(8);
    expect(stored.state.movieGifCalibration).toEqual({ turning: 0.03 });

    useUiPrefsStore.setState({ movieGifCalibration: undefined });
    localStorage.setItem('silexgis.uiPrefs', JSON.stringify(stored));
    useUiPrefsStore.persist.rehydrate();
    expect(useUiPrefsStore.getState().movieGifCalibration).toEqual({ turning: 0.03 });
  });

  it('keeps the centerline budgets when the limits for the walls in view arrive beside them', () => {
    // One version on again, and the nearest neighbour of the new fields: a budget somebody tuned
    // for the flat map must survive, and the walls must come back following the installation
    // rather than holding a number nobody typed.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 6,
        state: {
          pinnedTypeIds: [8],
          centerlineDetailZoom: 16,
          centerlineMaxPaths: 40000,
          // A stray value under the new name in an older blob is not a choice anybody made.
          meshesInViewMaxCaves: 500,
        },
      }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().centerlineDetailZoom).toBe(16);
    expect(useUiPrefsStore.getState().centerlineMaxPaths).toBe(40000);
    expect(useUiPrefsStore.getState().meshesInViewMinZoom).toBeUndefined();
    expect(useUiPrefsStore.getState().meshesInViewMaxCaves).toBeUndefined();
    expect(useUiPrefsStore.getState().meshesInViewMaxBytes).toBeUndefined();
  });
});

describe('uiPrefsStore limits for the walls of the caves in view', () => {
  it('follows the installation until somebody sets a number of their own', () => {
    const state = useUiPrefsStore.getState();

    expect(state.meshesInViewMinZoom).toBeUndefined();
    expect(state.meshesInViewMaxCaves).toBeUndefined();
    expect(state.meshesInViewMaxBytes).toBeUndefined();
  });

  it('stores the three together, and clears whichever is left out', () => {
    const { setMeshesInViewLimits } = useUiPrefsStore.getState();
    setMeshesInViewLimits({ minZoom: 12, maxCaves: 30, maxBytes: 256 * 1024 * 1024 });

    expect(useUiPrefsStore.getState()).toMatchObject({
      meshesInViewMinZoom: 12,
      meshesInViewMaxCaves: 30,
      meshesInViewMaxBytes: 256 * 1024 * 1024,
    });

    // One setter for all three, as the centerline limits have: a value not named is a value
    // handed back to the installation, which is what "use the defaults" has to be able to say.
    setMeshesInViewLimits({ maxCaves: 30 });

    expect(useUiPrefsStore.getState().meshesInViewMinZoom).toBeUndefined();
    expect(useUiPrefsStore.getState().meshesInViewMaxCaves).toBe(30);
    expect(useUiPrefsStore.getState().meshesInViewMaxBytes).toBeUndefined();
  });

  it('persists them in this browser, and keeps them across a reload at the current version', () => {
    useUiPrefsStore.getState().setMeshesInViewLimits({ minZoom: 13, maxCaves: 24, maxBytes: 128_000_000 });

    const stored = JSON.parse(localStorage.getItem('silexgis.uiPrefs')!) as {
      state: Record<string, unknown>;
      version: number;
    };
    expect(stored.state).toMatchObject({
      meshesInViewMinZoom: 13,
      meshesInViewMaxCaves: 24,
      meshesInViewMaxBytes: 128_000_000,
    });

    // What a reload does: the store starts empty and reads the blob back.
    useUiPrefsStore.setState({
      meshesInViewMinZoom: undefined,
      meshesInViewMaxCaves: undefined,
      meshesInViewMaxBytes: undefined,
    });
    localStorage.setItem('silexgis.uiPrefs', JSON.stringify(stored));
    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState()).toMatchObject({
      meshesInViewMinZoom: 13,
      meshesInViewMaxCaves: 24,
      meshesInViewMaxBytes: 128_000_000,
    });
  });
});

describe('uiPrefsStore panel arrangements', () => {
  // The store is a module singleton, so each case starts from a known arrangement rather than
  // from whatever the previous one left — otherwise a layout captures another test's panel.
  beforeEach(() => useUiPrefsStore.setState({ panels: {}, layouts: [] }));

  it('keeps every panel apart, so a pop-out is not rearranged by the main window', () => {
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 30, pinned: false });
    useUiPrefsStore.getState().setPanelPrefs('popout', { width: 100 });

    expect(useUiPrefsStore.getState().panels.main).toMatchObject({ width: 30, pinned: false });
    expect(useUiPrefsStore.getState().panels.popout).toEqual({ width: 100 });
  });

  it('saves an arrangement under a name and replaces it when the name is reused', () => {
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 30 });
    useUiPrefsStore.getState().saveLayout('Surveying', false);
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 55 });
    useUiPrefsStore.getState().saveLayout('Surveying', true);

    const layouts = useUiPrefsStore.getState().layouts.filter((l) => l.name === 'Surveying');
    expect(layouts).toHaveLength(1);
    expect(layouts[0].panels.main).toMatchObject({ width: 55 });
  });

  it('restores an arrangement wholesale rather than merging it over the current one', () => {
    // Merging would leave a panel the layout says nothing about at whatever the last one set,
    // which is the state loading a layout is meant to end.
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 30 });
    useUiPrefsStore.getState().saveLayout('Narrow', false);
    useUiPrefsStore.getState().setPanelPrefs('popout', { width: 90 });

    const id = useUiPrefsStore.getState().layouts.find((l) => l.name === 'Narrow')!.id;
    useUiPrefsStore.getState().applyLayout(id);

    expect(useUiPrefsStore.getState().panels.main).toMatchObject({ width: 30 });
    expect(useUiPrefsStore.getState().panels.popout).toBeUndefined();
  });
});

describe('uiPrefsStore dialog placement', () => {
  it('defaults to no stored placement and records per-dialog choices independently', () => {
    expect(useUiPrefsStore.getState().dialogPlacement).toEqual({});

    useUiPrefsStore.getState().setDialogPlacement('cave-add', 'drawer');
    useUiPrefsStore.getState().setDialogPlacement('feature-edit', 'modal');

    expect(useUiPrefsStore.getState().dialogPlacement).toEqual({
      'cave-add': 'drawer',
      'feature-edit': 'modal',
    });
  });
});

describe('uiPrefsStore account preferences', () => {
  it('keeps a member’s arrangement from the next member who signs in on the same browser', () => {
    const store = useUiPrefsStore.getState();
    store.adoptAccount('account-a');
    store.setCoordinateFormat('utm');
    store.togglePinnedType(4);
    store.setDocks({ leftHidden: true });
    store.setPanelPrefs('main', { width: 30 });

    expect(useUiPrefsStore.getState().adoptAccount('account-b')).toBe('reset');

    const after = useUiPrefsStore.getState();
    expect(after.accountId).toBe('account-b');
    expect(after.coordinateFormat).toBe('dd');
    expect(after.pinnedTypeIds).toEqual([]);
    expect(after.docks).toEqual(DEFAULT_DOCKS);
    expect(after.panels).toEqual({});
  });

  it('keeps them across a sign-in of the same account, and hands an unclaimed cache to nobody', () => {
    const store = useUiPrefsStore.getState();
    store.setCoordinateFormat('dms');

    // Whoever set this, no account claimed it: the first account to sign in starts from defaults.
    expect(store.adoptAccount('account-a')).toBe('reset');
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('dd');
    useUiPrefsStore.getState().setCoordinateFormat('utm');
    expect(useUiPrefsStore.getState().adoptAccount('account-a')).toBe('same');
    expect(useUiPrefsStore.getState().coordinateFormat).toBe('utm');
  });

  it('asks the next member signing in again what to do with protected positions', () => {
    const store = useUiPrefsStore.getState();
    store.adoptAccount('account-a');
    store.setKarstLinkTreatment('grid_position');

    useUiPrefsStore.getState().adoptAccount('account-b');

    expect(useUiPrefsStore.getState().karstLinkTreatment).toBeUndefined();
  });

  it('keeps each account its own default audience for saved filters, private for the next member', () => {
    useUiPrefsStore.getState().adoptAccount('account-a');
    useUiPrefsStore.getState().setSavedFilterAudience({ visibility: 'cavingGroup', cavingGroupId: 'club-a' });

    // A member signing in after them on the same browser must not start out sharing with their club.
    useUiPrefsStore.getState().adoptAccount('account-b');
    expect(useUiPrefsStore.getState().savedFilterAudience).toEqual({ visibility: 'private' });

    useUiPrefsStore.getState().releaseAccount();
    expect(useUiPrefsStore.getState().savedFilterAudience).toEqual({ visibility: 'private' });
  });

  it('puts back every account preference the one list names, and starts the store from it', () => {
    const fresh = accountPrefDefaults();
    const store = useUiPrefsStore.getState();
    store.adoptAccount('account-a');
    store.setCoordinateFormat('utm');
    store.togglePinnedType(4);
    store.setKarstLinkTreatment('omit');

    useUiPrefsStore.getState().releaseAccount();

    const after = useUiPrefsStore.getState();
    for (const key of Object.keys(fresh) as (keyof typeof fresh)[]) {
      expect(after[key], key).toEqual(fresh[key]);
    }
  });

  it('gives them up on sign-out, and leaves the browser’s own settings alone', () => {
    const store = useUiPrefsStore.getState();
    store.adoptAccount('account-a');
    store.setCoordinateFormat('utm');
    store.setCenterlineLimits({ detailZoom: 14 });

    useUiPrefsStore.getState().releaseAccount();

    const after = useUiPrefsStore.getState();
    expect(after.accountId).toBeUndefined();
    expect(after.coordinateFormat).toBe('dd');
    expect(after.centerlineDetailZoom).toBe(14);
  });

  it('does not hand a cache from before preferences belonged to an account to the first one to sign in', () => {
    // A shared expedition laptop: whoever arranged this, the next member to sign in is not them.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 7,
        state: { pinnedTypeIds: [2], panels: { main: { width: 25 } }, landingPage: 'dashboard' },
      }),
    );

    useUiPrefsStore.persist.rehydrate();
    useUiPrefsStore.getState().adoptAccount('account-a');

    const state = useUiPrefsStore.getState();
    expect(state.pinnedTypeIds).toEqual([]);
    expect(state.panels).toEqual({});
    expect(state.docks).toEqual(DEFAULT_DOCKS);
    expect(state.coordinateFormat).toBe('dd');
    expect(state.accountId).toBe('account-a');
    // The browser's own settings are the browser's, and stay.
    expect(state.landingPage).toBe('dashboard');
  });
});
