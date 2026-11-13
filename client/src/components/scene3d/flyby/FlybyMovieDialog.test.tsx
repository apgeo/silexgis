// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import { DEFAULT_FLYBY_MOVIE_SETTINGS } from '../../../flyby/flybyMovie.ts';
import type { MovieEncoder, MovieEncoderOptions, MovieFormatSupport } from '../../../movie/encode/movieEncoder.ts';
import type { CaveInView3D } from '../../../scene3d/centerlines3d.ts';
import type { Scene3DCameraState, Scene3DCore } from '../../../scene3d/scene3dEngine.ts';
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import Scene3DFlyby from './Scene3DFlyby.tsx';
// Loaded beforehand, so the panel's own chunk is already in hand when the tool asks for it.
import './FlybyMovieDialog.tsx';
import type { FlybyScene } from './useFlyby.ts';

// The movie part of the flyby tool, against a scene that is a plain object and an encoder that
// writes down what it is given. What the real scene and the real encoders make of it is the browser
// spec's to say; here it is the panel's manners: the box the scene is put in, the file that comes
// out and what it is called, and that an export asked to stop leaves the view as it was.

vi.mock('../../../api/hooks.ts', () => ({
  useCaveNames: (ids: readonly string[]) => new Map(ids.map((id) => [id, `Cave ${id.toUpperCase()}`])),
  caveQuery: (id: string) => ({
    queryKey: ['cave', id],
    queryFn: async () => ({ geom: { type: 'Point', coordinates: [25, 45.5] }, depth: 120, surveyedLength: 800 }),
  }),
  // The lists the flyby's own panel names things from, which is on screen until the movie panel opens.
  useMapLayers: () => ({ data: [] }),
  useRasterMaps: () => ({ data: { items: [] } }),
  useGeofiles: () => ({ data: { items: [] } }),
  useSavedFilters: () => ({ data: [] }),
}));
vi.mock('../../../viewlinks/geoTargets.ts', () => ({
  APPROXIMATE_SPAN_DEGREES: 0.05,
  geometryFor: async () => null,
}));
const saveBlob = vi.fn();
vi.mock('../../../api/download.ts', () => ({ saveBlob: (...args: unknown[]) => saveBlob(...args) }));

/** What the encoder was given, and a way to hold it on a frame. */
const encoding = {
  opened: [] as MovieEncoderOptions[],
  frames: 0,
  closed: 0,
  /** While set, adding a frame waits until the test lets it go. */
  hold: null as Promise<void> | null,
};
const formats: MovieFormatSupport[] = [
  { format: 'gif', supported: true, codec: null },
  { format: 'webm', supported: true, codec: 'vp09.00.10.08' },
  { format: 'mp4', supported: true, codec: 'avc1.42001f' },
];
vi.mock('../../../movie/encode/movieEncoder.ts', async (original) => ({
  ...(await original<typeof import('../../../movie/encode/movieEncoder.ts')>()),
  probeMovieFormats: async () => formats,
  openMovieEncoder: async (format: MovieEncoder['format'], options: MovieEncoderOptions): Promise<MovieEncoder> => {
    encoding.opened.push(options);
    return {
      format,
      samplesWanted: 0,
      prime: async () => undefined,
      addFrame: async () => {
        encoding.frames += 1;
        if (encoding.hold) {
          await encoding.hold;
        }
      },
      finish: async () => new Blob(['movie'], { type: `video/${format}` }),
      close: () => {
        encoding.closed += 1;
      },
    };
  },
}));

const HOME: Scene3DCameraState = { longitude: 24.9, latitude: 45.4, height: 3000, heading: 0, pitch: -40, roll: 0 };
const drawnCave: CaveInView3D = { caveId: 'a', label: 'Line name', bounds: [25, 45.5, 25.004, 45.504], top: 990, bottom: 800 };

/** Where each element of the view stands, by what it is: there is no layout here to ask. */
function layOut(element: Element): DOMRect {
  const rect = (left: number, top: number, width: number, height: number) =>
    ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top }) as DOMRect;
  if (element.classList.contains('scene3d-wrap')) return rect(0, 0, 1200, 700);
  if (element.classList.contains('scene3d-flyby-movie')) return rect(8, 8, 360, 612);
  if (element.classList.contains('scene3d-flyby-transport')) return rect(320, 620, 560, 48);
  return rect(0, 0, 0, 0);
}

