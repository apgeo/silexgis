// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import Polygon, { fromExtent } from 'ol/geom/Polygon';
import { Draw } from 'ol/interaction';
import { transformExtent } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TerrainBuild } from '../../api/hooks.ts';
import type { TerrainBbox } from './terrain/terrainArea.ts';

const submitMutate = vi.fn();
const uploadMutate = vi.fn();
const directoriesEnabled = vi.fn();

let builds: TerrainBuild[] = [];

let capabilities: { domains: Record<string, string>; isFullAdmin: boolean } | undefined = {
  domains: { terrain: 'read, execute, delete' },
  isFullAdmin: false,
};

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    // The real predicate, never a stub: the page's read/execute split is only worth asserting if
    // the thing doing the splitting is the one that ships.
    hasAccessAction: actual.hasAccessAction,
    terrainBuildUnsettled: actual.terrainBuildUnsettled,
    useCapabilities: () => ({ data: capabilities }),
    useSubmitTerrainBuild: () => ({ mutateAsync: submitMutate, isPending: false }),
    useUploadTerrainRaster: () => ({ mutateAsync: uploadMutate, isPending: false }),
    useTerrainSourceDirectories: (enabled: boolean) => {
      directoriesEnabled(enabled);
      return { data: enabled ? { roots: ['/srv/rasters'] } : undefined };
    },
    // The list sits under the form on the same page; it has tests of its own, and here it mostly
    // has to be able to render empty. The one thing the page itself reads out of it is whether
    // any build has met an installation with nothing to make tiles with.
    useTerrainBuilds: () => ({
      data: { items: builds, page: 1, pageSize: 10, totalItems: builds.length },
      isLoading: false,
    }),
    useTerrainBuild: () => ({ data: undefined, isLoading: false }),
    useChooseTerrainBuild: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useStopDrawingTerrainBuild: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useDeleteTerrainBuild: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const { default: TerrainPage } = await import('./TerrainPage.tsx');

/** The draw interaction the area picker attached to its own map. */
function attachedDraw(): Draw {
  const draws = vi
    .mocked(Map.prototype.addInteraction)
    .mock.calls.map((call) => call[0])
    .filter((interaction) => interaction instanceof Draw);
  expect(draws.length).toBeGreaterThan(0);
  return draws[draws.length - 1];
}

/** Draws a rectangle on the page's map the way a dragging user would. */
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
      <TerrainPage />
    </App>,
  );
}

