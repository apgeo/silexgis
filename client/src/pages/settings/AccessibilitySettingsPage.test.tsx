// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { DEFAULT_APPEARANCE, useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const savePreferences = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useUiPreferences: () => ({ data: undefined }),
    useUpdateUiPreferences: () => ({ mutate: savePreferences }),
  };
});

// The language switch saves to the account, which is not this page's to prove.
vi.mock('../../i18n/languageChoice.ts', () => ({
  useLanguageChoice: () => ({ language: 'en', choose: vi.fn() }),
}));

const { default: AccessibilitySettingsPage } = await import('./AccessibilitySettingsPage.tsx');

function show() {
  return render(
    <App>
      <AccessibilitySettingsPage />
    </App>,
  );
}

/** Everything this browser keeps that the tests below set, back as a fresh browser has it. */
function forget() {
  const prefs = useUiPrefsStore.getState();
  prefs.setLandingPage('map');
  prefs.setMapChromeHidden(false);
  prefs.setAppearance(DEFAULT_APPEARANCE);
  prefs.setCenterlineLimits({});
  prefs.setMeshesInViewLimits({});
}

beforeEach(() => {
  savePreferences.mockReset();
  forget();
});

afterEach(() => {
  cleanup();
  forget();
  localStorage.removeItem('silexgis.uiPrefs');
});

describe('what this browser alone keeps, on the accessibility page', () => {
  it('is the page the application opens on and the map’s controls, and no rendering budget', () => {
    show();

    expect(screen.getByRole('combobox', { name: 'Open the app on' })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Hide the on-map controls' })).toBeInTheDocument();
    // The budgets for what the map and the 3D view draw are numbers, and all of them are on the
    // advanced page: none is left here to be set in two places.
    expect(screen.queryAllByRole('spinbutton')).toHaveLength(0);
  });

  it('puts back what the page shows, and leaves the budgets kept on the advanced page alone', () => {
    const prefs = useUiPrefsStore.getState();
    prefs.setLandingPage('dashboard');
    prefs.setMapChromeHidden(true);
    prefs.setAppearance({ theme: 'dark', density: 'compact', reduceMotion: true });
    prefs.setCenterlineLimits({ detailZoom: 16, maxPaths: 40000 });
    prefs.setMeshesInViewLimits({ maxCaves: 30 });
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Reset these' }));

    const after = useUiPrefsStore.getState();
    expect(after.landingPage).toBe('map');
    expect(after.mapChromeHidden).toBe(false);
    expect(after.appearance).toEqual(DEFAULT_APPEARANCE);
    expect(savePreferences).toHaveBeenCalledWith({ appearance: DEFAULT_APPEARANCE }, expect.anything());
    // A button that emptied numbers this page does not show would do it where nobody is looking.
    expect(after.centerlineDetailZoom).toBe(16);
    expect(after.centerlineMaxPaths).toBe(40000);
    expect(after.meshesInViewMaxCaves).toBe(30);
  });
});
