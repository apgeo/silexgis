// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { buildThemeConfig } from '../../theme.ts';
import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import type { CaveListItem, CaveListParams, RegistryClustering } from '../../api/hooks.ts';

const useRegistryClustering = vi.fn((): { data: RegistryClustering | undefined } => ({
  data: undefined,
}));

vi.mock('../../api/hooks.ts', () => ({
  useRegistryClustering: (...args: unknown[]) => useRegistryClustering(...(args as [])),
}));

const { default: CaveDistributionPanel } = await import('./CaveDistributionPanel.tsx');

function cave(id: string): CaveListItem {
  return {
    id,
    kind: 'cave',
    name: id,
    identificationCode: null,
    caveTypeId: 1,
    region: null,
    surveyedLength: 100,
    depth: 20,
    explorationStatus: 'finished',
    locationProtected: false,
    entranceCount: 1,
    geom: null,
    approximateLocation: false,
    visibility: 'public',
    updatedAt: '2026-01-01T00:00:00Z',
  } as CaveListItem;
}

function renderPanel(scope: CaveListParams) {
  return render(
    <ConfigProvider theme={buildThemeConfig(DEFAULT_APPEARANCE)}>
      <App>
        <CaveDistributionPanel caves={[cave('a'), cave('b')]} typeName={() => 'Cave'} scope={scope} />
      </App>
    </ConfigProvider>,
  );
}

/**
 * An answer that puts both drawn caves in one published group.
 *
 * A hook that is switched off still returns whatever sits in the cache under the key it named, so
 * a test that only ever hands back `undefined` cannot see a grouping being drawn when none was
 * asked for — which is the failure worth testing here.
 */
function clustering(): RegistryClustering {
  return {
    measures: ['surveyedLength', 'depth'],
    requestedClusterCount: 2,
    population: { considered: 10, eligible: 2, excluded: 8, measures: [] },
    scaling: [],
    clusters: [{ index: 0, count: 2, centre: [100, 20], scaledCentre: [0, 0], meanDistanceToCentre: 0.1 }],
    assignments: [
      { caveId: 'a', cluster: 0, distanceToCentre: 0.1 },
      { caveId: 'b', cluster: 0, distanceToCentre: 0.2 },
    ],
    separation: { meanWithinDistance: 0.3, meanBetweenDistance: 1.4, ratio: 0.21 },
    minimumEligibleCount: 2,
    minimumPublishableClusterSize: 2,
    iterations: 4,
    converged: true,
    basis: 'Computed over the caves you may read.',
    projection: null,
  } as RegistryClustering;
}

/** The legend entries the drawn chart carries, which is where a colour becomes a claim. */
async function legendLabels() {
  const frame = await screen.findByTestId('chart-correlation');
  await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
  return Array.from(frame.querySelectorAll('text')).map((n) => n.textContent ?? '');
}

/** The last arguments the panel asked the grouping with: the request, and whether it was asked. */
function lastAsk() {
  return useRegistryClustering.mock.calls.at(-1) as unknown as [Record<string, unknown>, boolean];
}

beforeEach(() => {
  useRegistryClustering.mockReset();
  useRegistryClustering.mockReturnValue({ data: undefined });
});
afterEach(cleanup);

describe('the grouping the panel asks for', () => {
  it('carries the narrowing the list ran, so the colours describe the caves on screen', () => {
    renderPanel({ caveTypeId: 3, page: 2, pageSize: 20 });
    fireEvent.click(screen.getByTitle('Length vs depth'));

    const [request, asked] = lastAsk();
    expect(asked).toBe(true);
    expect(request).toEqual({ measures: 'surveyedLength,depth', caveTypeId: 3 });
  });

  it('does not ask at all while the list is narrowed by something the grouping cannot be asked', () => {
    renderPanel({ search: 'bear' });
    fireEvent.click(screen.getByTitle('Length vs depth'));

    expect(lastAsk()[1]).toBe(false);
  });

  it('does not ask while the chart the grouping would colour is not on screen', () => {
    renderPanel({ caveTypeId: 3 });

    expect(lastAsk()[1]).toBe(false);
  });
});

/**
 * Switching a request off stops it being sent; it does not stop an answer arriving.
 *
 * The request an unaskable narrowing falls back to names the same cache entry the unnarrowed
 * grouping filled in, so a reader who looks at the scatter and then searches would otherwise see
 * the whole registry's grouping painted over the caves the search left — under a note saying the
 * caves are drawn uncoloured.
 */
