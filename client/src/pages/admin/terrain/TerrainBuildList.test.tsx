// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import { terrainListPollInterval } from '../../../api/hooks.ts';
import type { TerrainBuild, TerrainBuildDetail } from '../../../api/hooks.ts';

const chooseMutate = vi.fn();
const stopMutate = vi.fn();
const deleteMutate = vi.fn();

function build(overrides: Partial<TerrainBuild>): TerrainBuild {
  return {
    id: 'b1',
    extent: { type: 'Polygon', coordinates: [[[25, 45.5], [25.5, 45.5], [25.5, 46], [25, 46], [25, 45.5]]] },
    requestedMaxDepth: 13,
    status: 'succeeded',
    phase: 'publish',
    progress: 100,
    message: null,
    errorCode: null,
    sizeBytes: 48 * 1024 * 1024,
    pyramidVersion: '1.2.3',
    heightDatum: 'orthometric',
    geoidHeightM: 0,
    surveyHeightOffsetM: 0,
    isActive: false,
    createdAt: '2026-08-17T06:00:00Z',
    startedAt: '2026-08-17T06:00:00Z',
    finishedAt: '2026-08-17T06:00:12Z',
    updatedAt: '2026-08-17T06:00:12Z',
    ...overrides,
  } as TerrainBuild;
}

let builds: TerrainBuild[] = [];
let detail: TerrainBuildDetail | undefined;

vi.mock('../../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks.ts')>(
    '../../../api/hooks.ts',
  );
  return {
    // The polling rules themselves are the real ones: they are what the assertions below are about.
    terrainBuildPollInterval: actual.terrainBuildPollInterval,
    terrainListPollInterval: actual.terrainListPollInterval,
    terrainBuildUnsettled: actual.terrainBuildUnsettled,
    useTerrainBuilds: () => ({
      data: { items: builds, page: 1, pageSize: 10, totalItems: builds.length },
      isLoading: false,
    }),
    useTerrainBuild: () => ({ data: detail, isLoading: detail === undefined }),
    useChooseTerrainBuild: () => ({ mutateAsync: chooseMutate, isPending: false }),
    useStopDrawingTerrainBuild: () => ({ mutateAsync: stopMutate, isPending: false }),
    useDeleteTerrainBuild: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: TerrainBuildList } = await import('./TerrainBuildList.tsx');

function show(props: { canExecute?: boolean; canDelete?: boolean } = {}) {
  return render(
    <App>
      <TerrainBuildList canExecute={props.canExecute ?? true} canDelete={props.canDelete ?? true} />
    </App>,
  );
}

beforeEach(() => {
  chooseMutate.mockReset().mockResolvedValue({});
  stopMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
  builds = [build({})];
  detail = { build: builds[0], logTail: null, sources: [] };
});

afterEach(cleanup);

