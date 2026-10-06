// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_APPEARANCE, useUiPrefsStore } from './uiPrefsStore.ts';

afterEach(() => {
  useUiPrefsStore.setState({
    pinnedTypeIds: [],
    dialogPlacement: {},
    mapChromeHidden: false,
    landingPage: 'map',
    appearance: DEFAULT_APPEARANCE,
    karstLinkTreatment: undefined,
    movieSettings: undefined,
    centerlineDetailZoom: undefined,
    centerlineMaxPaths: undefined,
    meshesInViewMinZoom: undefined,
    meshesInViewMaxCaves: undefined,
    meshesInViewMaxBytes: undefined,
  });
  localStorage.removeItem('silexgis.uiPrefs');
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
    expect(stored.version).toBe(7);
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

  it('keeps everything already stored when upgrading from the previous version', () => {
    // The guard on the version bump: without a migration, zustand throws the whole stored blob
    // away, quietly wiping every user's pinned types, landing page and centerline budgets.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({ version: 1, state: { pinnedTypeIds: [4, 9], landingPage: 'dashboard' } }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([4, 9]);
    expect(useUiPrefsStore.getState().landingPage).toBe('dashboard');
    expect(useUiPrefsStore.getState().appearance).toEqual(DEFAULT_APPEARANCE);
  });

  it('keeps an appearance chosen under the previous version when the panel keys arrive', () => {
    // The same guard one version on. Somebody who picked a dark theme must not be handed the
    // default one back because a panel arrangement was added beside it.
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

    expect(useUiPrefsStore.getState().appearance.theme).toBe('dark');
    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([3]);
    expect(useUiPrefsStore.getState().panels).toEqual({});
    expect(useUiPrefsStore.getState().layouts).toEqual([]);
  });

  it('keeps a panel arrangement when the selector memory arrives beside it', () => {
    // The same guard again. Adding somewhere for the object selectors to remember themselves must
    // not cost anybody the panel they had arranged.
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

    expect(useUiPrefsStore.getState().panels.main?.width).toBe(420);
    expect(useUiPrefsStore.getState().layouts).toHaveLength(1);
    expect(useUiPrefsStore.getState().selectors).toEqual({});
  });

  it('keeps a selector memory when the remembered export answer arrives beside it', () => {
    // The same guard again, for the field that remembers what somebody decided about protected
    // positions in an interchange export. Adding it must not cost anybody what they already had,
    // and somebody who has not decided must come back undecided rather than with a default
    // treatment nobody chose.
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

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([6]);
    expect(useUiPrefsStore.getState().selectors).toEqual({ caves: { width: 300 } });
    expect(useUiPrefsStore.getState().karstLinkTreatment).toBeUndefined();
  });

  it('keeps a remembered export answer when the movie settings arrive beside it', () => {
    // One version on again. Somebody who settled the interchange question must keep that answer,
    // and must come back with no movie settings rather than with somebody else's idea of them —
    // the dialog starts from its own private defaults when nothing is stored.
    localStorage.setItem(
      'silexgis.uiPrefs',
      JSON.stringify({
        version: 5,
        state: { pinnedTypeIds: [2], karstLinkTreatment: 'omit' },
      }),
    );

    useUiPrefsStore.persist.rehydrate();

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([2]);
    expect(useUiPrefsStore.getState().karstLinkTreatment).toBe('omit');
    expect(useUiPrefsStore.getState().movieSettings).toBeUndefined();
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

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([8]);
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
