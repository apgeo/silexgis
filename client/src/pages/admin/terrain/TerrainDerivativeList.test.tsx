// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import { terrainDerivativePollInterval } from '../../../api/hooks.ts';
import type { TerrainBuild, TerrainDerivativeLayerInfo } from '../../../api/hooks.ts';

const deleteMutate = vi.fn();

/** The record the server stores beside a picture, every choice spelt as its number. */
const STORED =
  '{"derivative":0,"lighting":0,"azimuthDegrees":315,"altitudeDegrees":45,"zFactor":1,"surfaceFit":0,"slopeUnit":0,"ruggednessFit":0,"computeEdges":true,"colourRamp":[]}';

function picture(overrides: Partial<TerrainDerivativeLayerInfo>): TerrainDerivativeLayerInfo {
  return {
    id: 'p1',
    terrainBuildId: 'b1',
    derivative: 'hillshade',
    name: 'Shaded relief',
    settings: STORED,
    status: 'ready',
    errorCode: null,
    message: null,
    version: 1,
    sizeBytes: 12 * 1024 * 1024,
    stale: false,
    staleReason: null,
    computedAt: '2026-08-17T06:05:00Z',
    createdAt: '2026-08-17T06:00:00Z',
    rasters: [],
    ...overrides,
  };
}

const builds: TerrainBuild[] = [
  {
    id: 'b1',
    extent: { type: 'Polygon', coordinates: [[[25, 45.5], [25.5, 45.5], [25.5, 46], [25, 46], [25, 45.5]]] },
    requestedMaxDepth: 13,
    status: 'succeeded',
    phase: 'publish',
    progress: 100,
    message: null,
    errorCode: null,
    sizeBytes: 1024,
    pyramidVersion: '1',
    heightDatum: 'orthometric',
    geoidHeightM: 0,
    surveyHeightOffsetM: 0,
    isActive: true,
    createdAt: '2026-08-17T06:00:00Z',
    startedAt: '2026-08-17T06:00:00Z',
    finishedAt: '2026-08-17T06:00:12Z',
    updatedAt: '2026-08-17T06:00:12Z',
  } as TerrainBuild,
];

let pictures: TerrainDerivativeLayerInfo[] = [];

vi.mock('../../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks.ts')>(
    '../../../api/hooks.ts',
  );
  return {
    // The polling rule itself is the real one: it is what the assertion below is about.
    terrainDerivativePollInterval: actual.terrainDerivativePollInterval,
    terrainDerivativeUnsettled: actual.terrainDerivativeUnsettled,
    useTerrainDerivatives: () => ({ data: pictures, isLoading: false }),
    useTerrainBuilds: () => ({
      data: { items: builds, page: 1, pageSize: 100, totalItems: builds.length },
      isLoading: false,
    }),
    useDeleteTerrainDerivative: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: TerrainDerivativeList } = await import('./TerrainDerivativeList.tsx');

function show(canDelete = true) {
  return render(
    <App>
      <TerrainDerivativeList canDelete={canDelete} />
    </App>,
  );
}

beforeEach(() => {
  deleteMutate.mockReset().mockResolvedValue(undefined);
  pictures = [picture({})];
});

afterEach(cleanup);

describe('TerrainDerivativeList', () => {
  it('shows what each picture was drawn from, what it cost, and which are out of date', () => {
    pictures = [
      picture({ id: 'p1', stale: true, staleReason: 'elevationReplaced' }),
      picture({
        id: 'p2',
        derivative: 'slope',
        name: 'Steepness',
        settings: STORED.replace('"slopeUnit":0', '"slopeUnit":1'),
        status: 'queued',
        sizeBytes: 0,
        computedAt: null,
        terrainBuildId: 'gone',
      }),
    ];
    show();

    expect(screen.getByText('12.0 MB')).toBeInTheDocument();
    // Nothing is on disk until a picture is computed, and "0 B" would read as one that came out empty.
    expect(screen.queryByText('0 B')).not.toBeInTheDocument();
    expect(screen.getByTestId('terrain-derivative-stale-p1')).toHaveTextContent('Out of date');
    expect(screen.queryByTestId('terrain-derivative-stale-p2')).not.toBeInTheDocument();
    // The kind under the name is the same wording the layer list uses, and it is the kind's plain
    // name: steepness is a measurement and is not presented as anything less.
    expect(screen.getByTestId('terrain-derivative-kind-p2')).toHaveTextContent(/^Steepness$/);
    expect(screen.getByTestId('terrain-derivative-params-p1')).toHaveTextContent(
      'One light · light from 315° · 45° up · Horn (eight neighbours)',
    );
    expect(screen.getByTestId('terrain-derivative-params-p2')).toHaveTextContent(
      'Horn (eight neighbours) · Rise over run, as a percentage',
    );
    expect(screen.getByText('25.0000, 45.5000, 25.5000, 46.0000')).toBeInTheDocument();
    expect(screen.getByText('a build no longer listed')).toBeInTheDocument();
    expect(screen.getByText('Queued')).toBeInTheDocument();
  });

  it('lets a failed picture explain itself in the server’s words', () => {
    pictures = [
      picture({
        id: 'p1',
        status: 'failed',
        errorCode: 'terrain_derivative.compute_failed',
        message: 'The elevation raster could not be read.',
        sizeBytes: 0,
      }),
    ];
    show();

    expect(screen.getByTestId('terrain-derivative-failure-p1')).toHaveTextContent('could not be read');
  });

  it('removes a picture behind a confirmation that names what it costs, and refuses in words', async () => {
    show();

    fireEvent.click(screen.getByTestId('terrain-derivative-delete-p1'));
    expect(await screen.findByText(/12\.0 MB it is keeping on disk/)).toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('Deleted.')).toBeInTheDocument();
    expect(deleteMutate).toHaveBeenCalledWith('p1');
  });

  it('turns a refusal into the sentence it deserves', async () => {
    deleteMutate.mockRejectedValue(new ApiError(404, 'terrain_derivative.not_found'));
    show();

    fireEvent.click(screen.getByTestId('terrain-derivative-delete-p1'));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('That picture is no longer here.')).toBeInTheDocument();
  });

  it('offers no delete to somebody who may not', () => {
    show(false);
    expect(screen.queryByTestId('terrain-derivative-delete-p1')).not.toBeInTheDocument();
  });

  it('answers an installation that has computed nothing with what to do next', () => {
    pictures = [];
    show();
    expect(screen.getByTestId('terrain-no-derivatives')).toHaveTextContent('Ask for one above');
  });

  // The rule the register's query hands to the cache, not a neighbouring one that says the same:
  // closely while anything is being computed, slowly otherwise, and never off, because the
  // addresses in each raster expire.
  it('asks closely while a picture is being computed and slowly once none is', () => {
    expect(terrainDerivativePollInterval([{ status: 'ready' }, { status: 'computing' }])).toBe(2000);
    expect(terrainDerivativePollInterval([{ status: 'queued' }])).toBe(2000);
    expect(terrainDerivativePollInterval([{ status: 'ready' }, { status: 'failed' }])).toBe(8 * 60_000);
    expect(terrainDerivativePollInterval([])).toBe(8 * 60_000);
    expect(terrainDerivativePollInterval(undefined)).toBe(8 * 60_000);
  });
});