function fakeScene() {
  let camera = HOME;
  const wrap = document.createElement('div');
  wrap.className = 'scene3d-wrap';
  const surface = document.createElement('div');
  Object.defineProperty(surface, 'clientWidth', { get: () => Number.parseInt(wrap.style.getPropertyValue('--flyby-frame-width')) || 0 });
  Object.defineProperty(surface, 'clientHeight', { get: () => Number.parseInt(wrap.style.getPropertyValue('--flyby-frame-height')) || 0 });
  wrap.appendChild(surface);
  document.body.appendChild(wrap);
  const capture = { open: null as { width: number; height: number } | null, frames: 0 };
  const canvas = document.createElement('canvas');
  const engine = {
    getCamera: () => camera,
    setCamera: (next: Scene3DCameraState) => {
      camera = next;
    },
    getCameraTarget: () => ({ longitude: 25, latitude: 45.5, height: 900 }),
    getProjection: () => 'perspective',
    getOrthoHalfWidth: () => undefined,
    groundHeight: () => 1000,
    requestRender: vi.fn(),
    isSettled: () => true,
    isDestroyed: () => false,
    beginCapture: (size: { width: number; height: number }) => {
      capture.open = size;
      canvas.width = size.width;
      canvas.height = size.height;
    },
    captureFrame: () => {
      capture.frames += 1;
      return canvas;
    },
    endCapture: () => {
      capture.open = null;
    },
    getVisibleCredits: () => ['Tiles © Example'],
  } as unknown as Scene3DCore;
  const scene: FlybyScene & { camera(): Scene3DCameraState; wrapElement: HTMLElement; capture: typeof capture } = {
    engine: () => engine,
    cavesInView: () => [drawnCave],
    refreshData: () => undefined,
    dataLoading: () => false,
    wrap: () => wrap,
    surface: () => surface,
    camera: () => camera,
    wrapElement: wrap,
    capture,
  };
  return scene;
}

function renderFlyby(scene = fakeScene()) {
  const onActiveChange = vi.fn();
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <App>
        <Scene3DFlyby scene={scene} selection={{ kind: 'cave', caveId: 'a' }} onActiveChange={onActiveChange} onClose={vi.fn()} />
      </App>
    </QueryClientProvider>,
  );
  return { scene, onActiveChange };
}

async function pass(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

/** Opens the movie panel, whose code arrives in a chunk of its own. */
async function openMovie() {
  fireEvent.click(screen.getByTestId('flyby-movie'));
  for (let tries = 0; tries < 50 && !screen.queryByTestId('flyby-movie-dialog'); tries += 1) {
    await pass(20);
  }
  expect(screen.getByTestId('flyby-movie-dialog')).toBeInTheDocument();
  // The formats this browser writes are asked once the panel is up.
  await pass(20);
}

/** The browser's own recorder, where a test gives the page one: what was made of it, and the rates its canvases were filmed at. */
const browserRecorder = {
  made: [] as { state: string; options: { mimeType: string } }[],
  streams: [] as number[],
};

/** Gives the page a recorder that writes `types`, and canvases that can be filmed. */
function stubBrowserRecorder(types: string[]) {
  class FakeMediaRecorder {
    static isTypeSupported = (type: string) => types.includes(type);
    state = 'inactive';
    mimeType: string;
    options: { mimeType: string };
    ondataavailable: ((event: { data: Blob }) => void) | null = null;
    onerror: ((event: Event) => void) | null = null;
    onstop: ((event: Event) => void) | null = null;
    constructor(_stream: unknown, options: { mimeType: string }) {
      this.options = options;
      this.mimeType = options.mimeType;
      browserRecorder.made.push(this);
    }
    start() {
      this.state = 'recording';
    }
    stop() {
      this.state = 'inactive';
      this.ondataavailable?.({ data: new Blob(['live movie']) });
      this.onstop?.(new Event('stop'));
    }
  }
  vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
  Object.defineProperty(HTMLCanvasElement.prototype, 'captureStream', {
    configurable: true,
    value(fps: number) {
      browserRecorder.streams.push(fps);
      return { getTracks: () => [{ stop: () => undefined }] };
    },
  });
}

const frameStyle = (wrap: HTMLElement) =>
  ['left', 'top', 'width', 'height'].map((side) => wrap.style.getPropertyValue(`--flyby-frame-${side}`));

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame', 'performance'] });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    return layOut(this);
  });
  // The shared stand-in for a 2D canvas writes no text; the credits on every frame are written.
  const getContext = HTMLCanvasElement.prototype.getContext;
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (this: HTMLCanvasElement, ...args) {
    const context = (getContext as (...given: unknown[]) => CanvasRenderingContext2D | null).apply(this, args);
    return context && Object.assign(context, { fillText: () => undefined, textBaseline: 'alphabetic' });
  } as typeof getContext);
  useFlybyStore.setState({ library: [], loadRoute: false, movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'webm' } });
  saveBlob.mockClear();
  encoding.opened = [];
  encoding.frames = 0;
  encoding.closed = 0;
  encoding.hold = null;
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  delete (HTMLCanvasElement.prototype as { captureStream?: unknown }).captureStream;
  browserRecorder.made = [];
  browserRecorder.streams = [];
  document.body.replaceChildren();
});

