// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
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
    expect(stored.version).toBe(2);
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
