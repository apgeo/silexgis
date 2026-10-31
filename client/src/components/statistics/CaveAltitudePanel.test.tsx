// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AltitudeDisagreement } from '../../api/hooks/entranceAltitudes.ts';

const { reportSpy } = vi.hoisted(() => ({ reportSpy: vi.fn() }));

vi.mock('../../api/hooks/entranceAltitudes.ts', () => ({
  useCaveAltitudeDisagreement: (...args: unknown[]) => reportSpy(...args),
  useFillEntranceAltitude: () => ({ mutateAsync: vi.fn(), isPending: false }),
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

function show(data: AltitudeDisagreement | undefined, canEdit: boolean, isError = false) {
  reportSpy.mockReturnValue({ data, isLoading: false, isError });
  return render(
    <App>
      <MemoryRouter>
        <CaveAltitudePanel caveId="cave-1" canEdit={canEdit} />
      </MemoryRouter>
    </App>,
  );
}

afterEach(cleanup);

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
    expect(screen.queryByTestId('altitude-fill')).toBeNull();
    expect(screen.getByTestId('cave-altitude-panel')).toBeTruthy();
  });
});
