// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';

import { buildThemeConfig } from '../../theme.ts';
import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import type { RegistryClustering } from '../../api/hooks.ts';
import ClusterPopulationNote from './ClusterPopulationNote.tsx';

/**
 * An invented answer in the shape the grouping route returns.
 *
 * Every figure here is made up for the test. The point of the component is that it says what the
 * answer says and invents nothing, so a fixture is enough to prove it — and a fixture keeps the
 * assertions about the sentences rather than about whatever a registry happens to hold.
 */
function clustering(over: Partial<RegistryClustering> = {}): RegistryClustering {
  return {
    measures: ['surveyedLength', 'depth'],
    requestedClusterCount: 3,
    population: {
      considered: 3000,
      eligible: 400,
      excluded: 2600,
      measures: [
        { measure: 'surveyedLength', recorded: 500, missing: 2500, soleReason: 90 },
        { measure: 'depth', recorded: 2900, missing: 100, soleReason: 12 },
      ],
    },
    scaling: [
      { measure: 'surveyedLength', mean: 120, standardDeviation: 45 },
      { measure: 'depth', mean: 30, standardDeviation: 12 },
    ],
    clusters: [
      { index: 0, count: 200, centre: [100, 20], scaledCentre: [-0.4, -0.8], meanDistanceToCentre: 0.3 },
      { index: 1, count: 198, centre: [900, 90], scaledCentre: [1.2, 1.1], meanDistanceToCentre: 0.4 },
      { index: 2, count: 2, centre: null, scaledCentre: null, meanDistanceToCentre: null },
    ],
    assignments: [],
    separation: { meanWithinDistance: 0.35, meanBetweenDistance: 2.1, ratio: 0.17 },
    minimumEligibleCount: 8,
    minimumPublishableClusterSize: 3,
    iterations: 7,
    converged: true,
    basis: 'Computed over the caves you may read.',
    ...over,
  } as RegistryClustering;
}

function renderNote(answer: RegistryClustering | undefined, unaskable = false) {
  return render(
    <ConfigProvider theme={buildThemeConfig(DEFAULT_APPEARANCE)}>
      <App>
        <ClusterPopulationNote clustering={answer} unaskable={unaskable} />
      </App>
    </ConfigProvider>,
  );
}

afterEach(cleanup);

describe('the account of who the grouping was computed over', () => {
  it('publishes the eligible, considered and excluded counts the answer carried', () => {
    renderNote(clustering());

    const said = screen.getByTestId('cave-cluster-excluded').textContent ?? '';
    expect(said).toContain('400');
    expect(said).toContain('3000');
    // The excluded count is published rather than left to be subtracted: a figure a reader has to
    // compute is a figure a reader skips, and this is the one that decides what the picture means.
    expect(said).toContain('2600');
  });

  it('names the measures the distances were taken over, in the answer’s own order', () => {
    renderNote(clustering());

    const said = screen.getByTestId('cave-cluster-measures').textContent ?? '';
    expect(said.indexOf('Surveyed length')).toBeGreaterThanOrEqual(0);
    expect(said.indexOf('Depth')).toBeGreaterThan(said.indexOf('Surveyed length'));
  });

  it('says what each measure cost, and stays silent about one that cost nothing', () => {
    renderNote(
      clustering({
        population: {
          considered: 100,
          eligible: 60,
          excluded: 40,
          measures: [
            { measure: 'surveyedLength', recorded: 100, missing: 0, soleReason: 0 },
            { measure: 'depth', recorded: 60, missing: 40, soleReason: 40 },
          ],
        },
      }),
    );

    expect(screen.queryByTestId('cave-cluster-coverage-surveyedLength')).toBeNull();
    expect(screen.getByTestId('cave-cluster-coverage-depth').textContent).toContain('40');
  });

  it('says the grouping is weak when the groups are as wide as the gaps between them', () => {
    renderNote(
      clustering({
        separation: { meanWithinDistance: 1.4, meanBetweenDistance: 1.3, ratio: 1.4 / 1.3 },
      }),
    );

    expect(screen.getByTestId('cave-cluster-weak')).toBeTruthy();
  });

  it('stays quiet about weakness when the groups really do separate', () => {
    renderNote(clustering());

    expect(screen.queryByTestId('cave-cluster-weak')).toBeNull();
  });

  it('says so when the middles do not separate at all', () => {
    renderNote(
      clustering({ separation: { meanWithinDistance: 0.9, meanBetweenDistance: 0, ratio: null } }),
    );

    expect(screen.getByTestId('cave-cluster-weak').textContent).toContain('do not separate');
  });

  it('says a group under the publishable floor was not summarised', () => {
    renderNote(clustering());

    expect(screen.getByTestId('cave-cluster-withheld').textContent).toContain('3');
  });

  it('says nothing could be grouped, and how far short the population fell', () => {
    renderNote(clustering({ clusters: [], assignments: [], separation: null }));

    expect(screen.getByTestId('cave-cluster-empty').textContent).toContain('8');
    // The account still stands when there is no grouping to account for: that is the case where a
    // reader most needs to know how few caves recorded the measures.
    expect(screen.getByTestId('cave-cluster-excluded').textContent).toContain('2600');
  });

  it('says a measure that did no work did no work', () => {
    renderNote(
      clustering({
        scaling: [
          { measure: 'surveyedLength', mean: 120, standardDeviation: 45 },
          { measure: 'depth', mean: 30, standardDeviation: 0 },
        ],
      }),
    );

    expect(screen.getByTestId('cave-cluster-inert-depth')).toBeTruthy();
    expect(screen.queryByTestId('cave-cluster-inert-surveyedLength')).toBeNull();
  });

  it('says the run was still moving caves when it stopped', () => {
    renderNote(clustering({ converged: false }));

    expect(screen.getByTestId('cave-cluster-arbitrary')).toBeTruthy();
  });

  it('renders the server’s own sentence about what it counted over, unchanged', () => {
    renderNote(clustering());

    expect(screen.getByTestId('cave-cluster-basis').textContent).toBe(
      'Computed over the caves you may read.',
    );
  });

  it('says the grouping was not asked for when the list is narrowed beyond its reach', () => {
    renderNote(undefined, true);

    expect(screen.getByTestId('cave-cluster-unaskable')).toBeTruthy();
    expect(screen.queryByTestId('cave-cluster-population')).toBeNull();
  });

  it('draws nothing at all while no grouping has been answered', () => {
    const { container } = renderNote(undefined);

    expect(container.querySelector('[data-testid="cave-cluster-population"]')).toBeNull();
  });
});
