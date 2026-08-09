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
    expect(stored.version).toBe(3);
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