beforeEach(() => {
  submitMutate.mockReset().mockResolvedValue({});
  uploadMutate.mockReset().mockResolvedValue({ reference: 'r1', sizeBytes: 1 });
  directoriesEnabled.mockReset();
  builds = [];
  capabilities = { domains: { terrain: 'read, execute, delete' }, isFullAdmin: false };
  vi.spyOn(Map.prototype, 'addInteraction');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('TerrainPage', () => {
  it('refuses a caller with no terrain right, and opens for one who holds it', () => {
    // A caller holding rights elsewhere and none at all over terrain — the state has to be built,
    // not assumed from some other role that probably cannot.
    capabilities = { domains: { caves: 'read, write' }, isFullAdmin: false };
    const refused = show();
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Start build/ })).not.toBeInTheDocument();
    refused.unmount();

    capabilities = { domains: { terrain: 'read, execute' }, isFullAdmin: false };
    show();
    expect(screen.getByRole('heading', { name: /Terrain/ })).toBeInTheDocument();
  });

  it('offers no way to start a build to somebody who may only read, and one to who may execute', () => {
    capabilities = { domains: { terrain: 'read' }, isFullAdmin: false };
    const reader = show();
    expect(screen.getByRole('heading', { name: /Terrain/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Start build/ })).not.toBeInTheDocument();
    reader.unmount();

    capabilities = { domains: { terrain: 'read, execute' }, isFullAdmin: false };
    show();
    expect(screen.getByRole('button', { name: /Start build/ })).toBeInTheDocument();
  });

  it('offers a directory on the server only to a full administrator, and asks for none otherwise', () => {
    const ordinary = show();
    expect(screen.queryByLabelText('Directory on the server')).not.toBeInTheDocument();
    // The server refuses this list to anyone below full administration, so it is never asked for.
    expect(directoriesEnabled).toHaveBeenCalledWith(false);
    ordinary.unmount();

    capabilities = { domains: { terrain: 'read, execute, delete' }, isFullAdmin: true };
    show();
    expect(screen.getByLabelText('Directory on the server')).toBeInTheDocument();
    expect(directoriesEnabled).toHaveBeenCalledWith(true);
  });

  it('cannot start a build before a rectangle is drawn', () => {
    show();
    expect(screen.getByRole('button', { name: /Start build/ })).toBeDisabled();

    drawRectangle([25, 45.5, 25.5, 46]);

    expect(screen.getByTestId('terrain-area-extent')).toHaveTextContent('square degrees');
    expect(screen.getByRole('button', { name: /Start build/ })).toBeEnabled();
  });

  it('sends the rectangle, the depth and where the data comes from', async () => {
    show();
    drawRectangle([25, 45.5, 25.5, 46]);

    fireEvent.click(screen.getByRole('button', { name: /Start build/ }));

    await vi.waitFor(() => expect(submitMutate).toHaveBeenCalledTimes(1));
    const sent = submitMutate.mock.calls[0][0] as Record<string, unknown>;
    expect(sent.west).toBeCloseTo(25, 4);
    expect(sent.south).toBeCloseTo(45.5, 4);
    expect(sent.east).toBeCloseTo(25.5, 4);
    expect(sent.north).toBeCloseTo(46, 4);
    expect(sent.maxDepth).toBe(13);
    expect(sent.fetchCoverage).toBe(true);
    expect(sent.heightDatum).toBe('orthometric');
    expect(sent.sources).toEqual([]);
  });

  it('refuses a rectangle larger than this installation builds in one go', () => {
    show();
    drawRectangle([20, 40, 30, 46]);

    expect(screen.getByTestId('terrain-area-too-large')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Start build/ })).toBeDisabled();
  });

  it('says what a depth means, and warns when the data cannot fill it', () => {
    show();
    expect(screen.getByTestId('terrain-depth-meaning')).toHaveTextContent('Level 13');
    expect(screen.queryByTestId('terrain-depth-capped')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Depth'), { target: { value: '14' } });

    expect(screen.getByTestId('terrain-depth-meaning')).toHaveTextContent('Level 14');
    // Nothing but the thirty-metre coverage is on this build, so the deeper levels would be
    // dropped by the tile maker with nothing said afterwards.
    expect(screen.getByTestId('terrain-depth-capped')).toBeInTheDocument();
  });

  it('says the installation cannot make tiles before another rectangle is drawn against it', () => {
    const quiet = show();
    // Nothing has met it, so nothing is claimed about it: the page does not go looking for the
    // service, it reports what a build found.
    expect(screen.queryByTestId('terrain-worker-missing')).not.toBeInTheDocument();
    quiet.unmount();

    builds = [
      {
        id: 'b1',
        extent: { type: 'Polygon', coordinates: [] },
        requestedMaxDepth: 13,
        status: 'failed',
        phase: 'bake',
        progress: 45,
        message: null,
        errorCode: 'terrain_build.bake_unavailable',
        sizeBytes: 1024,
        pyramidVersion: null,
        heightDatum: 'orthometric',
        geoidHeightM: 0,
        surveyHeightOffsetM: 0,
        isActive: false,
        createdAt: '2026-08-17T06:00:00Z',
        startedAt: '2026-08-17T06:00:00Z',
        finishedAt: '2026-08-17T06:00:05Z',
        updatedAt: '2026-08-17T06:00:05Z',
      } as TerrainBuild,
    ];
    show();

    expect(screen.getByTestId('terrain-worker-missing')).toHaveTextContent(
      'docker-compose.terrain-worker.yml',
    );
    // Everything before the bake still works, so the form is not taken away — the state is not a
    // wall, it is a thing to be told.
    expect(screen.getByRole('button', { name: /Start build/ })).toBeInTheDocument();
  });

  it('says before the button is pressed that making tiles needs a service of its own', () => {
    // The common installation does not run it, the step that needs it comes after hours of
    // fetching and preparing, and nothing the server publishes says in advance whether it is
    // there. So the one thing that can be said before that cost is spent is said here.
    show();
    expect(screen.getByTestId('terrain-bake-service')).toHaveTextContent(
      'a plain installation does not run',
    );
  });

  it('will not start a build without the rasters that have not arrived', async () => {
    // One raster of two fails on the way up. The upload call for the other one has already come
    // back, so nothing about the mutation says anything is outstanding — and a build submitted
    // here would name one raster, run for hours, and produce terrain missing exactly the fine
    // data it was started for, with nothing on screen ever having said so.
    uploadMutate.mockImplementation((file: File) =>
      file.name === 'good.tif'
        ? Promise.resolve({ reference: 'r-good', sizeBytes: 1 })
        : Promise.reject(new ApiError(502, 'terrain_build.raster_unsupported')),
    );
    show();
    drawRectangle([25, 45.5, 25.5, 46]);

    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, {
      target: {
        files: [
          new File(['a'], 'good.tif', { type: 'image/tiff' }),
          new File(['b'], 'bad.tif', { type: 'image/tiff' }),
        ],
      },
    });

    const pending = await screen.findByTestId('terrain-uploads-pending');
    expect(pending).toHaveTextContent('bad.tif');
    expect(pending).not.toHaveTextContent('good.tif');
    expect(screen.getByRole('button', { name: /Start build/ })).toBeDisabled();
    expect(submitMutate).not.toHaveBeenCalled();
  });

  it('turns a refusal into the sentence it deserves', async () => {
    submitMutate.mockRejectedValue(new ApiError(409, 'terrain_build.already_building'));
    show();
    drawRectangle([25, 45.5, 25.5, 46]);

    fireEvent.click(screen.getByRole('button', { name: /Start build/ }));

    expect(await screen.findByText(/already being built/)).toBeInTheDocument();
  });
});
