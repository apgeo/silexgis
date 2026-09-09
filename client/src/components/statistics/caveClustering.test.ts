// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import type { CaveListItem, CaveListParams, RegistryClustering } from '../../api/hooks.ts';
import {
  clusterByCave,
  clusteredScatter,
  clusteringScopeFor,
  publishableClusters,
  scatterClusteringMeasures,
  scatterPointsFor,
} from './caveClustering.ts';

function cave(id: string, surveyedLength: number | null, depth: number | null): CaveListItem {
  return {
    id,
    kind: 'cave',
    name: id,
    identificationCode: null,
    caveTypeId: 1,
    region: null,
    surveyedLength,
    depth,
    explorationStatus: 'finished',
    locationProtected: false,
    entranceCount: 1,
    geom: null,
    approximateLocation: false,
    visibility: 'public',
    updatedAt: '2026-01-01T00:00:00Z',
  } as CaveListItem;
}

function clustering(overrides: Partial<RegistryClustering> = {}): RegistryClustering {
  return {
    measures: ['surveyedLength', 'depth'],
    requestedClusterCount: 2,
    population: { considered: 10, eligible: 6, excluded: 4, measures: [] },
    scaling: [],
    clusters: [
      { index: 0, count: 4, centre: [100, 20], scaledCentre: [-0.5, -0.5], meanDistanceToCentre: 0.2 },
      { index: 1, count: 2, centre: null, scaledCentre: null, meanDistanceToCentre: null },
    ],
    assignments: [
      { caveId: 'a', cluster: 0, distanceToCentre: 0.1 },
      { caveId: 'b', cluster: 0, distanceToCentre: 0.2 },
      { caveId: 'c', cluster: 1, distanceToCentre: 0.3 },
    ],
    separation: { meanWithinDistance: 0.3, meanBetweenDistance: 1.4, ratio: 0.21 },
    minimumEligibleCount: 8,
    minimumPublishableClusterSize: 3,
    iterations: 4,
    converged: true,
    basis: 'Computed over the caves you may read.',
    ...overrides,
  } as RegistryClustering;
}

/**
 * A grouping asked about a wider set of caves than the one on screen is wrong in a way that
 * leaves no trace: every colour still lands on a point, the picture still looks answered, and
 * only a reader who knows both queries could tell. So the agreement between the two is asserted
 * here rather than trusted.
 */
describe('the grouping is asked the same question the points answer', () => {
  // One sample value per narrowing the cave list can hold. A `Record` over the list's own
  // parameter names, so a narrowing added later fails to compile until it is covered here too.
  const sample: Record<keyof CaveListParams, NonNullable<CaveListParams[keyof CaveListParams]>> = {
    page: 2,
    pageSize: 50,
    sort: '-name',
    caveTypeId: 3,
    region: 'Apuseni',
    search: 'bear',
    minLength: 100,
    bbox: '1,2,3,4',
    tag: 'survey',
  };

  /** These choose which of the narrowed caves are drawn, not which caves the narrowing holds. */
  const ordering: Array<keyof CaveListParams> = ['page', 'pageSize', 'sort'];

  it('either carries every narrowing or refuses to ask at all', () => {
    for (const key of Object.keys(sample) as Array<keyof CaveListParams>) {
      if (ordering.includes(key)) continue;
      const scope = clusteringScopeFor({ [key]: sample[key] } as CaveListParams);
      if (scope === null) continue;
      expect(scope[key as keyof typeof scope], `narrowing "${key}" reached no grouping request`).toBe(
        sample[key],
      );
    }
  });

  it('asks over the two measures the scatter draws, carrying the narrowings the route accepts', () => {
    expect(clusteringScopeFor({ caveTypeId: 3, page: 2, sort: '-name' })).toEqual({
      measures: scatterClusteringMeasures,
      caveTypeId: 3,
    });
  });

  it('refuses to ask while a narrowing the grouping has no parameter for is in force', () => {
    expect(clusteringScopeFor({ search: 'bear' })).toBeNull();
    expect(clusteringScopeFor({ tag: 'survey' })).toBeNull();
    expect(clusteringScopeFor({ minLength: 100 })).toBeNull();
    expect(clusteringScopeFor({ bbox: '1,2,3,4' })).toBeNull();
  });

  // A narrowing both routes accept is still uncarriable when the two do not agree on what it
  // means: the list matches a region by case-insensitive substring, the grouping by equality, so
  // "apuseni" narrows the points to every region containing it and would group none of them.
  it('refuses to ask about a region, because the two routes do not match one the same way', () => {
    expect(clusteringScopeFor({ region: 'Apuseni' })).toBeNull();
    expect(clusteringScopeFor({ region: 'apuseni', caveTypeId: 3 })).toBeNull();
  });

  it('treats an empty search as no search, because that is how the list spells it', () => {
    expect(clusteringScopeFor({ search: '', caveTypeId: 3 })).not.toBeNull();
  });

  it('asks over every readable cave when the list is not narrowed', () => {
    expect(clusteringScopeFor({ page: 1, pageSize: 20 })).toEqual({
      measures: scatterClusteringMeasures,
      caveTypeId: undefined,
    });
  });
});

