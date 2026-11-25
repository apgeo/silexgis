// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import type { FlybyBundleRequest } from '../../../api/hooks/flybyBundle.ts';
import type { FlybyLayerKey } from '../../../flyby/flybyDocument.ts';
import type { FlybyLookTarget } from '../../../flyby/flybyLookApply.ts';
import { DEFAULT_FLYBY_MOVIE_SETTINGS } from '../../../flyby/flybyMovie.ts';
import type { MovieFormatSupport } from '../../../movie/encode/movieEncoder.ts';
import type { CaveInView3D } from '../../../scene3d/centerlines3d.ts';
import type { Scene3DCameraState, Scene3DCore } from '../../../scene3d/scene3dEngine.ts';
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import type { WorkspaceSelection } from '../../../stores/workspaceStore.ts';
import Scene3DFlyby from './Scene3DFlyby.tsx';
import { useFlybyDraftStore } from './flybyDraft.ts';
// Loaded beforehand, so the panel's own chunk is already in hand when the tool asks for it.
import './FlybyMovieDialog.tsx';
import type { FlybyScene } from './useFlyby.ts';

// The movie panel's other way out: a flyby handed over as a bundle for Blender. The scene is a
// plain object and the server is a function that writes down what it was asked; what the server
// makes of the request is its own tests' to say. Here it is what the panel says before the bundle
// is made — where it is rendered, at what rate, what it leaves out, which walls it asks about —
// and what it sends.

vi.mock('../../../api/hooks.ts', () => ({
  useCaveNames: (ids: readonly string[]) => new Map(ids.map((id) => [id, `Cave ${id.toUpperCase()}`])),
  caveQuery: (id: string) => ({
    queryKey: ['cave', id],
    queryFn: async () => ({ geom: { type: 'Point', coordinates: [25, 45.5] }, depth: 120, surveyedLength: 800 }),
  }),
  useMapLayers: () => ({ data: [] }),
  useSavedFilters: () => ({ data: [] }),
  useFeatureTypes: () => ({ data: [] }),
  useSearch: () => ({ data: undefined }),
  // The two routes a layer of entrances or features is drawn from, as the export asks them.
  fetchEntranceFeatures: async (bbox: string, zoom: number, params: object) => {
    layersAsked.push({ route: 'entrances', bbox, zoom, params });
    return {
      type: 'FeatureCollection',
      features: [
        { type: 'Feature', geometry: { type: 'Point', coordinates: [25.001, 45.501] }, properties: { id: 'e-own', caveId: 'a', name: 'Mouth' } },
        { type: 'Feature', geometry: { type: 'Point', coordinates: [25.011, 45.501] }, properties: { id: 'e-next', caveId: 'n' } },
        // Shown only approximately, and several in one: neither is a place to stand a marker on.
        { type: 'Feature', geometry: { type: 'Point', coordinates: [25.05, 45.55] }, properties: { id: 'e-moved', caveId: 'x', approximate: true } },
        { type: 'Feature', geometry: { type: 'Point', coordinates: [25.02, 45.52] }, properties: { cluster: true, count: 4 } },
      ],
    };
  },
  fetchMapFeatures: async (bbox: string, params: object) => {
    layersAsked.push({ route: 'features', bbox, params });
    return {
      type: 'FeatureCollection',
      features: [
        { type: 'Feature', geometry: { type: 'Point', coordinates: [25.002, 45.502] }, properties: { id: 'f-point' } },
        { type: 'Feature', geometry: { type: 'LineString', coordinates: [[25, 45.5], [25.01, 45.51]] }, properties: { id: 'f-line' } },
        { type: 'Feature', geometry: null, properties: { id: 'f-withheld', approximate: true } },
      ],
    };
  },
}));
/** What the export asked the two layer routes. */
const layersAsked: { route: string; bbox: string; zoom?: number; params: object }[] = [];
vi.mock('../../../api/hooks/mapLayers.ts', () => ({
  useRasterMapLayers: () => ({ data: [] }),
  useGeofileLayers: () => ({ data: [] }),
}));
vi.mock('../../../viewlinks/geoTargets.ts', () => ({
  APPROXIMATE_SPAN_DEGREES: 0.05,
  geometryFor: async () => null,
}));