describe('a grouping that was not asked for never reaches the picture', () => {
  it('colours the points when the narrowing was one the grouping could be asked', async () => {
    useRegistryClustering.mockReturnValue({ data: clustering() });
    renderPanel({ caveTypeId: 3 });
    fireEvent.click(screen.getByTitle('Length vs depth'));

    expect(await legendLabels()).toContain('Group 1');
    expect(screen.queryByTestId('cave-cluster-unaskable')).toBeNull();
  });

  it('draws no group while the narrowing is one the grouping cannot be asked', async () => {
    useRegistryClustering.mockReturnValue({ data: clustering() });
    renderPanel({ search: 'bear' });
    fireEvent.click(screen.getByTitle('Length vs depth'));

    expect(screen.getByTestId('cave-cluster-unaskable')).toBeTruthy();
    expect(screen.queryByTestId('cave-cluster-population')).toBeNull();
    expect(await legendLabels()).not.toContain('Group 1');
  });
});

describe('the principal-axes mode of the length and depth view', () => {
  function withProjection(): RegistryClustering {
    return {
      ...clustering(),
      measures: ['surveyedLength', 'depth', 'projectedExtension', 'ramificationIndex'],
      projection: {
        measures: ['surveyedLength', 'depth', 'projectedExtension'],
        droppedMeasures: ['ramificationIndex'],
        first: { loadings: [0.7, 0.5, 0.5], explainedShare: 0.62 },
        second: { loadings: [-0.2, 0.8, -0.6], explainedShare: 0.25 },
        positions: [
          { caveId: 'a', x: 1.2, y: 0.3 },
          { caveId: 'b', x: -1.2, y: -0.3 },
        ],
      },
    } as RegistryClustering;
  }

  it('asks the grouping over the measures the axes are taken over, and only in this mode', () => {
    renderPanel({ caveTypeId: 3 });
    fireEvent.click(screen.getByTitle('Length vs depth'));
    expect(lastAsk()[0].measures).toBe('surveyedLength,depth');

    fireEvent.click(screen.getByText('Principal axes'));
    expect(String(lastAsk()[0].measures).split(',').length).toBeGreaterThan(2);
    expect(lastAsk()[1]).toBe(true);
  });

  it('titles the axes with the share of the variation each carries and names the dropped measure', async () => {
    useRegistryClustering.mockReturnValue({ data: withProjection() });
    renderPanel({ caveTypeId: 3 });
    fireEvent.click(screen.getByTitle('Length vs depth'));
    fireEvent.click(screen.getByText('Principal axes'));

    const frame = await screen.findByTestId('chart-principal-axes');
    await waitFor(() => expect(frame.querySelector('svg')).not.toBeNull());
    const texts = Array.from(frame.querySelectorAll('text')).map((n) => n.textContent ?? '');
    expect(texts).toContain('Axis 1 (62 % of the variation)');
    expect(texts).toContain('Axis 2 (25 % of the variation)');
    expect(screen.getByTestId('principal-axes-dropped').textContent).toContain('Ramification index');
    expect(screen.getByTestId('principal-axes-first').textContent).toContain('Surveyed length (m) +0.70');
  });

  it('says so, and draws no chart, when the server published no projection', () => {
    useRegistryClustering.mockReturnValue({ data: { ...clustering(), projection: null } as RegistryClustering });
    renderPanel({ caveTypeId: 3 });
    fireEvent.click(screen.getByTitle('Length vs depth'));
    fireEvent.click(screen.getByText('Principal axes'));

    expect(screen.getByTestId('principal-axes-none')).toBeTruthy();
    expect(screen.queryByTestId('chart-principal-axes')).toBeNull();
  });

  it('draws no axes while the narrowing is one the grouping cannot be asked', () => {
    useRegistryClustering.mockReturnValue({ data: withProjection() });
    renderPanel({ search: 'bear' });
    fireEvent.click(screen.getByTitle('Length vs depth'));
    fireEvent.click(screen.getByText('Principal axes'));

    expect(screen.getByTestId('cave-cluster-unaskable')).toBeTruthy();
    expect(screen.queryByTestId('chart-principal-axes')).toBeNull();
  });
});
