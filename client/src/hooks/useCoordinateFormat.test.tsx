// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import PointField from '../components/settings/PointField.tsx';
import '../i18n';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useCoordinateCopyFormatter } from './useCoordinateFormat.ts';

afterEach(() => {
  cleanup();
  useUiPrefsStore.getState().setCoordinateFormat('dd');
});

// A public trig point, not a registry position.
const point: [number, number] = [24.73617, 45.59944];

it('writes a shown position in the format chosen, and follows a change of it', () => {
  render(<PointField value={point} />);
  expect(screen.getByText('45.59944°N 24.73617°E')).toBeTruthy();

  act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
  expect(screen.getByText(/^35T \d{6} E \d{7} N$/)).toBeTruthy();

  act(() => useUiPrefsStore.getState().setCoordinateFormat('dms'));
  expect(screen.getByText('45°35′58.0″N 24°44′10.2″E')).toBeTruthy();
});

function CopyProbe() {
  const copyText = useCoordinateCopyFormatter();
  return <span data-testid="copy">{copyText(point[0], point[1])}</span>;
}

it('copies decimal degrees as lat, lon, and the other formats as they are shown', () => {
  render(<CopyProbe />);
  expect(screen.getByTestId('copy').textContent).toBe('45.599440, 24.736170');

  act(() => useUiPrefsStore.getState().setCoordinateFormat('utm'));
  expect(screen.getByTestId('copy').textContent).toMatch(/^35T \d{6}\.\d E \d{7}\.\d N$/);
});
