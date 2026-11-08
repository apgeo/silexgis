// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import type { Scene3DLocalScan, Scene3DScanPoint } from '../../scene3d/scene3dEngine.ts';
import ScanControlPointsPlacer from './ScanControlPointsPlacer.tsx';

/** The local scene, reduced to what placing uses: a load to land, and clicks that take points. */
function fakeScene() {
  let resolveLoad: (() => void) | undefined;
  const scene = {
    picking: [] as boolean[],
    marks: [] as (readonly Scene3DScanPoint[])[],
    pickListeners: [] as ((point: Scene3DScanPoint) => void)[],
    isDestroyed: () => false,
    destroy: () => {},
    requestRender: () => {},
    load: () =>
      new Promise<void>((resolve) => {
        resolveLoad = resolve;
      }),
    status: () => undefined,
    setStyle: () => {},
    setDetail: () => {},
    viewAll: () => {},
    setMeasuring: () => {},
    measurement: () => undefined,
    subscribeMeasurement: () => () => {},
    subscribeRenderError: () => () => {},
    setPicking: (on: boolean) => {
      scene.picking.push(on);
    },
    setMarks: (points: readonly Scene3DScanPoint[]) => {
      scene.marks.push(points);
    },
    subscribePick: (listener: (point: Scene3DScanPoint) => void) => {
      scene.pickListeners.push(listener);
      return () => {};
    },
    click: (point: Scene3DScanPoint) => scene.pickListeners.forEach((listener) => listener(point)),
    land: () => resolveLoad?.(),
  };
  return scene;
}

let built: ReturnType<typeof fakeScene>[] = [];
vi.mock('../../scene3d/scene3dContext.ts', () => ({
  createLocalScan3D: () => {
    const scene = fakeScene();
    built.push(scene);
    return scene as unknown as Scene3DLocalScan;
  },
}));

const placeMutate = vi.fn();
let models: Partial<SurveyModelInfo>[] = [];
vi.mock('../../api/hooks.ts', () => ({
  useSurveyModels: () => ({ data: models }),
  usePlaceSurveyModelByControlPoints: () => ({ mutateAsync: placeMutate, isPending: false }),
  // The station field offers names as they are typed; nothing is offered here, so it is a box.
  useSurveyModelStationSearch: () => ({ data: undefined }),
}));

const TILES = '/api/v1/survey-models/scan/tiles/tileset.json?token=t';

function scan(overrides: Partial<SurveyModelInfo> = {}): SurveyModelInfo & { tilesetUrl: string } {
  return {
    id: 'scan',
    caveId: 'c1',
    name: 'Hall scan',
    format: 'laz',
    placementControlPoints: null,
    ...overrides,
    tilesetUrl: TILES,
  } as SurveyModelInfo & { tilesetUrl: string };
}

beforeEach(() => {
  built = [];
  placeMutate.mockReset();
  models = [
    { id: 'old', format: 'lox', isCurrent: false },
    { id: 'plot', format: 'lox', isCurrent: true },
  ];
});
afterEach(cleanup);

async function openPlacer(model = scan()) {
  render(
    <App>
      <ScanControlPointsPlacer model={model} caveId="c1" open onClose={() => {}} />
    </App>,
  );
  await waitFor(() => expect(built).toHaveLength(1));
  await act(async () => {
    built[0].land();
  });
  return built[0];
}

function station(index: number, name: string) {
  fireEvent.change(screen.getByTestId(`scan-control-station-${index}`), { target: { value: name } });
}