/** What the server was asked, and what it answers with. */
const server = {
  asked: [] as { url: string; body: FlybyBundleRequest }[],
  answer: (): Headers | Promise<Headers> =>
    new Headers({ 'X-SilexGis-Bundle-Walls': '1', 'X-SilexGis-Bundle-Lines': '0', 'X-SilexGis-Bundle-Not-Drawn': '0', 'X-SilexGis-Bundle-Walls-Left-Out': '0' }),
};
vi.mock('../../../api/download.ts', async (original) => ({
  ...(await original<typeof import('../../../api/download.ts')>()),
  saveBlob: vi.fn(),
  downloadFilePostWithHeaders: async (url: string, body: FlybyBundleRequest) => {
    server.asked.push({ url, body });
    return server.answer();
  },
}));
const formats: MovieFormatSupport[] = [
  { format: 'gif', supported: true, codec: null },
  { format: 'webm', supported: true, codec: 'vp09.00.10.08' },
  { format: 'mp4', supported: true, codec: 'avc1.42001f' },
];
vi.mock('../../../movie/encode/movieEncoder.ts', async (original) => ({
  ...(await original<typeof import('../../../movie/encode/movieEncoder.ts')>()),
  probeMovieFormats: async () => formats,
}));

const HOME: Scene3DCameraState = { longitude: 24.9, latitude: 45.4, height: 3000, heading: 0, pitch: -40, roll: 0 };
const drawnCave: CaveInView3D = { caveId: 'a', label: 'Line name', bounds: [25, 45.5, 25.004, 45.504], top: 990, bottom: 800 };
const neighbour: CaveInView3D = { caveId: 'n', label: 'Next door', bounds: [25.01, 45.5, 25.012, 45.502], top: 950, bottom: 900 };

function layOut(element: Element): DOMRect {
  const rect = (left: number, top: number, width: number, height: number) =>
    ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top }) as DOMRect;
  if (element.classList.contains('scene3d-wrap')) return rect(0, 0, 1200, 700);
  if (element.classList.contains('scene3d-flyby-movie')) return rect(8, 8, 360, 612);
  if (element.classList.contains('scene3d-flyby-transport')) return rect(320, 620, 560, 48);
  return rect(0, 0, 0, 0);
}

/** What the viewer has switched on, as the view would answer it. */
interface Viewer {
  layers?: Partial<Record<FlybyLayerKey, boolean>>;
  listed?: FlybyLayerKey[];
  relief?: number;
}

function fakeScene(extra: Partial<FlybyScene> = {}, viewer: Viewer = {}) {
  let camera = HOME;
  const wrap = document.createElement('div');
  wrap.className = 'scene3d-wrap';
  const surface = document.createElement('div');
  wrap.appendChild(surface);
  document.body.appendChild(wrap);
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
  } as unknown as Scene3DCore;
  const look: FlybyLookTarget = {
    viewerLook: (layers) => ({
      backgroundOpacity: 1,
      surface: 'overlay',
      lens: 60,
      relief: viewer.relief ?? 1,
      layers: Object.fromEntries(
        layers.map((key) => [key, { visible: viewer.layers?.[key] ?? viewer.listed?.includes(key) ?? false, opacity: 1 }]),
      ),
    }),
    viewerLayers: () => viewer.listed ?? [],
    showBackground: () => undefined,
    showLayer: () => undefined,
    showWalls: () => undefined,
    showLines: () => undefined,
    showSurface: () => undefined,
    showSky: () => undefined,
    showRelief: () => undefined,
    requestRender: () => undefined,
  };
  const scene: FlybyScene & { camera(): Scene3DCameraState } = {
    engine: () => engine,
    cavesInView: () => [drawnCave, neighbour],
    refreshData: () => undefined,
    dataLoading: () => false,
    wrap: () => wrap,
    surface: () => surface,
    look,
    altitudePlacement: () => ({ absolute: true, offsetM: 37.5 }),
    caveSurveyTop: (caveId) => (caveId === 'a' ? 985 : undefined),
    camera: () => camera,
    ...extra,
  };
  return scene;
}

const WALLS_AND_LINES: Viewer = { layers: { walls: true, centerlines: true } };

