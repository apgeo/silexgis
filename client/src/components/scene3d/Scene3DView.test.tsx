// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

// The engine library is replaced by the same double the scene module's own suite uses; the test
// runner has no graphics context to give it.
vi.mock('cesium', () => import('../../scene3d/cesiumTestDouble.ts'));
vi.mock('../../api/hooks.ts', () => ({ useMapLayers: () => ({ data: mapLayers }) }));

const engine = await import('../../scene3d/cesiumTestDouble.ts');
const { default: Scene3DView } = await import('./Scene3DView.tsx');

let mapLayers: unknown[] | undefined;

/** Makes the browser look like one that can run the scene, or one that cannot. */
function withWebGl2(available: boolean) {
  if (!available) {
    // jsdom already has no WebGL 2 constructor, which is exactly the browser being simulated.
    return;
  }
  class FakeWebGl2Context {
    getExtension() {
      return { loseContext() {} };
    }
  }
  vi.stubGlobal('WebGL2RenderingContext', FakeWebGl2Context);
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
    new FakeWebGl2Context() as unknown as CanvasRenderingContext2D,
  );
}

function renderView() {
  return render(
    <MemoryRouter>
      <Scene3DView />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  engine.engineState.reset();
  mapLayers = undefined;
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Scene3DView', () => {
  it('explains itself instead of showing a dead canvas when the browser cannot run it', () => {
    withWebGl2(false);
    renderView();

    expect(screen.getByTestId('scene3d-unsupported')).toBeInTheDocument();
    expect(screen.getByText('This browser cannot show the 3D view')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the 2D map' })).toBeInTheDocument();
    // Nothing was mounted, so nothing downloaded the engine either.
    expect(screen.queryByTestId('scene3d-container')).not.toBeInTheDocument();
    expect(engine.engineState.widgets).toHaveLength(0);
  });

  it('builds the scene inside its own element when the browser can run it', async () => {
    withWebGl2(true);
    renderView();

    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    expect(engine.engineState.widgets[0].container).toBe(screen.getByTestId('scene3d-container'));
    await waitFor(() => expect(screen.queryByTestId('scene3d-loading')).not.toBeInTheDocument());
  });

  it('tears the scene down when it goes away', async () => {
    withWebGl2(true);
    const view = renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    view.unmount();

    await waitFor(() => expect(engine.engineState.widgets[0].isDestroyed()).toBe(true));
  });

  it('drapes the configured basemaps over the globe and shows the default one', async () => {
    withWebGl2(true);
    mapLayers = [
      {
        id: 1,
        name: 'OpenStreetMap',
        layerKind: 'xyz',
        urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        attribution: '© OpenStreetMap contributors',
        isBase: true,
        isDefault: true,
        sortOrder: 0,
        options: null,
      },
      {
        id: 2,
        name: 'OpenTopoMap',
        layerKind: 'xyz',
        urlTemplate: 'https://tile.opentopomap.org/{z}/{x}/{y}.png',
        attribution: null,
        isBase: true,
        isDefault: false,
        sortOrder: 1,
        options: null,
      },
    ];
    renderView();

    await waitFor(() => expect(engine.engineState.providers).toHaveLength(2));
    const layers = engine.engineState.widgets[0].scene.imageryLayers.layers;
    expect(layers.map((layer) => layer.show)).toEqual([true, false]);
    expect(engine.engineState.providers[0].url).toBe(
      'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
    );
  });

  it('reports a failure to start in the application\'s own words', async () => {
    withWebGl2(true);
    // A second scene on a different element is refused, which is the cheapest way to make the
    // start path fail for real rather than by stubbing the module.
    const { acquireScene3D } = await import('../../scene3d/scene3dContext.ts');
    const other = acquireScene3D(document.createElement('div'));

    renderView();

    expect(await screen.findByText('The 3D view could not be started')).toBeInTheDocument();
    expect(await screen.findByText(/different container/)).toBeInTheDocument();
    other.release();
  });
});