describe('making a movie of a flyby', () => {
  it('boxes the scene to the movie\'s frame beside the panel, and to a vertical one when that is chosen', async () => {
    const { scene } = renderFlyby();
    expect(scene.wrapElement).not.toHaveClass('scene3d-wrap-framed');
    await openMovie();
    // Beside the panel (which ends at 368) and above the bar (which starts at 620), centred in
    // what is left: 51 boxes of 16 by 9.
    expect(scene.wrapElement).toHaveClass('scene3d-wrap-framed');
    expect(frameStyle(scene.wrapElement)).toEqual(['380px', '77px', '816px', '459px']);

    fireEvent.click(screen.getByTestId('flyby-movie-preset-vertical'));
    await pass(20);
    expect(useFlybyStore.getState().movie).toMatchObject({ size: '1080x1920', format: 'mp4' });
    // Nine by sixteen, as tall as the room above the bar allows and centred across it.
    expect(frameStyle(scene.wrapElement)).toEqual(['617px', '2px', '342px', '608px']);

    fireEvent.click(screen.getByTestId('flyby-movie-close'));
    expect(screen.queryByTestId('flyby-movie-dialog')).not.toBeInTheDocument();
    expect(scene.wrapElement).not.toHaveClass('scene3d-wrap-framed');
    expect(screen.getByTestId('flyby-panel')).toBeInTheDocument();
  });

  it('always says that the movie shows where the caves are', async () => {
    renderFlyby();
    await openMovie();
    expect(screen.getByTestId('flyby-movie-privacy')).toHaveTextContent(
      'The movie shows where these caves are. Share it only with people who may see that.',
    );
  });

  it('writes the flyby to a file named after it, a frame for every moment, and hands the view back', async () => {
    useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'webm', draft: true } });
    const { scene, onActiveChange } = renderFlyby();
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'Round the cave' } });
    await openMovie();
    // Thirty seconds of flight, as a draft: eight frames a second.
    expect(screen.getByTestId('flyby-movie-summary')).toHaveTextContent('240 frames of 640 × 360 · 0:30');
    expect(screen.getByTestId('flyby-movie-file')).toHaveTextContent(/silexgis-round-the-cave-\d{4}-\d{2}-\d{2}\.webm/);

    fireEvent.click(screen.getByTestId('flyby-movie-export'));
    for (let tries = 0; tries < 100 && saveBlob.mock.calls.length === 0; tries += 1) {
      await pass(50);
    }
    expect(saveBlob).toHaveBeenCalledTimes(1);
    expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-round-the-cave-\d{4}-\d{2}-\d{2}\.webm$/);
    expect(encoding.opened[0]).toMatchObject({ width: 640, height: 360, fps: 8 });
    expect(encoding.frames).toBe(240);
    // Taken off the element as the screen shows it: the box is 816 wide, larger than a draft's frame.
    expect(scene.capture.frames).toBe(240);
    expect(scene.capture.open).toBeNull();
    expect(scene.camera()).toEqual(HOME);
    expect(onActiveChange).toHaveBeenLastCalledWith(false);
    // A draft is taken quickly: there is no report of frames that waited for the scene.
    expect(screen.queryByTestId('flyby-movie-report')).not.toBeInTheDocument();
  });

  it('reports after a careful export how many frames were taken before the scene had loaded', async () => {
    useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'gif', size: '320x180', fps: 10, level: 'fine' } });
    renderFlyby();
    await openMovie();
    fireEvent.click(screen.getByTestId('flyby-movie-export'));
    for (let tries = 0; tries < 400 && saveBlob.mock.calls.length === 0; tries += 1) {
      await pass(500);
    }
    expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-flyby-\d{4}-\d{2}-\d{2}\.gif$/);
    expect(screen.getByTestId('flyby-movie-report')).toHaveTextContent('300 frames, made in');
    expect(screen.getByTestId('flyby-movie-report')).toHaveTextContent('Every frame was taken with the scene fully loaded.');
  });

  it('asks before a close throws an export away, keeps going when told to, and leaves the view as it was when stopped', async () => {
    let release!: () => void;
    encoding.hold = new Promise((resolve) => {
      release = resolve;
    });
    useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'webm', draft: true } });
    const { scene, onActiveChange } = renderFlyby();
    await openMovie();
    fireEvent.click(screen.getByTestId('flyby-movie-export'));
    await pass(100);
    expect(screen.getByTestId('flyby-movie-progress')).toBeInTheDocument();
    // The view keeps the flat map and the address out of it while the recorder has the camera.
    expect(onActiveChange).toHaveBeenLastCalledWith(true);
    // The bar is the recorder's while it runs.
    expect(screen.getByTestId('flyby-transport').parentElement).toHaveAttribute('inert');

    fireEvent.click(screen.getByTestId('flyby-movie-close'));
    await pass(50);
    expect(screen.getAllByText('Stop making the movie?').length).toBeGreaterThan(0);
    fireEvent.click(screen.getByRole('button', { name: 'Keep going' }));
    await pass(300);
    expect(screen.getByTestId('flyby-movie-dialog')).toBeInTheDocument();
    expect(encoding.closed).toBe(0);

    fireEvent.keyDown(screen.getByTestId('flyby-movie-dialog'), { key: 'Escape' });
    await pass(50);
    // The question asked the first time may still be fading out; the one just asked is the last.
    fireEvent.click(screen.getAllByRole('button', { name: 'Stop and close' }).at(-1)!);
    release();
    await pass(300);
    expect(screen.queryByTestId('flyby-movie-dialog')).not.toBeInTheDocument();
    expect(encoding.closed).toBe(1);
    expect(saveBlob).not.toHaveBeenCalled();
    expect(scene.capture.open).toBeNull();
    expect(scene.camera()).toEqual(HOME);
    expect(onActiveChange).toHaveBeenLastCalledWith(false);
  });

  it('offers no live recording in a browser without a recorder of its own', async () => {
    renderFlyby();
    await openMovie();
    expect(screen.getByTestId('flyby-movie-level-fine')).toBeInTheDocument();
    expect(screen.queryByTestId('flyby-movie-level-live')).not.toBeInTheDocument();
  });

  it('turns a live recording remembered from another browser into a frame-exact one where there is no recorder', async () => {
    useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'webm', level: 'live' } });
    renderFlyby();
    await openMovie();
    expect(useFlybyStore.getState().movie.level).toBe('fine');
  });

  describe('live, where the browser has a recorder', () => {
    beforeEach(() => {
      stubBrowserRecorder(['video/webm;codecs=vp9', 'video/webm;codecs=vp8']);
    });

    it('is offered as a fourth way, says plainly what it costs, and turns a GIF into what the recorder writes', async () => {
      useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'gif', size: '480x270', fps: 10 } });
      renderFlyby();
      await openMovie();
      fireEvent.click(screen.getByTestId('flyby-movie-level-live'));
      await pass(20);
      expect(useFlybyStore.getState().movie).toMatchObject({ level: 'live', format: 'webm' });
      expect(screen.getByText(/the browser's own recorder films it/)).toHaveTextContent(
        "The flyby plays in real time and the browser's own recorder films it: the file holds what the screen showed, at the speed this computer drew it. On a slow computer the movie stutters, and in some browsers the file has no length and cannot be wound on.",
      );
      // The rate is a ceiling now, not a promise.
      expect(screen.getByTestId('flyby-movie-summary')).toHaveTextContent(/^Up to \d+ frames of 480 × 270/);
      // This recorder writes no MP4, so MP4 cannot be chosen; and a GIF chosen again is refused.
      expect(screen.getByTestId('flyby-movie-format-mp4')).toBeDisabled();
      act(() => {
        useFlybyStore.setState({ movie: { ...useFlybyStore.getState().movie, format: 'gif' } });
      });
      expect(screen.getByTestId('flyby-movie-live-format')).toHaveTextContent('This browser records live only as WebM');
      expect(screen.getByTestId('flyby-movie-export')).toBeDisabled();
    });

    it('records the flyby as it plays, saves the file under the same name, says how fast it drew, and hands the view back', async () => {
      useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'webm', level: 'live', size: '640x360', fps: 30 } });
      const { scene, onActiveChange } = renderFlyby();
      fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'Round the cave' } });
      await openMovie();
      fireEvent.click(screen.getByTestId('flyby-movie-export'));
      await pass(1000);
      expect(onActiveChange).toHaveBeenLastCalledWith(true);
      expect(screen.getByTestId('flyby-movie-progress')).toHaveTextContent(/Recording live: 0:0\d of 0:30/);
      expect(browserRecorder.made[0].state).toBe('recording');
      // Thirty seconds of flight, in real time.
      for (let tries = 0; tries < 80 && saveBlob.mock.calls.length === 0; tries += 1) {
        await pass(500);
      }
      expect(saveBlob).toHaveBeenCalledTimes(1);
      expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-round-the-cave-\d{4}-\d{2}-\d{2}\.webm$/);
      expect((saveBlob.mock.calls[0][0] as Blob).type).toBe('video/webm;codecs=vp9');
      expect(browserRecorder.made[0].options.mimeType).toBe('video/webm;codecs=vp9');
      expect(browserRecorder.streams).toEqual([30]);
      expect(screen.getByTestId('flyby-movie-report')).toHaveTextContent(/frames recorded in 0:30: about [\d.]+ a second\./);
      // Nothing frame-exact was opened.
      expect(encoding.opened).toHaveLength(0);
      expect(scene.capture.frames).toBeGreaterThan(30);
      expect(scene.capture.open).toBeNull();
      expect(scene.camera()).toEqual(HOME);
      expect(onActiveChange).toHaveBeenLastCalledWith(false);
    });

    it('asks before a close throws a live recording away, and when stopped saves nothing and hands the view back', async () => {
      useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'webm', level: 'live', size: '640x360' } });
      const { scene, onActiveChange } = renderFlyby();
      await openMovie();
      fireEvent.click(screen.getByTestId('flyby-movie-export'));
      await pass(1000);
      expect(screen.getByTestId('flyby-transport').parentElement).toHaveAttribute('inert');
      fireEvent.keyDown(screen.getByTestId('flyby-movie-dialog'), { key: 'Escape' });
      await pass(50);
      fireEvent.click(screen.getAllByRole('button', { name: 'Stop and close' }).at(-1)!);
      await pass(300);
      expect(screen.queryByTestId('flyby-movie-dialog')).not.toBeInTheDocument();
      expect(browserRecorder.made[0].state).toBe('inactive');
      expect(saveBlob).not.toHaveBeenCalled();
      expect(scene.capture.open).toBeNull();
      expect(scene.camera()).toEqual(HOME);
      expect(onActiveChange).toHaveBeenLastCalledWith(false);
    });
  });

  it('refuses a GIF longer than a GIF may be, and says what to do', async () => {
    useFlybyStore.setState({ movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, format: 'gif', size: '480x270', fps: 25 } });
    renderFlyby();
    await openMovie();
    expect(screen.getByTestId('flyby-movie-gif-too-long')).toHaveTextContent('at most 600 frames, and this one would have 750');
    expect(screen.getByTestId('flyby-movie-export')).toBeDisabled();
  });
});