function renderFlyby(scene = fakeScene({}, WALLS_AND_LINES), selection: WorkspaceSelection | null = { kind: 'cave', caveId: 'a' }) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <App>
        <Scene3DFlyby scene={scene} selection={selection} onActiveChange={vi.fn()} onClose={vi.fn()} />
      </App>
    </QueryClientProvider>,
  );
  return scene;
}

async function pass(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

/** Opens the movie panel and chooses the Blender bundle in it. */
async function openBundle() {
  fireEvent.click(screen.getByTestId('flyby-movie'));
  for (let tries = 0; tries < 50 && !screen.queryByTestId('flyby-movie-dialog'); tries += 1) {
    await pass(20);
  }
  await pass(20);
  fireEvent.click(screen.getByTestId('flyby-movie-format-blender'));
  await pass(20);
}

/** Presses the button and waits until the server has answered, or the panel has said why not. */
async function download(times = 1) {
  fireEvent.click(screen.getByTestId('flyby-movie-export'));
  for (
    let tries = 0;
    tries < 100 && server.asked.length < times && !screen.queryByTestId('flyby-movie-failed');
    tries += 1
  ) {
    await pass(50);
  }
  await pass(50);
}

const leftOut = () =>
  within(screen.getByTestId('flyby-bundle-left-out'))
    .getAllByRole('listitem')
    .map((item) => item.getAttribute('data-testid')?.replace('flyby-bundle-left-out-', ''));

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame', 'performance'] });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    return layOut(this);
  });
  useFlybyStore.setState({ library: [], loadRoute: false, movie: { ...DEFAULT_FLYBY_MOVIE_SETTINGS, fps: 12.5 } });
  useFlybyDraftStore.setState({ draft: null });
  server.asked = [];
  server.answer = () =>
    new Headers({ 'X-SilexGis-Bundle-Walls': '1', 'X-SilexGis-Bundle-Lines': '1', 'X-SilexGis-Bundle-Not-Drawn': '0', 'X-SilexGis-Bundle-Walls-Left-Out': '0' });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
  document.body.replaceChildren();
});

