// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';

/**
 * The account's survey-upload choices: which results are always produced, public links, and who
 * a link opens for by default.
 *
 * The outputs offered come from what the installation says it produces, and what is saved is the
 * whole set, outputs in the catalogue's own order.
 */

const saveMutate = vi.fn();
const CATALOGUE = ['lox', '3d', 'plan-pdf', 'extended-pdf', 'kml', 'shp'];
let preferences: { alwaysOutputs: string[]; publicLinks: boolean; defaultLinkAudience: 'public' | 'organization' };
let loading: boolean;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSurveyUploadCapabilities: () => ({
      data: { outputs: { catalogue: CATALOGUE, defaults: ['lox', '3d'] } },
      isPending: false,
      isError: false,
      refetch: vi.fn(),
    }),
    useSurveyUploadPreferences: () => ({
      data: loading ? undefined : preferences,
      isPending: loading,
      isError: false,
      refetch: vi.fn(),
    }),
    useSaveSurveyUploadPreferences: () => ({ mutateAsync: saveMutate, isPending: false }),
  };
});

const { default: SurveyUploadSettingsPage } = await import('./SurveyUploadSettingsPage.tsx');

function show() {
  return render(
    <App>
      <SurveyUploadSettingsPage />
    </App>,
  );
}

beforeEach(() => {
  saveMutate.mockReset();
  preferences = { alwaysOutputs: ['kml'], publicLinks: true, defaultLinkAudience: 'public' };
  loading = false;
});

afterEach(cleanup);

describe('the survey-upload settings page', () => {
  it('offers exactly the outputs the installation produces, ticking the saved ones', () => {
    show();

    expect(screen.getAllByRole('checkbox').filter((box) => box.getAttribute('type') === 'checkbox')).toHaveLength(
      CATALOGUE.length,
    );
    expect(screen.getByRole('checkbox', { name: 'Google Earth (KML)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Plan map (PDF)' })).not.toBeChecked();
    expect(screen.getByRole('switch', { name: 'Public links for my uploads' })).toBeChecked();
  });

  it('keeps Save off until something changed, then saves the whole set in catalogue order', async () => {
    saveMutate.mockResolvedValue({});

    show();
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    // Ticked in the opposite order to the catalogue's.
    fireEvent.click(screen.getByRole('checkbox', { name: 'Plan map (PDF)' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Therion 3D model (.lox)' }));
    fireEvent.click(screen.getByRole('switch', { name: 'Public links for my uploads' }));
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() =>
      expect(saveMutate).toHaveBeenCalledWith({
        alwaysOutputs: ['lox', 'plan-pdf', 'kml'],
        publicLinks: false,
        defaultLinkAudience: 'public',
      }),
    );
  });

  it('drops a saved output the installation no longer produces instead of sending it back', async () => {
    preferences = { alwaysOutputs: ['kml', 'gone'], publicLinks: true, defaultLinkAudience: 'organization' };
    saveMutate.mockResolvedValue({});

    show();
    fireEvent.click(screen.getByRole('checkbox', { name: 'GIS shapefile (SHP)' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(saveMutate).toHaveBeenCalledWith({
        alwaysOutputs: ['kml', 'shp'],
        publicLinks: true,
        defaultLinkAudience: 'organization',
      }),
    );
  });

  it('shows the saved default audience and saves a changed one with the rest', async () => {
    saveMutate.mockResolvedValue({});

    show();
    expect(screen.getByRole('radio', { name: 'Anyone with the link' })).toBeChecked();
    fireEvent.click(screen.getByRole('radio', { name: 'Signed-in accounts only' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(saveMutate).toHaveBeenCalledWith({
        alwaysOutputs: ['kml'],
        publicLinks: true,
        defaultLinkAudience: 'organization',
      }),
    );
  });

  it('names the defaults the installation produces when nothing is asked for', () => {
    show();
    expect(screen.getByText(/the installation produces: Therion 3D model \(\.lox\), Survex 3D model \(\.3d\)/)).toBeTruthy();
  });
});

describe('the output names', () => {
  it('are given in both locales for every output the server lists', () => {
    // The server's catalogue; a name added there without wording here would reach the page as a raw id.
    const all = [
      'lox', '3d', 'plan-pdf', 'extended-pdf', 'plan-svg', 'extended-svg', 'kml', 'dxf', 'sql', 'csv',
      'survey-list-html', 'continuation-list-html', 'shp',
    ];
    const enNames: Record<string, string> = en.surveyUploads.outputs;
    const roNames: Record<string, string> = ro.surveyUploads.outputs;
    expect(all.filter((name) => !enNames[name])).toEqual([]);
    expect(all.filter((name) => !roNames[name])).toEqual([]);
    expect(Object.keys(enNames).sort()).toEqual([...all].sort());
  });
});
