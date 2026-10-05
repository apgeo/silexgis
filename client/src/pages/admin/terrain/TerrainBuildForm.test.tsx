// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import Polygon, { fromExtent } from 'ol/geom/Polygon';
import { Draw } from 'ol/interaction';
import { transformExtent } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import type { TerrainBuild } from '../../../api/hooks.ts';
import type { TerrainBbox } from './terrainArea.ts';

const submitMutate = vi.fn();
const uploadMutate = vi.fn();

function build(overrides: Partial<TerrainBuild>): TerrainBuild {
  return {
    id: 'b-region-00000000',
    extent: {
      type: 'Polygon',
      coordinates: [[[25, 45.5], [25.5, 45.5], [25.5, 46], [25, 46], [25, 45.5]]],
    },
    requestedMaxDepth: 11,
    status: 'succeeded',
    phase: 'publish',
    progress: 100,
    message: null,
    errorCode: null,
    sizeBytes: 1024,
    pyramidVersion: '1.0.0-abc',
    heightDatum: 'ellipsoidal',
    geoidHeightM: 41.5,
    surveyHeightOffsetM: 41.5,
    isActive: true,
    createdAt: '2026-08-17T06:00:00Z',
    startedAt: '2026-08-17T06:00:00Z',
    finishedAt: '2026-08-17T06:00:12Z',
    updatedAt: '2026-08-17T06:00:12Z',
    baseBuildId: null,
    ...overrides,
  } as TerrainBuild;
}

let builds: TerrainBuild[] = [];

vi.mock('../../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks.ts')>(
    '../../../api/hooks.ts',
  );
  return {
    terrainBuildUnsettled: actual.terrainBuildUnsettled,
    useSubmitTerrainBuild: () => ({ mutateAsync: submitMutate, isPending: false }),
    useUploadTerrainRaster: () => ({ mutateAsync: uploadMutate, isPending: false }),
    useTerrainSourceDirectories: () => ({ data: undefined }),
    useTerrainBuilds: () => ({
      data: { items: builds, page: 1, pageSize: 100, totalItems: builds.length },
      isLoading: false,
    }),
  };
});

const { default: TerrainBuildForm } = await import('./TerrainBuildForm.tsx');

/** The draw interaction the area picker attached to its own map. */
function attachedDraw(): Draw {
  const draws = vi
    .mocked(Map.prototype.addInteraction)
    .mock.calls.map((call) => call[0])
    .filter((interaction) => interaction instanceof Draw);
  expect(draws.length).toBeGreaterThan(0);
  return draws[draws.length - 1];
}

/** Draws a rectangle on the form's map the way a dragging user would. */
function drawRectangle(bbox: TerrainBbox) {
  fireEvent.click(screen.getByRole('button', { name: /Draw rectangle/ }));
  const geometry = new Polygon(
    fromExtent(transformExtent(bbox, 'EPSG:4326', 'EPSG:3857')).getCoordinates(),
  );
  act(() => {
    attachedDraw().dispatchEvent({ type: 'drawend', feature: new Feature({ geometry }) } as never);
  });
}

function show() {
  return render(
    <App>
      <TerrainBuildForm canExecute isFullAdmin={false} />
    </App>,
  );
}

/** Opens the extension picker and chooses the option whose label starts with the build's id. */
function chooseBase(idPrefix: string) {
  openPicker();
  fireEvent.click(listedOption(idPrefix)!);
}

/**
 * The option in the open list whose label starts with the build's id, or undefined. The label is
 * rendered twice by the control (once in the list, once for assistive technology), so the one in
 * the list is the one a pointer reaches.
 */
function listedOption(idPrefix: string): HTMLElement | undefined {
  return screen
    .queryAllByText(new RegExp(`^${idPrefix}`))
    .find((element) => element.closest('.ant-select-item-option') !== null);
}

/** Opens the extension picker's list of builds. */
function openPicker() {
  fireEvent.mouseDown(within(screen.getByTestId('terrain-extend-base')).getByRole('combobox'));
}

beforeEach(() => {
  submitMutate.mockReset().mockResolvedValue({});
  uploadMutate.mockReset().mockResolvedValue({ reference: 'r1', sizeBytes: 1 });
  builds = [];
  vi.spyOn(Map.prototype, 'addInteraction');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('extending a finished build', () => {
  it('offers only builds whose tiles were checked whole, and nothing when there are none', () => {
    const none = show();
    expect(screen.queryByTestId('terrain-extend-base')).not.toBeInTheDocument();
    none.unmount();

    builds = [
      build({ id: 'b-region-00000000' }),
      build({ id: 'b-running-0000000', status: 'running', phase: 'bake', pyramidVersion: null }),
      build({ id: 'b-unchecked-00000', pyramidVersion: null }),
    ];
    show();
    openPicker();
    expect(listedOption('b-region')).toBeDefined();
    expect(listedOption('b-running')).toBeUndefined();
    expect(listedOption('b-unchecked')).toBeUndefined();
  });

  it('takes the depth and the heights from the base, holds them, and names the base when starting', async () => {
    builds = [build({ id: 'b-region-00000000', requestedMaxDepth: 11 })];
    show();
    drawRectangle([25.1, 45.6, 25.2, 45.7]);

    const depth = screen.getByTestId('terrain-depth') as HTMLInputElement;
    expect(depth).toBeEnabled();

    chooseBase('b-region');

    // The addition cannot re-mesh what is there at another depth, and tiles from two datums meet
    // in a step along the join: both are the base's, and the form says so instead of asking.
    expect(depth).toHaveValue('11');
    expect(depth).toBeDisabled();
    expect(screen.getByTestId('terrain-extend-locked')).toHaveTextContent('level 11');
    // The base already holds the coverage, so it is not obtained again unless asked for.
    const coverage = screen.getByLabelText(/Obtain coverage/) as HTMLInputElement;
    expect(coverage.checked).toBe(false);

    // Asked for anyway here, so the build has something to add without an upload in this test.
    fireEvent.click(coverage);
    fireEvent.click(screen.getByRole('button', { name: /Start build/ }));

    await vi.waitFor(() => expect(submitMutate).toHaveBeenCalledTimes(1));
    const sent = submitMutate.mock.calls[0][0] as Record<string, unknown>;
    expect(sent.baseBuildId).toBe('b-region-00000000');
    expect(sent.maxDepth).toBe(11);
    expect(sent.heightDatum).toBe('ellipsoidal');
    expect(sent.geoidHeightM).toBe(41.5);
    expect(sent.fetchCoverage).toBe(true);
  });

  it('starts a plain build with no base named when the picker is left empty', async () => {
    builds = [build({ id: 'b-region-00000000' })];
    show();
    drawRectangle([25.1, 45.6, 25.2, 45.7]);
    fireEvent.click(screen.getByRole('button', { name: /Start build/ }));

    await vi.waitFor(() => expect(submitMutate).toHaveBeenCalledTimes(1));
    const sent = submitMutate.mock.calls[0][0] as Record<string, unknown>;
    expect('baseBuildId' in sent).toBe(false);
    expect(screen.getByTestId('terrain-depth')).toBeEnabled();
  });
});