describe('a flyby as a bundle for Blender', () => {
  it('is offered beside the movie formats, and says it is rendered on your own machine at one frame rate', async () => {
    renderFlyby();
    await openBundle();

    expect(screen.getByTestId('flyby-movie-format-blender').closest('label')).toHaveTextContent('Blender bundle (.zip)');
    expect(screen.getByTestId('flyby-movie-format-help')).toHaveTextContent(
      'Unpack it and render it on your own machine with Blender 4.2 or newer — nothing is rendered here.',
    );
    // The movie was set to twelve and a half frames a second, which a bundle cannot be played at.
    expect(screen.getByTestId('flyby-bundle-fps')).toHaveTextContent('A bundle is made at 25 frames a second.');
    expect(screen.getByTestId('flyby-movie-summary')).toHaveTextContent('750 frames of 1280 × 720 · 0:30');
    // How a frame is taken is a question about filming, and nothing is filmed.
    expect(screen.queryByTestId('flyby-movie-level')).not.toBeInTheDocument();
    expect(screen.queryByTestId('flyby-movie-draft')).not.toBeInTheDocument();
    expect(screen.getByTestId('flyby-movie-privacy')).toHaveTextContent('It holds no coordinates, no terrain and no map.');
    expect(screen.getByTestId('flyby-movie-export')).toHaveTextContent('Download the bundle');

    // And back: the movie's own choices return with its format.
    fireEvent.click(screen.getByTestId('flyby-movie-format-mp4'));
    await pass(20);
    expect(screen.getByTestId('flyby-movie-level')).toBeInTheDocument();
    expect(screen.queryByTestId('flyby-bundle-left-out')).not.toBeInTheDocument();
    expect(screen.getByTestId('flyby-movie-privacy')).toHaveTextContent('The movie shows where these caves are.');
  });

  it('says before it is made what of the flyby it leaves out', async () => {
    renderFlyby(
      fakeScene({}, { layers: { walls: true, centerlines: true, entrances: true, scans: true }, listed: ['file:track-1', 'raster:old-map'] }),
    );
    await openBundle();

    // The ground always; then what the viewer has switched on that a bundle has no place for.
    // The entrances the viewer has on are not on the list: they go in as markers.
    expect(leftOut()).toEqual(['ground', 'scans', 'mapLayers', 'tracks']);
    expect(screen.getByTestId('flyby-bundle-left-out-ground')).toHaveTextContent(
      'The ground and the map drawn on it: the caves stand in empty space, under a plain sky.',
    );
    expect(screen.getByTestId('flyby-bundle-left-out')).toHaveTextContent('The bundle is made anyway, and its README says the same.');
    expect(screen.getByTestId('flyby-bundle-pointed')).toHaveTextContent(
      'the entrances and the features that are points go in as markers',
    );
    // And what a bundle does that the screen does not: its daylight goes underground.
    expect(screen.getByTestId('flyby-bundle-daylight')).toHaveTextContent(
      'In the render the sun and the sky go out while the camera is under the ground, and come back when it comes out. The lamp on the camera then lights the walls from as far as the nearest of them is; survey lines keep their colours in any light.',
    );
  });

  it('lists no change of the look as left out: a relief that grows while the flyby plays is sent as keys', async () => {
    renderFlyby();
    fireEvent.click(screen.getByText('Ready-made changes'));
    const factor = screen.getByRole('spinbutton', { name: 'The factor the relief rises to' });
    fireEvent.change(factor, { target: { value: '3' } });
    fireEvent.blur(factor);
    fireEvent.click(screen.getByTestId('flyby-recipe-relief-add'));
    await openBundle();

    // Only the ground: what changes during the flyby goes into the bundle.
    expect(leftOut()).toEqual(['ground']);

    await download();
    const { body } = server.asked[0];
    expect(body.leftOut).toEqual(['ground']);
    // Up to three times as tall and back, eased as the flyby eases: four keys after the first.
    const relief = body.changes?.relief ?? [];
    expect(relief.map((key) => key.value)).toEqual([1, 1, 3, 3, 1]);
    expect(relief.map((key) => key.easing)).toEqual([null, null, 'smooth', null, 'smooth']);
    expect(relief.every((key) => Number.isInteger(key.frame) && key.frame >= 0 && key.frame < body.frames.length)).toBe(true);
    // The camera's heights stay true; the mark over the picture follows the ramp.
    expect(body.relief).toBe(1);
    expect(body.texts?.cornerCaptions?.length).toBeGreaterThan(10);
  });

  it('sends the camera of every frame, the flyby\'s cave with its walls and the others in view by their lines', async () => {
    const scene = renderFlyby();
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'Round the cave' } });
    await openBundle();
    await download();

    expect(server.asked).toHaveLength(1);
    const { url, body } = server.asked[0];
    expect(url).toBe('/api/v1/flybys/blender-bundle');
    expect(body.name).toBe('Round the cave');
    expect(body.output).toEqual({ width: 1280, height: 720, fps: 25, quality: 'high' });
    // Thirty seconds at twenty-five a second, eight numbers a frame.
    expect(body.frames).toHaveLength(750);
    expect(body.frames.every((frame) => frame.length === 8 && frame.every(Number.isFinite))).toBe(true);
    expect(body.caves).toEqual([
      { caveId: 'a', walls: true, surveyTopM: 985, groundM: 1000 },
      { caveId: 'n', walls: false, surveyTopM: null, groundM: 1000 },
    ]);
    // Where the view draws its caves, as it stands.
    expect(body.placement).toEqual({ absolute: true, offsetM: 37.5 });
    expect(body.relief).toBe(1);
    expect(body.look?.lines).toBe(true);
    expect(body.look?.depthBands?.[0]).toMatchObject({ fromM: 0 });
    expect(body.leftOut).toEqual(['ground']);
    // The bundle's own notes are asked for in the language the reader has the application in.
    expect(body.language).toBe('en');
    // The camera stays above this scene's ground, so nothing is said of the daylight.
    expect(body.changes?.daylight ?? null).toBeNull();

    expect(screen.getByTestId('flyby-bundle-report')).toHaveTextContent('1 cave with its walls.');
    expect(screen.getByTestId('flyby-bundle-report')).toHaveTextContent('1 cave by its survey lines.');
    expect(screen.getByTestId('flyby-bundle-report')).toHaveTextContent('README.txt inside the bundle says how to render it');
    // Getting the flyby ready moved the camera; the view is handed back as it was found.
    expect(scene.camera()).toEqual(HOME);
    expect(screen.getByTestId('flyby-movie-export')).toBeEnabled();
  });

  it('takes a stretch of the flyby, and the relief the viewer has it drawn at', async () => {
    renderFlyby(fakeScene({}, { ...WALLS_AND_LINES, relief: 2 }));
    await openBundle();
    fireEvent.click(screen.getByLabelText('A stretch of it'));
    fireEvent.change(screen.getByTestId('flyby-movie-from'), { target: { value: '10' } });
    fireEvent.change(screen.getByTestId('flyby-movie-to'), { target: { value: '14' } });
    await pass(20);
    expect(screen.getByTestId('flyby-movie-summary')).toHaveTextContent('100 frames of 1280 × 720 · 0:04');

    await download();

    const { body } = server.asked[0];
    expect(body.frames).toHaveLength(100);
    expect(body.relief).toBe(2);
    // Said in a corner of the picture, in the reader's words.
    expect(body.texts?.cornerCaptions).toEqual([{ text: 'Relief ×2', startS: 0, endS: 4 }]);
  });

  it('asks again about walls the view did not download, with their size, and leaves them out unless told', async () => {
    const pending = [{ caveId: 'a', bytes: 60 * 1024 * 1024 }];
    renderFlyby(
      fakeScene({ walls: { pending: (caveIds) => pending.filter((walls) => caveIds.includes(walls.caveId)), agree: () => undefined } }, WALLS_AND_LINES),
    );
    await openBundle();

    const question = screen.getByTestId('flyby-bundle-walls');
    expect(question.closest('label')).toHaveTextContent('Include the walls of 1 cave that were not downloaded into the view (60 MB)');
    expect(question).not.toBeChecked();
    expect(leftOut()).toContain('wallsNotDownloaded');

    await download();
    expect(server.asked[0].body.caves[0]).toMatchObject({ caveId: 'a', walls: false });
    expect(server.asked[0].body.leftOut).toContain('wallsNotDownloaded');

    fireEvent.click(question);
    await pass(20);
    expect(leftOut()).not.toContain('wallsNotDownloaded');
    await download(2);
    expect(server.asked[1].body.caves[0]).toMatchObject({ caveId: 'a', walls: true });
    expect(server.asked[1].body.leftOut).not.toContain('wallsNotDownloaded');
  });

  it('names the entrances and features of the layers the flyby shows, asks for the photographs and can reveal the caves', async () => {
    server.answer = () =>
      new Headers({
        'X-SilexGis-Bundle-Walls': '1',
        'X-SilexGis-Bundle-Lines': '1',
        'X-SilexGis-Bundle-Not-Drawn': '0',
        'X-SilexGis-Bundle-Walls-Left-Out': '0',
        'X-SilexGis-Bundle-Markers': '3',
        'X-SilexGis-Bundle-Markers-Not-Drawn': '1',
        'X-SilexGis-Bundle-Photos': '2',
      });
    layersAsked.length = 0;
    const scene = fakeScene({}, { layers: { walls: true, centerlines: true, entrances: true, photos: true }, listed: ['filter:f1'] });
    scene.look!.markerSources = () => [
      { layer: 'entrances', kind: 'entrances', params: {}, colour: '#146262' },
      // Hidden by the viewer, and never shown by the flyby: not asked for at all.
      { layer: 'surface-features', kind: 'surfaceFeatures', params: {}, colour: '#7a5c1e' },
      { layer: 'filter:f1', kind: 'features', params: { savedFilterId: 'f1' }, colour: '#722ed1' },
    ];
    renderFlyby(scene);
    await openBundle();

    // Said before the file is made: what is pointed out, and what a reveal is.
    expect(leftOut()).toEqual(['ground', 'featureOutlines']);
    const reveal = screen.getByTestId('flyby-bundle-reveal');
    expect(reveal.closest('label')).toHaveTextContent('Let the caves appear part by part, from their entrances inwards');
    expect(reveal).not.toBeChecked();
    fireEvent.click(reveal);
    await pass(20);
    await download();

    // The window: the bounds of the caves the view draws, widened — and a zoom past any cluster.
    expect(layersAsked.map((asked) => [asked.route, asked.params])).toEqual([
      ['entrances', {}],
      ['entrances', { savedFilterId: 'f1' }],
      ['features', { savedFilterId: 'f1' }],
    ]);
    const [west, south, east, north] = layersAsked[0].bbox.split(',').map(Number);
    expect(west).toBeLessThan(25 - 0.003);
    expect(east).toBeGreaterThan(25.012 + 0.003);
    expect(south).toBeLessThan(45.5 - 0.002);
    expect(north).toBeGreaterThan(45.504 + 0.002);
    expect(layersAsked[0].zoom).toBeGreaterThanOrEqual(16);

    const body = server.asked[0].body;
    // Ids, the ground under each, and a name for an entrance of the flyby's own cave — first.
    expect(body.markers).toEqual([
      {
        kind: 'entrances',
        colour: '#146262',
        markers: [
          { featureId: 'e-own', groundM: 1000, named: true },
          { featureId: 'e-next', groundM: 1000, named: false },
        ],
        shown: null,
        opacity: null,
      },
      {
        kind: 'features',
        colour: '#722ed1',
        markers: [
          { featureId: 'e-own', groundM: 1000, named: true },
          { featureId: 'e-next', groundM: 1000, named: false },
          { featureId: 'f-point', groundM: 1000, named: false },
        ],
        shown: null,
        opacity: null,
      },
    ]);
    expect(body.photos).toEqual({ caves: ['a'], shown: null });
    expect(body.reveal).toMatchObject({ by: 'distance', startS: 0 });
    expect(body.leftOut).toEqual(['ground', 'featureOutlines']);

    expect(screen.getByTestId('flyby-bundle-report-markers')).toHaveTextContent('3 markers.');
    expect(screen.getByTestId('flyby-bundle-report-photos')).toHaveTextContent('2 photographs.');
    expect(screen.getByTestId('flyby-bundle-report-markers-not-drawn')).toHaveTextContent(
      '1 entrance or feature is not in it: nothing of it is available to draw.',
    );
  });

  it('says what the server left out of the bundle it made', async () => {
    server.answer = () =>
      new Headers({ 'X-SilexGis-Bundle-Walls': '0', 'X-SilexGis-Bundle-Lines': '1', 'X-SilexGis-Bundle-Not-Drawn': '1', 'X-SilexGis-Bundle-Walls-Left-Out': '1' });
    renderFlyby();
    await openBundle();
    await download();

    expect(screen.getByTestId('flyby-bundle-report-walls-left-out')).toHaveTextContent(
      'The walls of 1 cave were left out; it is drawn by its survey lines.',
    );
    // No reason: the server gives none, for a cave it may be keeping back.
    expect(screen.getByTestId('flyby-bundle-report-not-drawn')).toHaveTextContent(
      '1 cave is not in it: nothing of it is available to draw.',
    );
  });

  it('puts a refusal by the server into a sentence, and leaves the view as it was', async () => {
    const { DownloadError } = await import('../../../api/download.ts');
    server.answer = () => Promise.reject(new DownloadError(422, { code: 'flyby_bundle.nothing_to_draw' }));
    const scene = renderFlyby();
    await openBundle();
    await download();

    expect(screen.getByTestId('flyby-movie-failed')).toHaveTextContent(
      'Nothing of this flyby\'s caves is available to draw, so there is no bundle to make.',
    );
    expect(screen.queryByTestId('flyby-bundle-report')).not.toBeInTheDocument();
    expect(scene.camera()).toEqual(HOME);
  });

  it('is not made of a flyby about no cave, or of one whose look hides walls and lines alike', async () => {
    renderFlyby(fakeScene({}, WALLS_AND_LINES), null);
    await openBundle();
    expect(screen.getByTestId('flyby-bundle-refused')).toHaveTextContent('A bundle is made of caves, and this flyby is about none.');
    expect(screen.getByTestId('flyby-movie-export')).toBeDisabled();
    cleanup();
    // The tool keeps the flyby it held for the next time it is opened; this is another flyby.
    useFlybyDraftStore.setState({ draft: null });

    renderFlyby(fakeScene({}, { layers: { walls: false, centerlines: false } }));
    await openBundle();
    expect(screen.getByTestId('flyby-bundle-refused')).toHaveTextContent(
      'The walls and the survey lines are both hidden in this flyby\'s look, so nothing would be drawn.',
    );
    expect(screen.getByTestId('flyby-movie-export')).toBeDisabled();
    expect(server.asked).toHaveLength(0);
  });
});