describe('TerrainBuildList', () => {
  it('shows what a build covers, what it cost on disk, and which one is drawn', () => {
    builds = [build({ id: 'b1', isActive: true }), build({ id: 'b2', sizeBytes: null })];
    show();

    expect(screen.getAllByText('25.0000, 45.5000, 25.5000, 46.0000')).toHaveLength(2);
    // The whole build, not the tiles alone: 46 MB against roughly a megabyte of published tiles
    // in a real run, which is exactly why the column says what it counts.
    expect(screen.getAllByText('48.0 MB').length).toBeGreaterThan(0);
    expect(screen.getAllByText('On disk').length).toBeGreaterThan(0);
    expect(screen.getByTestId('terrain-active-b1')).toBeInTheDocument();
    expect(screen.queryByTestId('terrain-active-b2')).not.toBeInTheDocument();
  });

  it('does not offer a build with no stamped version, and does offer one that has it', () => {
    // A build that ran to the end but stamped no version has nothing to draw: the server refuses
    // it with a code of its own, so it is never offered.
    builds = [
      build({ id: 'unstamped', pyramidVersion: null }),
      build({ id: 'stamped', pyramidVersion: '2026.1' }),
    ];
    show();

    expect(screen.queryByTestId('terrain-activate-unstamped')).not.toBeInTheDocument();
    expect(screen.getByTestId('terrain-activate-stamped')).toBeInTheDocument();
  });

  it('does not offer to choose the build already being drawn, and offers to stop drawing it', () => {
    builds = [build({ id: 'drawn', isActive: true })];
    show();

    expect(screen.queryByTestId('terrain-activate-drawn')).not.toBeInTheDocument();
    expect(screen.getByTestId('terrain-stop-drawn')).toBeInTheDocument();
  });

  it('says in words that the scene changed, because the scene itself cannot say so', async () => {
    builds = [build({ id: 'stamped' })];
    show();

    fireEvent.click(screen.getByTestId('terrain-activate-stamped'));

    expect(await screen.findByText(/scene now draws this build/)).toBeInTheDocument();
    expect(chooseMutate).toHaveBeenCalledWith('stamped');
  });

  it('names what a delete destroys, and turns the refusal for the drawn one into a sentence', async () => {
    deleteMutate.mockImplementation((id: string) =>
      id === 'drawn'
        ? Promise.reject(new ApiError(409, 'terrain_build.active'))
        : Promise.resolve(undefined),
    );

    builds = [build({ id: 'drawn', isActive: true })];
    const drawn = show();
    fireEvent.click(screen.getByTestId('terrain-delete-drawn'));
    expect(await screen.findByText(/48\.0 MB it is keeping on disk/)).toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    expect(await screen.findByText(/terrain the scene is drawing/)).toBeInTheDocument();
    drawn.unmount();

    // The same control on a build nothing is drawing goes through, which is what makes the
    // refusal above a statement about that build rather than about the button.
    builds = [build({ id: 'old' })];
    show();
    fireEvent.click(screen.getByTestId('terrain-delete-old'));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    expect(await screen.findByText('Deleted.')).toBeInTheDocument();
  });

  it('offers nothing to change to somebody who may only read', () => {
    builds = [build({ id: 'b1', isActive: true })];
    const reader = show({ canExecute: false, canDelete: false });
    expect(screen.queryByTestId('terrain-delete-b1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('terrain-stop-b1')).not.toBeInTheDocument();
    reader.unmount();

    builds = [build({ id: 'b1', isActive: true })];
    show({ canExecute: true, canDelete: true });
    expect(screen.getByTestId('terrain-delete-b1')).toBeInTheDocument();
    expect(screen.getByTestId('terrain-stop-b1')).toBeInTheDocument();
  });

  // The two rights are separate bits with nothing implied between them, and the server asks a
  // different one of the two on each control: which build the scene draws is execute, in both
  // directions; removing a build and what it left on disk is delete. Held together, as they were,
  // this reads as correct while offering somebody a button that always refuses and withholding
  // from somebody else one the server would have accepted.
  it('asks the right each control actually needs, and neither one for both', () => {
    builds = [build({ id: 'b1', isActive: true })];
    const runner = show({ canExecute: true, canDelete: false });
    expect(screen.getByTestId('terrain-stop-b1')).toBeInTheDocument();
    expect(screen.queryByTestId('terrain-delete-b1')).not.toBeInTheDocument();
    runner.unmount();

    builds = [build({ id: 'b1', isActive: true })];
    show({ canExecute: false, canDelete: true });
    expect(screen.queryByTestId('terrain-stop-b1')).not.toBeInTheDocument();
    expect(screen.getByTestId('terrain-delete-b1')).toBeInTheDocument();
  });

  it('stops asking once nothing on the page is still running, and asks while anything is', () => {
    // This is the rule the list itself hands to the query, not a neighbouring one that happens to
    // say the same thing: the interval disables itself rather than being cleared by whoever
    // navigated away, which is the only reason a page left open on finished builds costs nothing.
    expect(terrainListPollInterval([{ status: 'succeeded' }, { status: 'failed' }])).toBe(false);
    expect(terrainListPollInterval([{ status: 'succeeded' }, { status: 'running' }])).toBe(2000);
    expect(terrainListPollInterval([{ status: 'queued' }])).toBe(2000);
    expect(terrainListPollInterval([])).toBe(false);
    expect(terrainListPollInterval(undefined)).toBe(false);
  });

  it('says on the row which step a running build is on, and says it of no settled one', () => {
    builds = [
      build({ id: 'running', status: 'running', phase: 'bake', progress: 42, sizeBytes: null }),
      build({ id: 'queued', status: 'queued', phase: 'pending', progress: 0, sizeBytes: null }),
      build({ id: 'done' }),
    ];
    show();

    // Only one build runs at a time and the newest row is often the queued one, so the row has to
    // say which of them is working without every row being opened in turn.
    expect(screen.getByTestId('terrain-row-phase-running')).toHaveTextContent('Bake 42%');
    // A queued build has started no step; a percentage against it would be a number about nothing.
    expect(screen.queryByTestId('terrain-row-phase-queued')).not.toBeInTheDocument();
    expect(screen.queryByTestId('terrain-row-phase-done')).not.toBeInTheDocument();
  });
});

describe('the pipeline view', () => {
  it('draws the steps in the order they run, with the one it stopped at in error', () => {
    const failed = build({
      id: 'b1',
      status: 'failed',
      phase: 'bake',
      progress: 45,
      message: 'The tile maker ran short of memory and wrote coarser tiles than were asked for.',
      errorCode: 'terrain_build.bake_degraded',
      pyramidVersion: null,
      sizeBytes: 1024,
    });
    builds = [failed];
    detail = {
      build: failed,
      logTail: 'CRITICAL memory pressure\nstopping',
      sources: [
        { id: 1, kind: 'fetched', reference: 'copernicus', attribution: 'Copernicus', licence: null },
      ],
    };
    show();

    const steps = screen.getByTestId('terrain-pipeline');
    const labels = Array.from(steps.querySelectorAll('[data-testid^="terrain-phase-"]')).map(
      (node) => node.getAttribute('data-testid'),
    );
    expect(labels).toEqual([
      'terrain-phase-fetch',
      'terrain-phase-prepare',
      'terrain-phase-bake',
      'terrain-phase-validate',
      'terrain-phase-publish',
    ]);
    expect(screen.getByTestId('terrain-build-failure')).toHaveTextContent(
      'coarser tiles than were asked for',
    );
    expect(screen.getByTestId('terrain-log-tail')).toHaveTextContent('CRITICAL memory pressure');
    expect(screen.getByTestId('terrain-build-sources')).toHaveTextContent('Obtained coverage');
  });

  // A plain installation does not run the tile-making service, so this is the ordinary outcome
  // rather than a rare one, and it must not read as a fault of the build that met it.
  it('explains a missing tile-maker rather than sounding the alarm, and does not repeat the page', () => {
    const stopped = build({
      id: 'b1',
      status: 'failed',
      phase: 'bake',
      progress: 45,
      message: 'This installation has nothing that can turn rasters into tiles.',
      errorCode: 'terrain_build.bake_unavailable',
      pyramidVersion: null,
    });
    builds = [stopped];
    detail = { build: stopped, logTail: null, sources: [] };
    show();

    const notice = screen.getByTestId('terrain-build-bake-missing');
    expect(notice).toHaveTextContent('nothing that can turn rasters into tiles');
    // What to type is said once, above the form, where somebody about to draw another rectangle
    // reads it: the same alert twice on one screen makes neither of them read as the answer.
    expect(screen.queryByTestId('terrain-worker-missing')).not.toBeInTheDocument();
    expect(notice).not.toHaveTextContent('SILEXGIS__Terrain__BakeEnabled=true');
    // What it says about this build is only what is true of it: the work before the bake stands,
    // and nothing anywhere can carry that build on from where it stopped.
    expect(notice).toHaveTextContent('still on disk under it');
    expect(screen.queryByTestId('terrain-build-failure')).not.toBeInTheDocument();
  });

  it('answers an installation that has built nothing with what to do next', () => {
    builds = [];
    detail = undefined;
    show();

    expect(screen.getByTestId('terrain-no-builds')).toHaveTextContent(
      'No elevation surface has been built for this installation yet.',
    );
    expect(screen.getByTestId('terrain-no-builds')).toHaveTextContent('Draw a rectangle above');
    // And nothing pretends to be watching a build that does not exist.
    expect(screen.queryByTestId('terrain-build-detail')).not.toBeInTheDocument();
  });

  it('says a queued build has not started, rather than drawing it as though it had', () => {
    const queued = build({
      id: 'b1',
      status: 'queued',
      phase: 'pending',
      progress: 0,
      pyramidVersion: null,
      startedAt: null,
      finishedAt: null,
      sizeBytes: null,
    });
    builds = [queued];
    detail = { build: queued, logTail: null, sources: [] };
    show();

    expect(screen.getByTestId('terrain-phase-pending')).toBeInTheDocument();
    expect(screen.queryByTestId('terrain-log-tail')).not.toBeInTheDocument();
  });
});
