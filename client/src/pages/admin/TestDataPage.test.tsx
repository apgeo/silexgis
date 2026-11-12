// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, ConfigProvider } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type { TestDataOverview, TestDataProfile, TestDataRun } from '../../api/hooks.ts';

const profile: TestDataProfile = {
  seed: 20261111,
  locale: 'ro',
  regions: null,
  caves: {
    count: 25,
    typeWeights: { cave: 70, pit: 18 },
    lengthMedianM: 60,
    lengthSigma: 1.6,
    maxLengthM: 25000,
    multiEntrancePercent: 15,
  },
  access: {
    visibilityPercent: { public: 45, authenticated: 40, cavingGroup: 10, private: 5 },
    locationProtectedCavesPercent: 8,
    exactLocationGrantAccountsPercent: 15,
    denyOrObjectGrantPercent: 2,
  },
  marking: { tag: 'testgenerated', descriptionFooter: true },
};

function run(overrides: Partial<TestDataRun> = {}): TestDataRun {
  return {
    id: 'r1',
    status: 'succeeded',
    phase: null,
    progressDone: 30,
    progressTotal: 30,
    seed: 20261111,
    counts: { caves: 25, entrances: 31 },
    notes: [],
    error: null,
    alongsideExistingData: false,
    requestedBy: 'u1',
    createdAt: '2026-11-11T20:00:00Z',
    startedAt: '2026-11-11T20:00:01Z',
    finishedAt: '2026-11-11T20:00:09Z',
    cancelRequestedAt: null,
    removedAt: null,
    profile,
    ...overrides,
  };
}

let overview: TestDataOverview;
const start = vi.fn();
const remove = vi.fn();
const cancel = vi.fn();

class FakeApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly detail?: string;
  readonly problem?: Record<string, unknown>;

  constructor(status: number, code?: string, detail?: string, problem?: Record<string, unknown>) {
    super(code);
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.problem = problem;
  }
}

vi.mock('../../api/client.ts', () => ({ ApiError: FakeApiError }));
vi.mock('../../api/hooks.ts', () => ({
  useTestDataOverview: () => ({ data: overview, isLoading: false, isError: false }),
  useTestDataCoverage: () => ({ data: [], isLoading: false }),
  useStartTestDataRun: () => ({ mutateAsync: start, isPending: false }),
  useCancelTestDataRun: () => ({ mutateAsync: cancel, isPending: false }),
  useRemoveTestDataRun: () => ({ mutateAsync: remove, isPending: false }),
  testDataRunIsBusy: (r: { status: string }) => ['queued', 'running', 'removing'].includes(r.status),
}));

const { default: TestDataPage } = await import('./TestDataPage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <App>
          <TestDataPage />
        </App>
      </ConfigProvider>
    </MemoryRouter>,
  );
}

describe('TestDataPage', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
    overview = {
      deploymentAllows: true,
      enabled: true,
      maxCaves: 20000,
      profiles: { small: profile, default: { ...profile, caves: { ...profile.caves, count: 500 } }, large: profile },
      existing: { caves: 0, trips: 0, documents: 0 },
      runs: [run()],
      tables: [{ table: 'caves', role: 'generated', part: 'geography', note: 'every register field' }],
    };
    start.mockReset();
    remove.mockReset();
    cancel.mockReset();
  });
  afterEach(cleanup);

  it('starts the chosen profile as edited, and asks before generating beside existing data', async () => {
    start
      .mockRejectedValueOnce(new FakeApiError(409, 'testdata.existing_data_confirm_required', undefined, { existing: { caves: 3, trips: 1, documents: 0 } }))
      .mockResolvedValueOnce(run({ id: 'r2', status: 'queued' }));
    show();

    fireEvent.change(screen.getByTestId('test-data-json'), {
      target: { value: JSON.stringify({ ...profile, caves: { ...profile.caves, count: 7 } }) },
    });
    fireEvent.click(screen.getByTestId('test-data-start'));

    await waitFor(() => expect(start).toHaveBeenCalledTimes(1));
    expect(start.mock.calls[0][0]).toMatchObject({ profile: { caves: { count: 7 } }, confirmAlongsideExistingData: false });

    // The refusal is a question, naming how much is already there.
    expect(await screen.findByText(/already holds 3 caves, 1 trips and 0 documents/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Generate anyway' }));
    await waitFor(() => expect(start).toHaveBeenCalledTimes(2));
    expect(start.mock.calls[1][0]).toMatchObject({ confirmAlongsideExistingData: true });
  });

  it('refuses to start from a profile that is not JSON', () => {
    show();
    fireEvent.change(screen.getByTestId('test-data-json'), { target: { value: '{ not json' } });
    expect((screen.getByTestId('test-data-start') as HTMLButtonElement).disabled).toBe(true);
  });

  it('removes a finished run only after confirming, and offers to cancel one still going', async () => {
    overview = { ...overview, runs: [run({ id: 'going', status: 'running', progressDone: 5 })] };
    show();
    expect(screen.queryByTestId('test-data-remove')).toBeNull();
    fireEvent.click(screen.getByTestId('test-data-cancel'));
    await waitFor(() => expect(cancel).toHaveBeenCalledWith('going'));
    cleanup();

    overview = { ...overview, runs: [run()] };
    show();
    fireEvent.click(screen.getByTestId('test-data-remove'));
    expect(remove).not.toHaveBeenCalled();
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(remove).toHaveBeenCalledWith('r1'));
  });
});
