// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import '../../i18n';
import { defaultFormatSettings, FORMAT_IDS } from '../../geo/formats/registry.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const savePreferences = vi.fn();

vi.mock('../../api/hooks/accountPrefs.ts', () => ({
  useSaveAccountPreferences: () => ({ mutate: savePreferences }),
}));

const { default: CoordinateSettingsPage } = await import('./CoordinateSettingsPage.tsx');

function show() {
  return render(
    <App>
      <CoordinateSettingsPage />
    </App>,
  );
}

beforeEach(() => savePreferences.mockReset());

afterEach(() => {
  cleanup();
  useUiPrefsStore.setState({
    coordinateFormat: 'dd',
    coordinateSettings: defaultFormatSettings(),
    coordinateFormatsListed: [...FORMAT_IDS],
  });
});

it('offers every format by what a position looks like in it, and takes and saves the one picked', async () => {
  show();

  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Coordinates shown as' }));
  fireEvent.click(await screen.findByText(/^UTM, with zone — 35T /));

  expect(useUiPrefsStore.getState().coordinateFormat).toBe('utm');
  expect(savePreferences).toHaveBeenCalledTimes(1);
});

it('previews each format on the sample point, and a setting changes its preview at once', async () => {
  show();
  expect(screen.getByTestId('coordinate-preview-dd').textContent).toBe('45.59944°N 24.73617°E');
  // The Swiss grids have no reference for Romania, so they are previewed on their own origin.
  expect(screen.getByTestId('coordinate-preview-lv95').textContent).toMatch(/^E 2(599|600)\d{3} N 1(199|200)\d{3}$/);
  expect(screen.getByTestId('coordinate-preview-stereo70').textContent).toMatch(/^E \d{6} N \d{6}$/);

  const row = screen.getByTestId('coordinate-settings-dd');
  fireEvent.mouseDown(within(row).getByRole('combobox', { name: 'Decimal degrees — Decimal places' }));
  fireEvent.click(await screen.findByTitle('2'));

  expect(useUiPrefsStore.getState().coordinateSettings.dd.decimals).toBe(2);
  expect(screen.getByTestId('coordinate-preview-dd').textContent).toBe('45.60°N 24.74°E');
  expect(savePreferences).toHaveBeenCalled();
});

it('keeps the listed formats in their own order whatever order they are ticked in', () => {
  useUiPrefsStore.setState({ coordinateFormatsListed: [] });
  show();

  fireEvent.click(screen.getByRole('checkbox', { name: 'Plus Code' }));
  fireEvent.click(screen.getByRole('checkbox', { name: 'Degrees, decimal minutes' }));

  expect(useUiPrefsStore.getState().coordinateFormatsListed).toEqual(['ddm', 'pluscode']);
});
