// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render as renderBare, screen } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it } from 'vitest';
import '../../i18n';
import { defaultFormatSettings, FORMAT_IDS } from '../../geo/formats/registry.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import OtherFormats from './OtherFormats.tsx';

// Both link to the converter, so they are rendered inside a router.
const render = (ui: ReactElement) => renderBare(ui, { wrapper: MemoryRouter });

// The summit of Moldoveanu, a public trig point, not a registry position.
const [lon, lat] = [24.73617, 45.59944];

afterEach(() => {
  cleanup();
  useUiPrefsStore.setState({
    coordinateFormat: 'dd',
    coordinateSettings: defaultFormatSettings(),
    coordinateFormatsListed: [...FORMAT_IDS],
  });
});

it('lists the position in every listed format but the one it is shown in, leaving out a grid with no reference here', () => {
  render(<OtherFormats lon={lon} lat={lat} />);
  expect(screen.queryByTestId('other-formats')).toBeNull();

  fireEvent.click(screen.getByRole('button', { name: 'Other formats' }));

  expect(screen.queryByTestId('other-format-dd')).toBeNull();
  expect(screen.getByTestId('other-format-dms').textContent).toBe('45°35′58.0″N 24°44′10.2″E');
  expect(screen.getByTestId('other-format-utm').textContent).toMatch(/^35T \d{6} E \d{7} N$/);
  expect(screen.getByTestId('other-format-stereo70')).toBeTruthy();
  expect(screen.getByTestId('other-format-pluscode')).toBeTruthy();
  // Switzerland's grids have no reference for Romania.
  expect(screen.queryByTestId('other-format-lv95')).toBeNull();
  expect(screen.queryByTestId('other-format-lv03')).toBeNull();
});

it('writes a protected position no finer than the grid in every format it lists', () => {
  render(<OtherFormats lon={lon} lat={lat} approximate />);
  fireEvent.click(screen.getByRole('button', { name: 'Other formats' }));

  expect(screen.getByTestId('other-format-dms').textContent).toBe('45°36′N 24°44′E');
  expect(screen.getByTestId('other-format-ddm').textContent).toBe('45°36′N 24°44′E');
  expect(screen.getByTestId('other-format-utm').textContent).toMatch(/^35T \d{3}000 E \d{4}000 N$/);
  expect(screen.getByTestId('other-format-stereo70').textContent).toMatch(/^E \d{3}000 N \d{3}000$/);
  // MGRS: the 10 km square — zone, band, square letters and one digit per axis.
  expect(screen.getByTestId('other-format-mgrs').textContent?.replace(/\s/g, '')).toMatch(/^35T[A-Z]{2}\d{2}$/);
  // Plus Codes: six characters and padding, a 0.05° cell.
  expect(screen.getByTestId('other-format-pluscode').textContent).toMatch(/^[23456789CFGHJMPQRVWX]{6}00\+$/);
});

it('follows the listed formats, and shows nothing when the only one listed is the one shown', () => {
  useUiPrefsStore.setState({ coordinateFormatsListed: ['mgrs'] });
  const { unmount } = render(<OtherFormats lon={lon} lat={lat} />);
  fireEvent.click(screen.getByRole('button', { name: 'Other formats' }));
  expect(screen.getAllByTestId(/^other-format-/)).toHaveLength(1);
  unmount();

  useUiPrefsStore.setState({ coordinateFormatsListed: ['dd'] });
  render(<OtherFormats lon={lon} lat={lat} />);
  expect(screen.queryByRole('button', { name: 'Other formats' })).toBeNull();
});
