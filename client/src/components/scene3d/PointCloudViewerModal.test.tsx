// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { Scene3DLocalScan } from '../../scene3d/scene3dEngine.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { defaultColoring } from '../../scene3d/pointCloudColoring.ts';
import PointCloudViewerModal from './PointCloudViewerModal.tsx';

/**
 * A stand-in for the local scene: records what it was asked, lands the load when told. The
 * engine module is replaced wholesale, so nothing here needs a graphics context.
 */
function fakeScene() {
  let resolveLoad: (() => void) | undefined;
  let rejectLoad: ((error: unknown) => void) | undefined;
  const scene = {
    loads: [] as unknown[],
    styles: [] as unknown[],
    details: [] as unknown[],
    destroyed: false,
    viewedAll: 0,
    isDestroyed: () => scene.destroyed,
    destroy: () => {
      scene.destroyed = true;
    },
    requestRender: () => {},
    load: (options: unknown) => {
      scene.loads.push(options);
      return new Promise<void>((resolve, reject) => {
        resolveLoad = resolve;
        rejectLoad = reject;
      });
    },
    status: () => undefined,
    setStyle: (style: unknown) => {
      scene.styles.push(style);
    },
    setDetail: (detail: unknown) => {
      scene.details.push(detail);
    },
    viewAll: () => {
      scene.viewedAll += 1;
    },
    measuring: [] as boolean[],
    measurementListeners: [] as ((measurement: unknown) => void)[],
    setMeasuring: (on: boolean) => {
      scene.measuring.push(on);
    },
    measurement: () => undefined,
    subscribeMeasurement: (listener: (measurement: unknown) => void) => {
      scene.measurementListeners.push(listener);
      return () => {};
    },
    /** How a test stands in for two clicks on the cloud. */
    measure: (measurement: unknown) => {
      for (const listener of scene.measurementListeners) {
        listener(measurement);
      }
    },
    subscribeRenderError: () => () => {},
    land: () => resolveLoad?.(),
    refuse: (error: unknown) => rejectLoad?.(error),
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

const URL = '/api/v1/survey-models/m1/tiles/tileset.json?token=t';

beforeEach(() => {
  built = [];
  useUiPrefsStore.getState().setPointDetail(undefined);
});

afterEach(cleanup);

describe('the colouring a scan opens with', () => {
  it('is what the scanner recorded when it recorded anything, else height', () => {
    expect(defaultColoring(true, true)).toBe('rgb');
    expect(defaultColoring(false, true)).toBe('intensity');
    expect(defaultColoring(false, false)).toBe('height');
  });
});

describe('the scan viewer', () => {
  it('builds the local scene, loads the tileset with this browser’s detail, and destroys it on close', async () => {
    useUiPrefsStore.getState().setPointDetail('high');
    const onClose = vi.fn();
    const { rerender } = render(
      <PointCloudViewerModal open tilesetUrl={URL} name="Hall" hasRgb={false} hasIntensity onClose={onClose} />,
    );

    await waitFor(() => expect(built).toHaveLength(1));
    const scene = built[0];
    await waitFor(() => expect(scene.loads).toHaveLength(1));
    expect(scene.loads[0]).toEqual({
      url: URL,
      detail: { maximumScreenSpaceError: 8, cacheBytes: 1024 * 1024 * 1024 },
      style: { colorBy: 'intensity' },
    });
    expect(screen.getByTestId('point-cloud-surface')).toHaveAttribute('data-status', 'loading');

    await act(async () => {
      scene.land();
    });
    expect(screen.getByTestId('point-cloud-surface')).toHaveAttribute('data-status', 'loaded');

    rerender(<PointCloudViewerModal open={false} tilesetUrl={URL} name="Hall" hasRgb={false} hasIntensity onClose={onClose} />);
    // The scene holds a drawing context from the browser's small budget; closing gives it back.
    await waitFor(() => expect(scene.destroyed).toBe(true));
  });

  it('offers only the colourings the tiles carry, and restyles the scene when one is chosen', async () => {
    render(<PointCloudViewerModal open tilesetUrl={URL} name="Hall" hasRgb={false} hasIntensity={false} onClose={() => {}} />);
    await waitFor(() => expect(built).toHaveLength(1));
    await act(async () => {
      built[0].land();
    });

    expect(screen.queryByText('Recorded colour')).toBeNull();
    expect(screen.queryByText('Intensity')).toBeNull();
    expect(screen.getByText('Height')).toBeInTheDocument();

    cleanup();
    render(<PointCloudViewerModal open tilesetUrl={URL} name="Hall" hasRgb hasIntensity onClose={() => {}} />);
    await waitFor(() => expect(built).toHaveLength(2));
    fireEvent.click(screen.getByText('Height'));
    expect(built[1].styles).toEqual([{ colorBy: 'height' }]);
    fireEvent.click(screen.getByTestId('point-cloud-view-all'));
    expect(built[1].viewedAll).toBe(1);
  });

  it('measures once the scan is drawn: turns the scene’s measure on, says where it stands, and reads the metres', async () => {
    render(<PointCloudViewerModal open tilesetUrl={URL} name="Hall" hasRgb={false} hasIntensity={false} onClose={() => {}} />);
    await waitFor(() => expect(built).toHaveLength(1));
    // Nothing to click on until the cloud is there.
    expect(screen.getByTestId('point-cloud-measure')).toBeDisabled();
    await act(async () => {
      built[0].land();
    });

    fireEvent.click(screen.getByTestId('point-cloud-measure'));
    expect(built[0].measuring).toEqual([true]);
    expect(screen.getByTestId('point-cloud-measure-readout')).toHaveTextContent('Click two points on the scan.');

    act(() => built[0].measure({ points: 1 }));
    expect(screen.getByTestId('point-cloud-measure-readout')).toHaveTextContent('One point taken');
    act(() => built[0].measure({ points: 2, distanceM: 2.345 }));
    expect(screen.getByTestId('point-cloud-measure-readout')).toHaveTextContent('2.35 m between the two points');

    fireEvent.click(screen.getByTestId('point-cloud-measure'));
    expect(built[0].measuring).toEqual([true, false]);
    expect(screen.queryByTestId('point-cloud-measure-readout')).toBeNull();
  });

  it('says so when the tileset cannot be read', async () => {
    render(<PointCloudViewerModal open tilesetUrl={URL} name="Hall" hasRgb={false} hasIntensity={false} onClose={() => {}} />);
    await waitFor(() => expect(built).toHaveLength(1));
    await act(async () => {
      built[0].refuse(new Error('403'));
    });
    expect(screen.getByTestId('point-cloud-failed')).toHaveTextContent('The scan could not be read.');
  });

  it('hands a changed detail setting to the open scene without reloading it', async () => {
    render(<PointCloudViewerModal open tilesetUrl={URL} name="Hall" hasRgb={false} hasIntensity={false} onClose={() => {}} />);
    await waitFor(() => expect(built).toHaveLength(1));
    await act(async () => {
      built[0].land();
    });

    act(() => {
      useUiPrefsStore.getState().setPointDetail('low');
    });
    expect(built[0].details.at(-1)).toEqual({ maximumScreenSpaceError: 32, cacheBytes: 128 * 1024 * 1024 });
    expect(built[0].loads).toHaveLength(1);
  });
});