describe('a group the server declined to summarise is not drawn as a group', () => {
  it('publishes only the groups holding at least the server’s floor', () => {
    expect([...publishableClusters(clustering())]).toEqual([0]);
  });

  it('leaves the caves of a withheld group unlabelled rather than dropping them', () => {
    const byCave = clusterByCave(clustering());
    expect(byCave.get('a')).toBe(0);
    expect(byCave.has('c')).toBe(false);

    const groups = clusteredScatter([cave('a', 100, 20), cave('c', 500, 90)], clustering());
    expect(groups).not.toBeNull();
    expect(groups!.map((g) => g.cluster)).toEqual([0, null]);
    expect(groups!.find((g) => g.cluster === null)!.points).toEqual([[500, 90]]);
  });

  it('withholds a group whose count clears the floor but whose centre the server did not publish', () => {
    const withheld = clustering({
      clusters: [
        { index: 0, count: 4, centre: null, scaledCentre: null, meanDistanceToCentre: null },
      ],
    });
    expect([...publishableClusters(withheld)]).toEqual([]);
  });
});

describe('the points the scatter draws', () => {
  it('keeps the cave a point came from, so a group label can reach it', () => {
    expect(scatterPointsFor([cave('a', 100, -20)])).toEqual([{ caveId: 'a', point: [100, 20] }]);
  });

  it('leaves out a cave missing either measure rather than plotting it at zero', () => {
    expect(scatterPointsFor([cave('a', 100, null), cave('b', null, 20), cave('c', 0, 20)])).toEqual([]);
  });

  it('draws no legend at all when there is no grouping to show', () => {
    expect(clusteredScatter([cave('a', 100, 20)], undefined)).toBeNull();
    expect(clusteredScatter([], clustering())).toBeNull();
  });

  it('draws no legend when the grouping placed none of these caves in a published group', () => {
    expect(clusteredScatter([cave('c', 500, 90)], clustering())).toBeNull();
  });

  it('orders the groups by the label the server gave them, with the ungrouped last', () => {
    const three = clustering({
      clusters: [
        { index: 0, count: 3, centre: [1, 1], scaledCentre: [0, 0], meanDistanceToCentre: 0.1 },
        { index: 1, count: 3, centre: [2, 2], scaledCentre: [1, 1], meanDistanceToCentre: 0.1 },
      ],
      assignments: [
        { caveId: 'a', cluster: 1, distanceToCentre: 0.1 },
        { caveId: 'b', cluster: 0, distanceToCentre: 0.1 },
      ],
    });
    const groups = clusteredScatter([cave('a', 10, 10), cave('b', 20, 20), cave('z', 30, 30)], three);
    expect(groups!.map((g) => g.cluster)).toEqual([0, 1, null]);
  });
});
