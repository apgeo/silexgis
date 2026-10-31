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
const cancelMutate = vi.fn();
const retryMutate = vi.fn();

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
    pyramidBytes: null,
    achievedMaxDepth: null,
    pyramidVersion: '1.2.3',
    heightDatum: 'orthometric',
    geoidHeightM: 0,
    surveyHeightOffsetM: 0,
    isActive: false,
    createdAt: '2026-08-17T06:00:00Z',
    startedAt: '2026-08-17T06:00:00Z',
    finishedAt: '2026-08-17T06:00:12Z',
    updatedAt: '2026-08-17T06:00:12Z',
    cancelRequestedAt: null,
    bakeTimeLimit: null,
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
    useCancelTerrainBuild: () => ({ mutateAsync: cancelMutate, isPending: false }),
    useRetryTerrainBuild: () => ({ mutateAsync: retryMutate, isPending: false }),
  };
});

const { default: TerrainBuildList } = await import('./TerrainBuildList.tsx');

/**
 * The confirmation's own button, which carries the same words as the row's: told apart by the
 * row's test id, which the confirmation's button does not have.
 */
async function confirmCallOff(): Promise<HTMLElement> {
  const buttons = await screen.findAllByRole('button', { name: 'Call off' });
  const confirm = buttons.find((button) => !button.dataset.testid);
  expect(confirm).toBeDefined();
  return confirm!;
}

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
  cancelMutate.mockReset().mockResolvedValue({});
  retryMutate.mockReset().mockResolvedValue({});
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

  it('says which size it shows, and the depth reached when it is less than the depth asked for', () => {
    builds = [
      build({ id: 'capped', requestedMaxDepth: 16, achievedMaxDepth: 13, pyramidBytes: 1.1 * 1024 * 1024 }),
      build({ id: 'whole', requestedMaxDepth: 13, achievedMaxDepth: 13, pyramidBytes: 2 * 1024 * 1024 }),
      build({ id: 'unchecked', requestedMaxDepth: 14, achievedMaxDepth: null, pyramidBytes: null }),
    ];
    show();

    // The cap is shown on the row it happened to, with what was asked for beside it ...
    expect(screen.getByTestId('terrain-depth-short-capped')).toHaveTextContent('13 (asked for 16)');
    // ... and nowhere else: a build that reached its depth, or one not yet checked, shows one number.
    expect(screen.queryByTestId('terrain-depth-short-whole')).not.toBeInTheDocument();
    expect(screen.queryByTestId('terrain-depth-short-unchecked')).not.toBeInTheDocument();

    // The whole folder and the tiles alone are both on the row, each labelled as what it is.
    expect(screen.getByTestId('terrain-pyramid-size-capped')).toHaveTextContent('1.1 MB of tiles');
    expect(screen.getByTestId('terrain-pyramid-size-whole')).toHaveTextContent('2.0 MB of tiles');
    expect(screen.queryByTestId('terrain-pyramid-size-unchecked')).not.toBeInTheDocument();
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

  it('tells a build being called off and one that was apart from running and failed ones', () => {
    builds = [
      build({ id: 'running', status: 'running', phase: 'fetch' }),
      build({ id: 'stopping', status: 'running', phase: 'bake', cancelRequestedAt: '2026-10-15T09:00:00Z' }),
      build({ id: 'failed', status: 'failed', errorCode: 'terrain_build.bake_failed' }),
      build({ id: 'off', status: 'failed', errorCode: 'terrain_build.cancelled' }),
    ];
    show();

    expect(screen.getByTestId('terrain-state-running')).toHaveTextContent('Running');
    expect(screen.getByTestId('terrain-state-stopping')).toHaveTextContent('Being cancelled');
    expect(screen.getByTestId('terrain-state-failed')).toHaveTextContent('Failed');
    expect(screen.getByTestId('terrain-state-off')).toHaveTextContent('Cancelled');
  });

  it('offers to call off only what has not stopped, and to run again only what has', () => {
    builds = [
      build({ id: 'queued', status: 'queued', phase: 'pending', pyramidVersion: null }),
      build({ id: 'running', status: 'running', phase: 'bake', pyramidVersion: null }),
      build({ id: 'stopping', status: 'running', phase: 'bake', cancelRequestedAt: '2026-10-15T09:00:00Z' }),
      build({ id: 'failed', status: 'failed', errorCode: 'terrain_build.bake_unavailable' }),
      build({ id: 'off', status: 'failed', errorCode: 'terrain_build.cancelled' }),
      build({ id: 'done' }),
    ];
    show();

    expect(screen.getByTestId('terrain-cancel-queued')).toBeInTheDocument();
    expect(screen.getByTestId('terrain-cancel-running')).toBeInTheDocument();
    // Already asked: the server would answer a second press as done, so there is nothing to offer.
    expect(screen.queryByTestId('terrain-cancel-stopping')).not.toBeInTheDocument();
    expect(screen.queryByTestId('terrain-cancel-done')).not.toBeInTheDocument();

    expect(screen.getByTestId('terrain-retry-failed')).toBeInTheDocument();
    expect(screen.getByTestId('terrain-retry-off')).toBeInTheDocument();
    for (const id of ['queued', 'running', 'stopping', 'done']) {
      expect(screen.queryByTestId(`terrain-retry-${id}`)).not.toBeInTheDocument();
    }
  });

  it('offers neither to somebody who may read builds but not start them', () => {
    builds = [
      build({ id: 'running', status: 'running', phase: 'bake' }),
      build({ id: 'failed', status: 'failed', errorCode: 'terrain_build.bake_failed' }),
    ];
    show({ canExecute: false, canDelete: true });

    expect(screen.queryByTestId('terrain-cancel-running')).not.toBeInTheDocument();
    expect(screen.queryByTestId('terrain-retry-failed')).not.toBeInTheDocument();
  });

  it('confirms before calling a build off, and says whether it stopped or is stopping', async () => {
    builds = [build({ id: 'running', status: 'running', phase: 'bake' })];
    const running = show();

    fireEvent.click(screen.getByTestId('terrain-cancel-running'));
    // Nothing is sent until the confirmation is answered.
    expect(cancelMutate).not.toHaveBeenCalled();
    expect(await screen.findByText(/obtained and prepared is kept/)).toBeInTheDocument();
    fireEvent.click(await confirmCallOff());
    expect(await screen.findByText(/will stop at the first point it can/)).toBeInTheDocument();
    expect(cancelMutate).toHaveBeenCalledWith('running');
    running.unmount();

    builds = [build({ id: 'queued', status: 'queued', phase: 'pending', pyramidVersion: null })];
    show();
    fireEvent.click(screen.getByTestId('terrain-cancel-queued'));
    fireEvent.click(await confirmCallOff());
    expect(await screen.findByText('The build was called off.')).toBeInTheDocument();
  });

  it('runs a stopped build again, and words the refusal while its bake is still stopping', async () => {
    retryMutate.mockImplementation((id: string) =>
      id === 'stopping'
        ? Promise.reject(new ApiError(409, 'terrain_build.bake_stopping'))
        : Promise.resolve({}),
    );

    builds = [build({ id: 'stopping', status: 'failed', errorCode: 'terrain_build.cancelled' })];
    const stopping = show();
    fireEvent.click(screen.getByTestId('terrain-retry-stopping'));
    expect(await screen.findByText(/has not yet said it stopped/)).toBeInTheDocument();
    stopping.unmount();

    builds = [build({ id: 'failed', status: 'failed', errorCode: 'terrain_build.bake_unavailable' })];
    show();
    fireEvent.click(screen.getByTestId('terrain-retry-failed'));
    expect(await screen.findByText(/queued again and will carry on/)).toBeInTheDocument();
    expect(retryMutate).toHaveBeenCalledWith('failed');
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
      message: 'The tile maker ended badly (code 1). It said: out of disk space.',
      errorCode: 'terrain_build.bake_failed',
      pyramidVersion: null,
      sizeBytes: 1024,
    });
    builds = [failed];
    detail = {
      build: failed,
      logTail: 'CRITICAL memory pressure\nstopping',
      sources: [
        { id: 1, kind: 'fetched', reference: 'copernicus', attribution: 'Copernicus', licence: null, inherited: false },
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
    expect(screen.getByTestId('terrain-build-failure')).toHaveTextContent('out of disk space');
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
    // and running the same build again carries it on from the bake.
    expect(notice).toHaveTextContent('still on disk under it');
    expect(notice).toHaveTextContent('press Run again on the stopped build');
    expect(screen.getByTestId('terrain-retry-b1')).toBeInTheDocument();
    expect(screen.queryByTestId('terrain-build-failure')).not.toBeInTheDocument();
  });

  it('says a build is being called off while it still runs, and shows a called-off one as no alarm', () => {
    const stopping = build({
      id: 'b1',
      status: 'running',
      phase: 'bake',
      progress: 45,
      cancelRequestedAt: '2026-10-15T09:00:00Z',
      pyramidVersion: null,
    });
    builds = [stopping];
    detail = { build: stopping, logTail: null, sources: [] };
    const running = show();
    expect(screen.getByTestId('terrain-build-cancelling')).toHaveTextContent('being cancelled');
    running.unmount();

    const off = build({
      id: 'b1',
      status: 'failed',
      phase: 'bake',
      progress: 45,
      errorCode: 'terrain_build.cancelled',
      message: 'Called off. What it had obtained and prepared is kept.',
      pyramidVersion: null,
    });
    builds = [off];
    detail = { build: off, logTail: null, sources: [] };
    show();
    expect(screen.getByTestId('terrain-build-cancelled')).toHaveTextContent('Called off.');
    expect(screen.queryByTestId('terrain-build-failure')).not.toBeInTheDocument();
    expect(screen.queryByTestId('terrain-build-cancelling')).not.toBeInTheDocument();
  });

  // The tile maker short of memory does not fail; it makes coarser terrain. The build that met
  // that is told what coarser means and the two lines that give the tile maker more memory.
  it('explains a bake that ran short of memory, with the setting and the command to apply it', () => {
    const degraded = build({
      id: 'b1',
      status: 'failed',
      phase: 'bake',
      progress: 45,
      errorCode: 'terrain_build.bake_degraded',
      message: 'The tile maker ran short of memory.',
      pyramidVersion: null,
    });
    builds = [degraded];
    detail = { build: degraded, logTail: null, sources: [] };
    show();

    const notice = screen.getByTestId('terrain-build-degraded');
    expect(notice).toHaveTextContent('coarser than asked for');
    expect(notice).toHaveTextContent('less detail');
    expect(notice).toHaveTextContent('SILEXGIS_TERRAIN_WORKER_MEMORY=16g');
    expect(notice).toHaveTextContent(
      'docker compose -f docker-compose.yml -f docker-compose.terrain-worker.yml up -d terrain-worker',
    );
    expect(screen.queryByTestId('terrain-build-failure')).not.toBeInTheDocument();
    expect(screen.getByTestId('terrain-retry-b1')).toBeInTheDocument();
  });

  it('says a bake reached the time limit, after how long against what limit, and how to allow longer', async () => {
    const timedOut = build({
      id: 'b1',
      status: 'failed',
      phase: 'bake',
      progress: 45,
      errorCode: 'terrain_build.bake_timed_out',
      message: 'Time limit reached: the bake ran 12 h (43217 s); the limit is 12 h (…=43200).',
      pyramidVersion: null,
      bakeTimeLimit: { elapsedSeconds: 46_800, limitSeconds: 43_200 },
    });
    builds = [timedOut];
    detail = { build: timedOut, logTail: null, sources: [] };
    const shown = show();

    const notice = screen.getByTestId('terrain-build-timed-out');
    expect(notice).toHaveTextContent('Time limit reached after 13 h (limit 12 h)');
    expect(notice).toHaveTextContent('SILEXGIS__Terrain__BakeTimeoutSeconds=86400');
    expect(screen.queryByTestId('terrain-build-failure')).not.toBeInTheDocument();

    // The value is in seconds; the "?" beside it says what the usual lengths come to.
    fireEvent.click(screen.getByTestId('terrain-seconds-help-button'));
    expect(await screen.findByTestId('terrain-seconds-help')).toHaveTextContent('1 day = 86400');
    shown.unmount();

    // Without the numbers the heading still names the limit rather than falling back to "failed".
    const bare = { ...timedOut, bakeTimeLimit: null };
    builds = [bare];
    detail = { build: bare, logTail: null, sources: [] };
    show();
    expect(screen.getByTestId('terrain-build-timed-out')).toHaveTextContent('Time limit reached');
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
