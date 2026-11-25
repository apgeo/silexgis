// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { FeaturePassages } from '../../api/hooks.ts';

const { passagesSpy, rights } = vi.hoisted(() => ({
  passagesSpy: vi.fn(),
  rights: { terrain: 'read' as string | undefined },
}));

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    // The real predicate, so the card's gate is the one that ships.
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: { domains: { terrain: rights.terrain }, isFullAdmin: false } }),
    useFeaturePassages: (...args: unknown[]) => passagesSpy(...args),
  };
});

const { default: FeaturePassagesCard } = await import('./FeaturePassagesCard.tsx');

const answer: FeaturePassages = {
  featureId: 'feature-1',
  featureName: 'Dolina',
  isPolygon: true,
  distanceM: 50,
  hasTerrain: true,
  truncated: false,
  passages: [
    {
      caveId: 'cave-1',
      caveName: 'Pestera 1',
      lateralOffsetM: 0,
      basis: 'surveyFlags',
      isApproximation: false,
      hasAltitudes: true,
      passageLongitude: 25.1,
      passageLatitude: 46.2,
      passageAltitudeM: 900,
      outcome: 'sampled',
      groundAltitudeM: 1000,
      overburdenM: 100,
    },
    {
      caveId: 'cave-2',
      caveName: 'Pestera 2',
      lateralOffsetM: 23.46,
      basis: 'skeletonHeuristic',
      isApproximation: true,
      hasAltitudes: false,
      passageLongitude: null,
      passageLatitude: null,
      passageAltitudeM: null,
      outcome: null,
      groundAltitudeM: null,
      overburdenM: null,
    },
  ],
};

function show(
  kind: string,
  geometryType: string | null,
  data: FeaturePassages | undefined,
  { isError = false } = {},
) {
  passagesSpy.mockReturnValue({ data, isLoading: false, isError });
  return render(
    <App>
      <MemoryRouter>
        <FeaturePassagesCard featureId="feature-1" geometryType={geometryType} kind={kind} />
      </MemoryRouter>
    </App>,
  );
}

afterEach(() => {
  cleanup();
  rights.terrain = 'read';
  passagesSpy.mockReset();
});

describe('FeaturePassagesCard', () => {
  it('lists the caves beneath with the offset and the rock overhead', () => {
    show('generic', 'Polygon', answer);
    const card = screen.getByTestId('feature-passages');
    expect(card.textContent).toContain('Pestera 1');
    expect(card.textContent).toContain('Under the outline');
    expect(card.textContent).toContain('100.0 m');
    expect(card.textContent).toContain('23.5 m');
    // A drawing with no altitudes says so rather than showing nought.
    expect(card.textContent).toContain('No altitudes surveyed');
  });

  it('says when nothing runs beneath, rather than drawing an empty table', () => {
    show('generic', 'Point', { ...answer, passages: [] });
    expect(screen.getByText('No passage you may place lies within this reach.')).toBeTruthy();
  });

  it('explains missing elevation data without hiding the offsets', () => {
    show('generic', 'Polygon', { ...answer, hasTerrain: false });
    expect(screen.getByTestId('feature-passages-no-terrain')).toBeTruthy();
    expect(screen.getByTestId('feature-passages').textContent).toContain('Pestera 1');
  });

  it('is absent when the server refused, and is not asked about a cave or a line', () => {
    show('generic', 'Polygon', undefined, { isError: true });
    expect(screen.queryByTestId('feature-passages')).toBeNull();
    cleanup();
    show('cave', 'Point', answer);
    expect(screen.queryByTestId('feature-passages')).toBeNull();
    cleanup();
    show('generic', 'LineString', answer);
    expect(screen.queryByTestId('feature-passages')).toBeNull();
  });

  it('does not ask at all for a reader without the right to read terrain', () => {
    rights.terrain = undefined;
    show('generic', 'Polygon', answer);
    expect(screen.queryByTestId('feature-passages')).toBeNull();
    // The query is built switched off, so no request is sent and no refusal is logged.
    expect(passagesSpy).toHaveBeenCalledWith('feature-1', 50, false);
  });
});