describe('placing a scan by control points', () => {
  it('pairs points picked on the cloud with stations, sends them, and shows the residuals with the far one flagged', async () => {
    const scene = await openPlacer();
    const save = screen.getByTestId('scan-control-save');
    expect(save).toBeDisabled();

    // A pick button arms the scene for one click; the click fills that pair and disarms it.
    fireEvent.click(screen.getByTestId('scan-control-pick-0'));
    expect(scene.picking.at(-1)).toBe(true);
    act(() => scene.click({ x: 1, y: 2, z: 0.5 }));
    expect(scene.picking.at(-1)).toBe(false);
    expect(screen.getByTestId('scan-control-point-0')).toHaveTextContent('x 1.00 · y 2.00 · z 0.50 m');
    expect(scene.marks.at(-1)).toEqual([{ x: 1, y: 2, z: 0.5 }]);

    fireEvent.click(screen.getByTestId('scan-control-pick-1'));
    act(() => scene.click({ x: 11, y: 2, z: 0.4 }));
    station(0, '1.0');
    expect(save).toBeDisabled(); // a pair with no station is not a pair yet
    station(1, '1.4');
    expect(save).toBeEnabled();

    placeMutate.mockResolvedValue({
      ...scan(),
      placementSource: 'controlPoints',
      placementControlPoints: {
        linePlotId: 'plot',
        rmsM: 0.42,
        warnM: 0.5,
        tiltFitted: false,
        pairs: [
          { scanX: 1, scanY: 2, scanZ: 0.5, station: '1.0', eastM: 0.1, northM: 0, upM: 0.05, horizontalM: 0.1, distanceM: 0.11, flagged: false },
          { scanX: 11, scanY: 2, scanZ: 0.4, station: '1.4', eastM: 0.6, northM: 0, upM: 0, horizontalM: 0.6, distanceM: 0.6, flagged: true },
        ],
      },
    });
    fireEvent.click(save);

    await waitFor(() => expect(placeMutate).toHaveBeenCalledTimes(1));
    expect(placeMutate.mock.calls[0][0]).toEqual({
      id: 'scan',
      caveId: 'c1',
      request: {
        pairs: [
          { scanX: 1, scanY: 2, scanZ: 0.5, station: '1.0' },
          { scanX: 11, scanY: 2, scanZ: 0.4, station: '1.4' },
        ],
        fitTilt: false,
      },
    });
    expect(await screen.findByTestId('scan-control-rms')).toHaveTextContent('0.42 m');
    expect(screen.getAllByTestId('scan-control-flagged')).toHaveLength(1);
    expect(screen.getByTestId('scan-control-flagged')).toHaveTextContent('0.60');
  });

  it('asks for a third pair before a tilt can be fitted', async () => {
    const scene = await openPlacer();
    for (const [index, point] of [
      [0, { x: 0, y: 0, z: 0 }],
      [1, { x: 10, y: 0, z: 0 }],
    ] as const) {
      fireEvent.click(screen.getByTestId(`scan-control-pick-${index}`));
      act(() => scene.click(point));
      station(index, `s${index}`);
    }
    expect(screen.getByTestId('scan-control-save')).toBeEnabled();
    fireEvent.click(screen.getByTestId('scan-control-tilt'));
    expect(screen.getByTestId('scan-control-save')).toBeDisabled();

    fireEvent.click(screen.getByTestId('scan-control-add'));
    fireEvent.click(screen.getByTestId('scan-control-pick-2'));
    act(() => scene.click({ x: 0, y: 10, z: 0 }));
    station(2, 's2');
    expect(screen.getByTestId('scan-control-save')).toBeEnabled();
  });

  it('opens a scan placed this way before on its pairs and residuals, so one pair can be changed', async () => {
    await openPlacer(
      scan({
        placementSource: 'controlPoints',
        placementControlPoints: {
          linePlotId: 'plot',
          rmsM: 0.05,
          warnM: 0.5,
          tiltFitted: false,
          pairs: [
            { scanX: 1, scanY: 2, scanZ: 3, station: 'A1', eastM: 0, northM: 0, upM: 0, horizontalM: 0, distanceM: 0.05, flagged: false },
            { scanX: 4, scanY: 5, scanZ: 6, station: 'A2', eastM: 0, northM: 0, upM: 0, horizontalM: 0, distanceM: 0.05, flagged: false },
          ],
        },
      }),
    );
    expect(screen.getByTestId('scan-control-station-1')).toHaveValue('A2');
    expect(screen.getByTestId('scan-control-point-0')).toHaveTextContent('x 1.00 · y 2.00 · z 3.00 m');
    expect(screen.getByTestId('scan-control-rms')).toHaveTextContent('0.05 m');
    expect(built[0].marks.at(-1)).toEqual([
      { x: 1, y: 2, z: 3 },
      { x: 4, y: 5, z: 6 },
    ]);
  });

  it('says there is nothing to place by when the cave has no current line plot', async () => {
    models = [{ id: 'old', format: 'lox', isCurrent: false }];
    await openPlacer();
    expect(screen.getByText(/no current line plot/)).toBeInTheDocument();
    expect(screen.getByTestId('scan-control-save')).toBeDisabled();
  });
});
