// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import { defaultFormatSettings, FORMAT_IDS } from '../../geo/formats/registry.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import CoordinateConverterPage from './CoordinateConverterPage.tsx';

const saved = vi.hoisted(() => ({ blobs: [] as { blob: Blob; name: string }[] }));
vi.mock('../../api/download.ts', () => ({
  saveBlob: (blob: Blob, name: string) => saved.blobs.push({ blob, name }),
}));

afterEach(() => {
  cleanup();
  saved.blobs.length = 0;
  useUiPrefsStore.setState({ coordinateFormat: 'dd', coordinateSettings: defaultFormatSettings() });
});

function open(path = '/tools/coordinates') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <CoordinateConverterPage />
    </MemoryRouter>,
  );
}

it('writes a pasted DMS position in every format, a grid with no reference here as not available', () => {
  open();
  fireEvent.change(screen.getByTestId('converter-input'), { target: { value: '45°35′58″N 24°44′10″E' } });

  expect(screen.getByTestId('converter-feedback').textContent).toContain('Degrees, minutes, seconds');
  for (const id of FORMAT_IDS) {
    expect(screen.getByTestId(`converter-result-${id}`)).toBeTruthy();
  }
  expect(screen.getByTestId('converter-result-dd').textContent).toBe('45.59944°N 24.73611°E');
  expect(screen.getByTestId('converter-result-utm').textContent).toMatch(/^35T \d{6} E \d{7} N$/);
  expect(screen.getByTestId('converter-result-stereo70').textContent).toMatch(/^E \d{6} N \d{6}$/);
  expect(screen.getByTestId('converter-result-lv95').textContent).toBe('Not available here');
  // The main map is offered at the place, and nothing is saved by looking.
  expect(screen.getByRole('link', { name: 'Open this place on the main map' }).getAttribute('href')).toMatch(
    /^\/map#15\.00\/45\.5994\d\/24\.7361\d$/,
  );
});

it('opens on a position handed over in the address, and says why a text cannot be read', () => {
  open(`/tools/coordinates?q=${encodeURIComponent('45.5, 25.5')}`);
  expect(screen.getByTestId('converter-result-dd').textContent).toBe('45.50000°N 25.50000°E');

  fireEvent.change(screen.getByTestId('converter-input'), { target: { value: '95.5 125.5' } });
  expect(screen.getByTestId('converter-feedback').textContent).toBe('The latitude is outside −90…90.');
  expect(screen.queryByTestId('converter-results')).toBeNull();
});

it('offers each reading of an ambiguous text, and writes the one chosen', () => {
  open();
  fireEvent.change(screen.getByTestId('converter-input'), { target: { value: '35S 500000 3800000' } });
  const readings = within(screen.getByTestId('converter-feedback')).getAllByRole('radio');
  expect(readings).toHaveLength(2);
  const first = screen.getByTestId('converter-result-dd').textContent;
  fireEvent.click(readings[1]);
  expect(screen.getByTestId('converter-result-dd').textContent).not.toBe(first);
});

it('converts many lines, says which failed, and downloads every format as CSV', async () => {
  open();
  fireEvent.change(screen.getByTestId('converter-batch-input'), {
    target: { value: '45°35′58″N 24°44′10″E\n35T 401234 5049876\nnot a position' },
  });
  fireEvent.click(screen.getByTestId('converter-batch-convert'));

  expect(screen.getByTestId('converter-batch-summary').textContent).toBe('Lines read: 3; not converted: 1.');
  expect(screen.getByText('Not a position in any format this application reads.')).toBeTruthy();

  fireEvent.click(screen.getByTestId('converter-batch-download'));
  expect(saved.blobs).toHaveLength(1);
  expect(saved.blobs[0].name).toBe('coordinates.csv');
  const lines = (await saved.blobs[0].blob.text()).replace(/^﻿/, '').trimEnd().split('\r\n');
  expect(lines).toHaveLength(4);
  expect(lines[0]).toContain('Stereo 70 (Romania)');
  expect(lines[1]).toMatch(/^1,45°35′58″N 24°44′10″E,45\.59944\d+,24\.7361\d+,/);
  expect(lines[3]).toMatch(/Not a position in any format this application reads\.$/);
});
