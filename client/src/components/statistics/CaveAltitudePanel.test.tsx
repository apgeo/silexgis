// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { AltitudeDisagreement } from '../../api/hooks/entranceAltitudes.ts';

const { reportSpy, fillSpy } = vi.hoisted(() => ({ reportSpy: vi.fn(), fillSpy: vi.fn() }));

vi.mock('../../api/hooks/entranceAltitudes.ts', () => ({
  useCaveAltitudeDisagreement: (...args: unknown[]) => reportSpy(...args),
  useFillEntranceAltitude: () => ({ mutateAsync: fillSpy, isPending: false }),
}));

const { default: CaveAltitudePanel } = await import('./CaveAltitudePanel.tsx');

const report: AltitudeDisagreement = {
  scopeId: 'cave-1',
  hasTerrain: true,
  entranceCount: 2,
  comparedCount: 1,
  disagreeingCount: 1,
  notRecordedCount: 1,
  notModelledCount: 0,
  toleranceM: 20,
  truncated: false,
  entrances: [
    {
      entranceId: 'e1',
      caveId: 'cave-1',
      caveName: 'Sample cave',
      entranceName: null,
      recordedAltitudeM: 1100,
      modelledAltitudeM: 1000,
      differenceM: 100,
      verdict: 'disagrees',
      fillableFromTerrain: false,
    },
    {
      entranceId: 'e2',
      caveId: 'cave-1',
      caveName: 'Sample cave',
      entranceName: 'Upper',
      recordedAltitudeM: null,
      modelledAltitudeM: 1000,
      differenceM: null,
      verdict: 'notRecorded',
      fillableFromTerrain: true,
    },
  ],
};

/**
 * What the read failed with, when a test makes it fail. A refusal unless the test says otherwise:
 * that is what the server answers a reader who may not read the subject, and the one failure the
 * card must answer with nothing at all.
 */
let readError: unknown;
const readRefetch = vi.fn();
beforeEach(() => {
  readError = new ApiError(404, 'cave.not_found');
  readRefetch.mockReset();
});

function show(data: AltitudeDisagreement | undefined, canEdit: boolean, isError = false) {
  reportSpy.mockReturnValue({ data, isLoading: false, isError, error: isError ? readError : null, refetch: readRefetch });
  return render(
    <App>
      <MemoryRouter>
        <CaveAltitudePanel caveId="cave-1" canEdit={canEdit} />
      </MemoryRouter>
    </App>,
  );
}

afterEach(() => {
  cleanup();
  fillSpy.mockReset();
});

describe('CaveAltitudePanel', () => {
  it('renders nothing when the server refuses', () => {
    show(undefined, true, true);
    expect(screen.queryByTestId('cave-altitude-panel')).toBeNull();
  });

  it('offers the fill action only on an entrance with no altitude, and says how many disagree', () => {
    show(report, true);
    expect(screen.getAllByTestId('altitude-fill')).toHaveLength(1);
    expect(screen.getByTestId('altitude-disagreement-note')).toBeTruthy();
  });

  it('offers no fill action to a reader who may not write the cave', () => {
    show(report, false);
    expect(screen.queryByTestId('altitude-fill')).toBeNull();
  });

  it('says so when no elevation data has been prepared, instead of showing a table', () => {
    show({ ...report, hasTerrain: false }, true);
    expect(screen.getByText(/No elevation data has been prepared on this installation/)).toBeInTheDocument();
    expect(screen.queryByTestId('altitude-fill')).toBeNull();
    expect(screen.queryByTestId('altitude-disagreement-note')).toBeNull();
  });

  it('fills the entrance it was pressed on and says it did', async () => {
    fillSpy.mockResolvedValue(undefined);
    show(report, true);
    fireEvent.click(screen.getByTestId('altitude-fill'));

    expect(await screen.findByText('Altitude filled from the elevation model.')).toBeInTheDocument();
    expect(fillSpy).toHaveBeenCalledTimes(1);
    expect(fillSpy).toHaveBeenCalledWith('e2');
  });

  it('says the fill failed when the server refuses it', async () => {
    fillSpy.mockRejectedValue(new Error('refused'));
    show(report, true);
    fireEvent.click(screen.getByTestId('altitude-fill'));

    expect(await screen.findByText(/The altitude could not be filled/)).toBeInTheDocument();
    expect(screen.queryByText('Altitude filled from the elevation model.')).toBeNull();
  });
});

describe('CaveAltitudePanel when its read fails for a reason other than a refusal', () => {
  it('names the failed read with a way to ask again instead of vanishing from the page', () => {
    // A dropped connection or a server error says nothing about the subject, so the card does not
    // get to disappear: a page missing a card reads as complete.
    readError = new TypeError('Failed to fetch');
    show(undefined, true, true);

    const failed = screen.getByTestId('cave-altitude-read-failed');
    expect(failed.textContent).toContain('These figures could not be read just now');
    fireEvent.click(within(failed).getByRole('button', { name: 'Retry' }));
    expect(readRefetch).toHaveBeenCalledTimes(1);
  });

  it('still draws nothing when the server refuses', () => {
    show(undefined, true, true);
    expect(screen.queryByTestId('cave-altitude-read-failed')).toBeNull();
  });
});
