// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { GeofileInfo } from '../../api/hooks.ts';
import GeofileEncodingDialog from './GeofileEncodingDialog.tsx';

const { reimport } = vi.hoisted(() => ({ reimport: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useReimportGeofile: () => ({ mutateAsync: reimport, isPending: false }),
}));

const table = (delimited: NonNullable<GeofileInfo['sourceOptions']>['delimited']): GeofileInfo => ({
  id: 'geo-1',
  name: 'puncte.csv',
  description: null,
  fileId: 'file-1',
  format: 'csv',
  srid: 4326,
  importStatus: 'imported',
  importError: null,
  featureCount: 3,
  bbox: null,
  style: null,
  sourceOptions: { delimited },
  ownerUserId: 'user-1',
  cavingGroupId: null,
  visibility: 'private',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
});

const corrected = { delimiter: ';', latitudeColumn: 'Y', longitudeColumn: 'X', elevationColumn: 'Z', wktColumn: null };

const show = (geofile: GeofileInfo, onClose = vi.fn()) =>
  render(
    <App>
      <GeofileEncodingDialog geofile={geofile} onClose={onClose} />
    </App>,
  );

beforeEach(async () => {
  reimport.mockReset();
  await i18n.changeLanguage('en');
});
afterEach(cleanup);

describe('GeofileEncodingDialog', () => {
  it('reads the table again as Windows-1252 and keeps the separator and corrected columns', async () => {
    reimport.mockResolvedValue({ id: 'geo-1', importStatus: 'uploaded', importError: null, featureCount: 0 });
    const onClose = vi.fn();
    show(table(corrected), onClose);

    expect(screen.getByRole('radio', { name: 'Detect it from the file' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'UTF-8' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Windows-1250, Central European' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('radio', { name: 'Windows-1252, Western European' }));
    fireEvent.click(screen.getByRole('button', { name: 'Read it again' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(reimport).toHaveBeenCalledWith({
      id: 'geo-1',
      sourceOptions: { delimited: { ...corrected, encoding: 'windows1252' } },
    });
  });

  it('goes back to detection by sending no encoding, and offers nothing to send until the choice changes', async () => {
    reimport.mockResolvedValue({ id: 'geo-1', importStatus: 'uploaded', importError: null, featureCount: 0 });
    show(table({ ...corrected, encoding: 'windows1250' }));

    const confirm = screen.getByRole('button', { name: 'Read it again' });
    expect(screen.getByRole('radio', { name: 'Windows-1250, Central European' })).toBeChecked();
    expect(confirm).toBeDisabled();

    fireEvent.click(screen.getByRole('radio', { name: 'Detect it from the file' }));
    fireEvent.click(confirm);

    await waitFor(() => expect(reimport).toHaveBeenCalled());
    expect(reimport.mock.calls[0][0].sourceOptions.delimited.encoding).toBeNull();
    expect(reimport.mock.calls[0][0].sourceOptions.delimited.latitudeColumn).toBe('Y');
  });

  it('still shows an encoding stored on the file that is not among the three offered', () => {
    show(table({ encoding: 'iso88592' }));

    expect(screen.getByRole('radio', { name: 'ISO-8859-2, Latin-2' })).toBeChecked();
  });

  it('says the file is still being read when the server refuses a second read, and stays open', async () => {
    reimport.mockRejectedValue(new ApiError(409, 'geofile.import_in_progress', 'An English sentence.'));
    const onClose = vi.fn();
    show(table(corrected), onClose);

    fireEvent.click(screen.getByRole('radio', { name: 'UTF-8' }));
    fireEvent.click(screen.getByRole('button', { name: 'Read it again' }));

    expect(await screen.findByTestId('geodata-encoding-failure')).toHaveTextContent(
      'This file is still being read.',
    );
    expect(onClose).not.toHaveBeenCalled();
  });

  it('names a refused permission rather than a generic failure', async () => {
    reimport.mockRejectedValue(new ApiError(403, 'access.forbidden'));
    show(table(corrected));

    fireEvent.click(screen.getByRole('radio', { name: 'UTF-8' }));
    fireEvent.click(screen.getByRole('button', { name: 'Read it again' }));

    expect(await screen.findByTestId('geodata-encoding-failure')).toHaveTextContent(
      'Your account may not change this file.',
    );
  });
});
